// Explicit optional-adapter verification. Uses real HTTPS cookies/AF and machine-signed callbacks.
import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import https from 'node:https';
import http from 'node:http';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { setTimeout } from 'node:timers/promises';
const directory = process.env.ORDERFLOW_DEPLOYMENT_VERIFY_CONFIG;
if (!directory) throw new Error('Set the isolated verification input directory.');
const env = Object.fromEntries((await readFile(path.join(directory, 'deployment.env'), 'utf8')).trim().split('\n').map(line => line.split(/=(.*)/s).slice(0, 2)));
assert.ok(env.COMPOSE_PROJECT_NAME.startsWith('orderflow-verify-'));
const origin = `https://localhost:${env.ORDERFLOW_DEPLOY_PORT}`;
const ca = await readFile(path.join(directory, 'tls.crt'));
const { DevelopmentSeed: { Account: account } } = JSON.parse(await readFile(path.join(directory, 'bootstrap.json'), 'utf8'));
const { Simulator: simulator } = JSON.parse(await readFile(path.join(directory, 'simulator.json'), 'utf8'));
function client(base, ca) {
  const cookies = new Map();
  return (method, route, data, headers = {}) => new Promise((resolve, reject) => {
    const url = new URL(route, base); const body = data === undefined ? undefined : typeof data === 'string' ? data : JSON.stringify(data);
    const request = (url.protocol === 'https:' ? https : http).request(url, { method, ca, headers: {
      ...(body === undefined ? {} : { 'Content-Type': 'application/json', 'Content-Length': Buffer.byteLength(body) }),
      Cookie: [...cookies].map(([key, value]) => `${key}=${value}`).join('; '), ...headers,
    } }, response => {
      for (const cookie of response.headers['set-cookie'] ?? []) { const pair = cookie.split(';')[0]; const index = pair.indexOf('='); cookies.set(pair.slice(0, index), pair.slice(index + 1)); }
      let value = ''; response.setEncoding('utf8'); response.on('data', chunk => { value += chunk; if (value.length > 262144) request.destroy(new Error('Bounded response exceeded')); });
      response.on('end', () => { let data; try { data = value ? JSON.parse(value) : null; } catch { reject(new Error(`Invalid JSON, HTTP ${response.statusCode}`)); return; } resolve({ status: response.statusCode, data, traceId: response.headers['x-trace-id'] }); });
    });
    request.setTimeout(10_000, () => request.destroy(new Error('Request timed out'))); request.on('error', reject); request.end(body);
  });
}
const admin = client(origin, ca), customer = client(origin, ca);
const provider = client(`http://127.0.0.1:${process.env.ORDERFLOW_DEPLOY_SIMULATOR_PORT ?? '5091'}`);
async function csrf(browser) { const response = await browser('GET', '/api/auth/antiforgery'); assert.equal(response.status, 200); return { 'X-CSRF-TOKEN': response.data.requestToken }; }
async function login(browser, email, password) { assert.equal((await browser('POST', '/api/auth/login', { email, password }, await csrf(browser))).status, 200); }
async function until(operation, condition, description) {
  const deadline = Date.now() + 45_000;
  do { const result = await operation(); if (condition(result)) return result; await setTimeout(100); } while (Date.now() < deadline);
  throw new Error(`Timed out observing ${description}`);
}
await login(admin, account.Email, account.Password);
const adminCsrf = await csrf(admin);
const category = await admin('POST', '/api/admin/categories', { name: `Demo ${randomUUID()}` }, adminCsrf); assert.equal(category.status, 201);
const product = await admin('POST', '/api/admin/products', { sku: `DEMO-${randomUUID()}`, name: 'Demo payment', description: '', price: '12.50', categoryId: category.data.id }, adminCsrf); assert.equal(product.status, 201);
assert.equal((await admin('POST', `/api/admin/products/${product.data.id}/stock-receipts`, { operationId: randomUUID(), quantity: 1, reason: 'Optional adapter verification' }, adminCsrf)).status, 201);
const email = `demo-${randomUUID()}@example.invalid`, password = 'Fixture-Only-Customer-123!';
assert.equal((await customer('POST', '/api/auth/register', { email, password }, await csrf(customer))).status, 201);
await login(customer, email, password); const customerCsrf = await csrf(customer);
const order = await customer('POST', '/api/orders', { items: [{ productId: product.data.id, quantity: 1 }] }, { ...customerCsrf, 'Idempotency-Key': randomUUID() }); assert.equal(order.status, 201);
const payment = await customer('POST', `/api/orders/${order.data.id}/payment-attempts`, {}, { ...customerCsrf, 'Idempotency-Key': randomUUID() }); assert.equal(payment.status, 202);
const key = `payment-${payment.data.id.replaceAll('-', '')}`; // Existing documented simulator identity, not a browser API field.
const controls = { 'X-Simulator-Control-Key': simulator.ControlKey };
const merchant = { 'X-Simulator-Key': simulator.ApiKey };
await until(() => provider('GET', `/provider/payments/${key}`, undefined, merchant), response => response.status === 200, 'provider submission');
assert.equal((await provider('POST', `/control/payments/${key}/resolve`, { state: 'Succeeded' }, controls)).status, 204);
const signed = await provider('POST', `/control/payments/${key}/callback`, undefined, controls); assert.equal(signed.status, 200);
assert.equal((await customer('POST', '/api/payments/simulator/callback', signed.data.body, {
  'X-Payment-Key-Id': signed.data.keyId, 'X-Payment-Timestamp': String(signed.data.timestamp), 'X-Payment-Signature': signed.data.signature,
})).status, 202);
await until(() => customer('GET', `/api/orders/${order.data.id}`), response => response.status === 200 && response.data.status === 'Confirmed', 'verified settlement');
const financial = await customer('GET', `/api/orders/${order.data.id}/payment-status`); assert.equal(financial.status, 200);
assert.equal(financial.data.captureCount, 1); assert.equal(financial.data.captures[0].amount, '12.50'); assert.equal(financial.data.captures[0].allocated, true);
const mailpit = client(`http://127.0.0.1:${process.env.ORDERFLOW_DEPLOY_MAIL_PORT ?? '8026'}`);
await until(() => mailpit('GET', `/api/v1/search?query=${encodeURIComponent(`to:${email}`)}`), response => response.status === 200 && response.data.messages?.length >= 1, 'Mailpit acceptance');
if (process.env.ORDERFLOW_VERIFY_TRACE_FILE) await writeFile(process.env.ORDERFLOW_VERIFY_TRACE_FILE, JSON.stringify({ traceId: order.traceId, orderId: order.data.id }));
console.log('PASS optional real simulator submission, signed capture, Confirmed allocation and .invalid Mailpit email; no browser secret or redirect proves payment.');
