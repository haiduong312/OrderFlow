// Automated rehearsal of the documented human demo, using the actual built UI and services.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFile, writeFile } from 'node:fs/promises';
import { createHash, createPublicKey } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import path from 'node:path';
const require = createRequire(new URL('../../client/package.json', import.meta.url));
const { chromium, expect: baseExpect } = require('@playwright/test');
const expect = baseExpect.configure({ timeout: 45_000 });
const directory = process.env.ORDERFLOW_DEMO_DIRECTORY;
assert.ok(directory, 'Set ORDERFLOW_DEMO_DIRECTORY to the freshly seeded disposable demo');
const root = path.resolve(directory);
const run = (...args) => execFileSync('python3', ['infra/demo/demo.py', ...args, '--directory', root],
    { timeout: 150_000, stdio: ['ignore', 'pipe', 'pipe'] }).toString();
run('check'); // Validate target/DB marker before reading any credentials or browsing.
const fixture = JSON.parse(await readFile(path.join(root, 'demo.json'), 'utf8'));
assert.ok(!fixture.rehearsalStarted, 'Fresh fixture required; never silently repeat UI checkout');
fixture.rehearsalStarted = true;
await writeFile(path.join(root, 'demo.json'), JSON.stringify(fixture, null, 2), { mode: 0o600 });
const { DevelopmentSeed: { Account: admin } } = JSON.parse(await readFile(path.join(root, 'bootstrap.json'), 'utf8'));
const pin = createHash('sha256').update(createPublicKey(await readFile(path.join(root, 'tls.crt'))).export({ type: 'spki', format: 'der' })).digest('base64');
const browser = await chromium.launch({ headless: true, args: [`--ignore-certificate-errors-spki-list=${pin}`] });
const start = performance.now();
let step = 'browser setup';
try {
    const customer = await browser.newContext({ baseURL: 'https://localhost:18448' });
    const operator = await browser.newContext({ baseURL: 'https://localhost:18448' });
    const page = await customer.newPage(), adminPage = await operator.newPage();
    customer.setDefaultTimeout(30_000); operator.setDefaultTimeout(30_000);
    async function get(page, route) {
        // Use the browser's pinned TLS session; APIRequestContext has a separate CA store.
        return page.evaluate(async (route) => {
            const response = await fetch(route);
            return { status: response.status, data: await response.json() };
        }, route);
    }
    async function login(page, email, password) {
        await page.goto('/login');
        await page.getByLabel('Email address', { exact: true }).fill(email);
        await page.getByLabel('Password', { exact: true }).fill(password);
        await page.locator('form button[type=submit]').click();
        await expect(page).toHaveURL(/\/account$/);
    }
    step = 'Customer login, restore and catalog';
    await login(page, fixture.customer.email, fixture.customer.password);
    await page.reload();
    await expect(page.locator('.account-person h2')).toHaveText(fixture.customer.email);
    await page.goto('/products');
    await expect(page.getByRole('link', { name: 'Demo Browse', exact: true })).toBeVisible();
    await page.goto('/products/' + fixture.products.browse);
    await expect(page.getByRole('button', { name: 'Add to cart', exact: true })).toBeEnabled();
    await page.getByLabel('Quantity for Demo Browse', { exact: true }).fill('1');
    await page.getByRole('button', { name: 'Add to cart', exact: true }).click();
    await page.getByRole('link', { name: 'View your cart', exact: true }).click();
    await expect(page.getByTestId('cart-total')).toHaveText('$12.50');
    await page.getByRole('link', { name: 'Continue to checkout', exact: true }).click();
    step = 'checkout';
    await page.getByRole('button', { name: 'Create order & reserve stock', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Order created', exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'View order & finish checkout', exact: true }).click();
    await expect(page.getByTestId('order-status')).toHaveText('PendingPayment');
    const orderId = new URL(page.url()).pathname.split('/').at(-1);
    assert.match(orderId, /^[a-f0-9-]{36}$/);
    await expect(page.getByTestId('live-state')).toHaveText('Live updates connected');
    await page.getByRole('button', { name: 'Start payment', exact: true }).click();
    await expect(page.locator('.payment-activity article').filter({ hasText: /Queued|Pending/ })).toBeVisible();
    step = 'operator simulator capture';
    console.log(run('pay', '--order', orderId).trim());
    // No reload here: durable event/SignalR invalidation (with polling fallback) refreshes this UI.
    await expect(page.getByTestId('order-status')).toHaveText('Confirmed');
    const financial = (await get(page, `/api/orders/${orderId}/payment-status`)).data;
    expect(financial.captureCount).toBe(1);
    expect(financial.captures[0].allocated).toBe(true);
    expect(financial.captures[0].amount).toBe('12.50');
    expect((await get(page, `/api/admin/orders/${orderId}`)).status).toBe(403);
    step = 'Admin fulfillment and Customer live refresh';
    await login(adminPage, admin.Email, admin.Password);
    await adminPage.goto('/admin/orders/' + orderId);
    await expect(adminPage.getByTestId('admin-order-state')).toHaveText('Confirmed');
    for (const state of ['Processing', 'Shipped', 'Completed']) {
        await adminPage.getByLabel('Fulfillment reason (optional)', { exact: true }).fill('Synthetic demo ' + state);
        await adminPage.getByRole('button', { name: 'Advance to ' + state, exact: true }).click();
        await expect(adminPage.getByTestId('admin-order-state')).toHaveText(state);
    }
    await expect(page.getByTestId('order-status')).toHaveText('Completed');
    await page.goto('/notifications');
    await expect.poll(async () => (await get(page, '/api/notifications')).data.totalCount).toBeGreaterThanOrEqual(5);
    step = 'duplicate replay';
    console.log(run('duplicate').trim());
    step = 'broker outage recovery';
    console.log(run('outage').trim());
    step = 'responsive navigation';
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto('/products');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
    const result = { passed: true, wall_seconds: (performance.now() - start) / 1000,
        checks: ['restore', 'catalog/cart', 'checkout', 'simulator capture', 'admin lifecycle', 'live connected and durable refresh', 'Customer admin denial', 'same-key replay', 'broker recovery', '390px navigation'],
        realtime_limit: 'Connected socket + UI/durable catch-up observed; existing transport tests separately prove event delivery, polling fallback remains enabled.' };
    await writeFile(path.join(root, 'results/rehearsal.json'), JSON.stringify(result, null, 2));
    console.log('PASS actual disposable portfolio demo; no fake API responses, browser control secrets, trace files or screenshots.');
} catch {
    // Playwright/Node exceptions can contain request cookies or typed credentials.
    // Never print arbitrary messages/stacks/call logs, even for generated fixtures.
    console.error('FAIL portfolio demo at fixed step: ' + step + '; inspect owned service state locally.');
    process.exitCode = 1;
} finally { await browser.close(); }
