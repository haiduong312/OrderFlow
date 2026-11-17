// Real built frontend, secure cookies and same-origin API. Trust only this fixture's TLS public key.
import { createRequire } from 'node:module';
import { readFile } from 'node:fs/promises';
import { createHash, createPublicKey } from 'node:crypto';
import path from 'node:path';
const require = createRequire(new URL('../../client/package.json', import.meta.url));
const { chromium, expect } = require('@playwright/test');
const directory = process.env.ORDERFLOW_DEPLOYMENT_VERIFY_CONFIG;
if (!directory) throw new Error('Set ORDERFLOW_DEPLOYMENT_VERIFY_CONFIG to the isolated verification input directory.');
const env = Object.fromEntries((await readFile(path.join(directory, 'deployment.env'), 'utf8')).trim().split('\n').map(line => line.split(/=(.*)/s).slice(0, 2)));
if (!env.COMPOSE_PROJECT_NAME.startsWith('orderflow-verify-')) throw new Error('Only a dedicated verification project is allowed.');
const pem = await readFile(path.join(directory, 'tls.crt'));
const pin = createHash('sha256').update(createPublicKey(pem).export({ type: 'spki', format: 'der' })).digest('base64');
const { DevelopmentSeed: { Account: account } } = JSON.parse(await readFile(path.join(directory, 'bootstrap.json'), 'utf8'));
const browser = await chromium.launch({ headless: true, args: [`--ignore-certificate-errors-spki-list=${pin}`] });
try {
  const context = await browser.newContext({ baseURL: `https://localhost:${env.ORDERFLOW_DEPLOY_PORT}`, viewport: { width: 1440, height: 1000 } });
  const page = await context.newPage();
  await page.goto('/login');
  await page.getByLabel('Email address', { exact: true }).fill(account.Email);
  await page.getByLabel('Password', { exact: true }).fill(account.Password);
  await page.locator('form button[type=submit]').click();
  await expect(page).toHaveURL(/\/(account|orders)(\?|$)/);
  await page.reload();
  await expect(page.locator('.account-person h2')).toHaveText(account.Email);
  const cookie = (await context.cookies()).find(value => value.name === '__Host-OrderFlow.Auth');
  expect(cookie?.secure).toBe(true); expect(cookie?.httpOnly).toBe(true);
  const nodes = await page.evaluate(async () => {
    const results = [];
    for (let i = 0; i < 4; i++) { const response = await fetch('/api/auth/me'); if (!response.ok) throw new Error(`me ${response.status}`); results.push(response.headers.get('X-OrderFlow-Instance')); }
    return [...new Set(results)].sort();
  });
  expect(nodes).toEqual(['api1', 'api2']);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/products');
  await expect(page.getByRole('main')).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  console.log('PASS real built frontend: HTTPS login/session restore, secure HttpOnly cookie across api1/api2, SPA history routing and 390px navigation.');
  await context.close();
} finally { await browser.close(); }
