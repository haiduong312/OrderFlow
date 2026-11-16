import { expect, test } from '@playwright/test';
import type { APIRequestContext, BrowserContext, Page } from '@playwright/test';
function required(name: string) {
    const value = process.env[name];
    if (!value) throw new Error(`Run the real Admin browser harness (${name} missing).`);
    return value;
}
async function csrf(request: APIRequestContext) {
    const response = await request.get('/api/auth/antiforgery');
    expect(response.status()).toBe(200);
    return (await response.json()) as { headerName: string; requestToken: string };
}
async function login(page: Page, admin = true) {
    await page.goto('/login');
    await page
        .getByLabel('Email address', { exact: true })
        .fill(required(admin ? 'ORDERFLOW_BROWSER_ADMIN' : 'ORDERFLOW_BROWSER_CUSTOMER_A'));
    await page.getByLabel('Password', { exact: true }).fill(required('ORDERFLOW_BROWSER_PASSWORD'));
    await page.locator('form button[type=submit]').click();
    await expect(page).toHaveURL(/\/account$/);
}
async function apiContext(page: Page, admin: boolean): Promise<BrowserContext> {
    const context = await page
        .context()
        .browser()!
        .newContext({ baseURL: test.info().project.use.baseURL as string });
    const token = await csrf(context.request);
    const response = await context.request.post('/api/auth/login', {
        data: {
            email: required(admin ? 'ORDERFLOW_BROWSER_ADMIN' : 'ORDERFLOW_BROWSER_CUSTOMER_A'),
            password: required('ORDERFLOW_BROWSER_PASSWORD'),
        },
        headers: { [token.headerName]: token.requestToken },
    });
    expect(response.status()).toBe(200);
    return context;
}
async function order(page: Page, payment = false) {
    const customer = await apiContext(page, false);
    try {
        const token = await csrf(customer.request);
        const response = await customer.request.post('/api/orders', {
            data: { items: [{ productId: required('ORDERFLOW_BROWSER_PRODUCT'), quantity: 2 }] },
            headers: { [token.headerName]: token.requestToken, 'Idempotency-Key': crypto.randomUUID() },
        });
        expect(response.status()).toBe(201);
        const created = await response.json();
        if (payment) {
            const result = await customer.request.post(`/api/orders/${created.id}/payment-attempts`, {
                data: {},
                headers: { [token.headerName]: token.requestToken, 'Idempotency-Key': crypto.randomUUID() },
            });
            expect(result.status()).toBe(202);
        }
        return created as { id: string; total: string };
    } finally {
        await customer.close();
    }
}
async function control(page: Page, action: string) {
    expect(
        (
            await page.request.post(`/api/_checkout-ui/${action}`, {
                headers: { 'X-Checkout-Test-Control': required('ORDERFLOW_CHECKOUT_CONTROL') },
            })
        ).status(),
    ).toBe(204);
}
async function nav(page: Page, name: string) {
    await page
        .getByRole('navigation', { name: 'Admin navigation' })
        .getByRole('link', { name, exact: true })
        .click();
    const heading: Record<string, string> = {
        Products: 'Products',
        Categories: 'Categories',
        Orders: 'Find and manage orders',
        Reports: 'Sales and stock reports',
        Audit: 'Read-only audit history',
        'Email failures': 'Email failures',
    };
    await expect(page.getByRole('heading', { name: heading[name]!, exact: true })).toBeVisible();
}
async function range(page: Page) {
    await page.getByLabel('From UTC date (inclusive)', { exact: true }).fill('2035-01-01');
    await page.getByLabel('To UTC date (exclusive)', { exact: true }).fill('2035-01-02');
}
async function operation(page: Page) {
    const me = await (await page.request.get('/api/auth/me')).json();
    return page.evaluate(
        (id) => JSON.parse(localStorage.getItem(`orderflow-admin-command:v1:${id}`)!),
        me.id,
    );
}

test('admin_catalog_management', async ({ page }) => {
    await login(page);
    await page.getByRole('link', { name: 'Admin workspace', exact: true }).click();
    await nav(page, 'Categories');
    await page.getByRole('button', { name: 'Create category', exact: true }).click();
    await page.getByLabel('Category name', { exact: true }).fill('Office supplies');
    await page.getByRole('button', { name: 'Save category', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Office supplies', exact: true })).toBeVisible();
    await nav(page, 'Products');
    await page.getByRole('link', { name: 'Create product', exact: true }).click();
    await page.getByLabel('SKU', { exact: true }).fill('ADMIN-' + crypto.randomUUID().slice(0, 8));
    await page.getByLabel('Product name', { exact: true }).fill('Admin created desk');
    await page.getByLabel('Description', { exact: true }).fill('Useful office desk.');
    await page.getByLabel('Price USD', { exact: true }).fill('12.30');
    await page.getByLabel('Active category', { exact: true }).selectOption({ label: 'Office supplies' });
    await page.getByRole('button', { name: 'Create product', exact: true }).click();
    await expect(page).toHaveURL(/\/admin\/products\/[0-9a-f-]+$/);
    const id = new URL(page.url()).pathname.split('/').at(-1)!;
    await expect(page.getByLabel('Price USD', { exact: true })).toHaveValue('12.30');
    await page.getByLabel('Price USD', { exact: true }).fill('15.10');
    await page.getByRole('button', { name: 'Save product', exact: true }).click();
    await expect(page.getByText('Version 2 ·', { exact: false })).toBeVisible();
    await expect(page.getByLabel('Price USD', { exact: true })).toHaveValue('15.10');
    await page.getByRole('link', { name: 'Stock and movements', exact: true }).click();
    await expect(page.getByTestId('stock-on-hand')).toHaveText('0');
    await page.getByLabel('Receipt quantity', { exact: true }).fill('12');
    await page.getByLabel('Stock reason', { exact: true }).fill('Initial shelf receipt');
    await page.getByRole('button', { name: 'Submit stock operation', exact: true }).click();
    await expect(page.getByTestId('stock-on-hand')).toHaveText('12');
    await page.getByLabel('Stock operation', { exact: true }).selectOption('adjustment');
    await page.getByLabel('Adjustment delta', { exact: true }).fill('-2');
    await page.getByLabel('Stock reason', { exact: true }).fill('Damaged units counted');
    await page.getByRole('button', { name: 'Submit stock operation', exact: true }).click();
    await expect(page.getByTestId('stock-on-hand')).toHaveText('10');
    await page.getByRole('link', { name: '← Product details', exact: true }).click();
    await page.getByRole('button', { name: 'Archive product', exact: true }).click();
    await expect(page.getByLabel('Product active', { exact: true })).not.toBeChecked();
    expect((await page.request.get(`/api/products/${id}`)).status()).toBe(404);
    await page.getByLabel('Product active', { exact: true }).check();
    await page.getByRole('button', { name: 'Save product', exact: true }).click();
    await expect(page.getByRole('button', { name: 'Archive product', exact: true })).toBeVisible();
    expect((await page.request.get(`/api/products/${id}`)).status()).toBe(200);
    await nav(page, 'Categories');
    await page.getByRole('button', { name: 'Office supplies', exact: true }).click();
    await page.getByRole('button', { name: 'Archive category', exact: true }).click();
    await expect(
        page.getByRole('heading', { name: 'Edit category: Office supplies', exact: true }),
    ).toHaveCount(0);
    expect((await page.request.get(`/api/products/${id}`)).status()).toBe(404);
    await page.getByRole('button', { name: 'Office supplies', exact: true }).click();
    await page.getByLabel('Category active', { exact: true }).check();
    const restored = page.waitForResponse(
        (response) =>
            /\/api\/admin\/categories\/[^/]+$/.test(new URL(response.url()).pathname) &&
            response.request().method() === 'PUT',
    );
    await page.getByRole('button', { name: 'Save category', exact: true }).click();
    expect((await restored).status()).toBe(200);
    await expect(
        page.getByRole('heading', { name: 'Edit category: Office supplies', exact: true }),
    ).toHaveCount(0);
    expect((await page.request.get(`/api/products/${id}`)).status()).toBe(200);
    await page.screenshot({ path: test.info().outputPath('admin-categories-desktop.png'), fullPage: true });
    for (const width of [320, 375, 768, 1440]) {
        await page.setViewportSize({ width, height: 1000 });
        await expect
            .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
            .toBeLessThanOrEqual(width);
    }
    await page.setViewportSize({ width: 375, height: 1000 });
    await page.screenshot({ path: test.info().outputPath('admin-categories-mobile.png'), fullPage: true });
});

test('admin_stock_recovery', async ({ page }) => {
    await order(page);
    await login(page);
    const id = required('ORDERFLOW_BROWSER_PRODUCT');
    await page.goto(`/admin/products/${id}/stock`);
    await expect(page.getByTestId('stock-reserved')).toHaveText('2');
    await page.getByLabel('Stock operation', { exact: true }).selectOption('adjustment');
    await page.getByLabel('Adjustment delta', { exact: true }).fill('-99');
    await page.getByLabel('Stock reason', { exact: true }).fill('Count discrepancy');
    const rejected = page.waitForResponse((response) => response.url().endsWith('/stock-adjustments'));
    await page.getByRole('button', { name: 'Submit stock operation', exact: true }).click();
    const conflict = await rejected;
    expect(conflict.status()).toBe(409);
    await expect(page.getByRole('alert').filter({ hasText: 'The server did not confirm' })).toContainText(
        (await conflict.json()).traceId,
    );
    await expect(page.getByTestId('stock-on-hand')).toHaveText('100');
    await page.getByLabel('Stock operation', { exact: true }).selectOption('receipt');
    await page.getByLabel('Receipt quantity', { exact: true }).fill('5');
    await page.getByLabel('Stock reason', { exact: true }).fill('Recovered stock receipt');
    let body = '';
    await page.route(`**/api/admin/products/${id}/stock-receipts`, async (route) => {
        body = route.request().postData()!;
        const result = await route.fetch();
        expect(result.status()).toBe(201);
        await route.abort('connectionreset');
    });
    await page.getByRole('button', { name: 'Submit stock operation', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Resolve the previous Admin operation' })).toBeVisible();
    await expect(page.getByTestId('stock-on-hand')).toHaveText('105');
    expect((await operation(page)).command.body.operationId).toBe(JSON.parse(body).operationId);
    await page.unroute(`**/api/admin/products/${id}/stock-receipts`);
    await page.getByRole('button', { name: 'Sign out', exact: true }).click();
    await login(page, false);
    await page.goto('/admin/products');
    await expect(page.getByRole('heading', { name: 'Admin access required' })).toBeVisible();
    await expect(page.getByRole('region', { name: 'Admin operation recovery' })).toHaveCount(0);
    await page.getByRole('button', { name: 'Sign out', exact: true }).click();
    await login(page);
    await page.goto(`/admin/products/${id}/stock`);
    const replay = page.waitForRequest(
        (request) => request.url().endsWith('/stock-receipts') && request.method() === 'POST',
    );
    await page.getByRole('button', { name: 'Recover same stock/email operation', exact: true }).click();
    expect((await replay).postData()).toBe(body);
    await expect(page.getByRole('region', { name: 'Admin operation recovery' })).toHaveCount(0);
    await expect(page.getByTestId('stock-on-hand')).toHaveText('105');
    await expect(page.getByRole('cell', { name: 'Recovered stock receipt', exact: true })).toHaveCount(1);
});

test('admin_order_lifecycle_reports', async ({ page }) => {
    const created = await order(page, true);
    await login(page);
    await page.goto(`/admin/orders/${created.id}`);
    await expect(page.getByTestId('admin-order-state')).toHaveText('PendingPayment');
    await expect(page.getByRole('button', { name: /^Advance to/ })).toHaveCount(0);
    const token = await csrf(page.request);
    const invalid = await page.request.post(`/api/admin/orders/${created.id}/transitions`, {
        data: { targetStatus: 'Completed', expectedVersion: 1 },
        headers: { [token.headerName]: token.requestToken },
    });
    expect(invalid.status()).toBe(409);
    await control(page, 'submit');
    await control(page, 'capture');
    await page.getByRole('button', { name: 'Reload Admin order', exact: true }).click();
    await expect(page.getByTestId('admin-order-state')).toHaveText('Confirmed');
    for (const [state, reason] of [
        ['Processing', 'Packed'],
        ['Shipped', 'Handed to carrier'],
        ['Completed', 'Delivery confirmed'],
    ]) {
        await page.getByLabel('Fulfillment reason (optional)', { exact: true }).fill(reason!);
        await page.getByRole('button', { name: `Advance to ${state}`, exact: true }).click();
        await expect(page.getByTestId('admin-order-state')).toHaveText(state!);
    }
    await expect(page.getByRole('button', { name: /^Advance to/ })).toHaveCount(0);
    await page.getByRole('link', { name: 'Open financial and history audit', exact: true }).click();
    await range(page);
    await page.getByRole('button', { name: 'Apply filters', exact: true }).click();
    await expect(page.getByRole('cell', { name: 'Delivery confirmed', exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: /edit history/i })).toHaveCount(0);
    const immutable = await page.request.put(`/api/admin/audit/orders/${created.id}/history`, {
        data: { status: 'Confirmed' },
        headers: { [token.headerName]: token.requestToken },
    });
    expect([404, 405]).toContain(immutable.status());
    const audit = await (
        await page.request.get(`/api/admin/audit/orders/${created.id}/history?from=2035-01-01&to=2035-01-02`)
    ).json();
    expect(audit.totalCount).toBe(5);
    await nav(page, 'Reports');
    await range(page);
    await page.getByLabel('Low-stock threshold', { exact: true }).fill('100');
    await page.getByRole('button', { name: 'Apply filters', exact: true }).click();
    await expect(page.locator('.admin-metrics dd')).toHaveText(['$25.00', '$25.00', '$0.00', '$25.00']);
    await expect(page.getByRole('row').filter({ hasText: 'Completed' })).toContainText('1');
    await page.screenshot({ path: test.info().outputPath('admin-reports-desktop.png'), fullPage: true });
    for (const width of [320, 375, 768, 1440]) {
        await page.setViewportSize({ width, height: 1000 });
        await expect
            .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
            .toBeLessThanOrEqual(width);
    }
});

test('admin_permissions_conflicts', async ({ page }) => {
    await login(page);
    const id = required('ORDERFLOW_BROWSER_PRODUCT');
    await page.goto(`/admin/products/${id}`);
    await expect(page.getByLabel('Product name', { exact: true })).toHaveValue('Checkout fixture product');
    const other = await apiContext(page, true);
    try {
        const current = await (await other.request.get(`/api/admin/products/${id}`)).json();
        const token = await csrf(other.request);
        expect(
            (
                await other.request.put(`/api/admin/products/${id}`, {
                    data: {
                        name: 'Concurrent owner edit',
                        description: current.description,
                        price: current.price,
                        categoryId: current.category.id,
                        isActive: true,
                        expectedVersion: current.version,
                    },
                    headers: { [token.headerName]: token.requestToken },
                })
            ).status(),
        ).toBe(200);
        await page.getByLabel('Product name', { exact: true }).fill('Stale draft');
        await page.getByRole('button', { name: 'Save product', exact: true }).click();
        await expect(page.getByRole('alert').filter({ hasText: 'The server did not confirm' })).toContainText(
            'version_conflict',
        );
        await expect(page.getByLabel('Product name', { exact: true })).toHaveValue('Concurrent owner edit');
    } finally {
        await other.close();
    }
    await page.getByRole('button', { name: 'Sign out', exact: true }).click();
    await login(page, false);
    await page.evaluate(() =>
        localStorage.setItem(
            'orderflow-ui-v1',
            JSON.stringify({ version: 1, state: { density: 'compact', roles: ['Admin'] } }),
        ),
    );
    await page.goto('/admin/products');
    await expect(page.getByRole('heading', { name: 'Admin access required' })).toBeVisible();
    const token = await csrf(page.request);
    for (const path of [
        '/api/admin/products',
        '/api/admin/categories',
        `/api/admin/products/${id}/inventory`,
        '/api/admin/orders',
        '/api/admin/reports/sales?from=2035-01-01&to=2035-01-02',
        `/api/admin/audit/orders/${crypto.randomUUID()}/refunds?from=2035-01-01&to=2035-01-02`,
        '/api/admin/email-deliveries',
    ])
        expect((await page.request.get(path)).status()).toBe(403);
    for (const path of [
        `/api/admin/products/${id}/stock-receipts`,
        `/api/admin/orders/${crypto.randomUUID()}/transitions`,
        `/api/admin/email-deliveries/${crypto.randomUUID()}/retry`,
    ])
        expect(
            (
                await page.request.post(path, {
                    data: {},
                    headers: { [token.headerName]: token.requestToken },
                })
            ).status(),
        ).toBe(403);
});

test('admin_email_retry', async ({ page }) => {
    await order(page, true);
    await control(page, 'submit');
    await control(page, 'capture');
    await control(page, 'email-review');
    await login(page);
    await page.goto('/admin/emails');
    await page.getByRole('button', { name: 'Review email retry', exact: true }).click();
    await page.getByLabel('Email retry reason', { exact: true }).fill('Sink restored');
    await page
        .getByLabel('I acknowledge duplicate-mail risk for an uncertain outcome.', { exact: true })
        .check();
    let original = '';
    let row!: { id: string; providerKey: string; attemptCount: number; reviewRetryCount: number };
    await page.route('**/api/admin/email-deliveries/*/retry', async (route) => {
        original = route.request().postData()!;
        const response = await route.fetch();
        expect(response.status()).toBe(200);
        row = await response.json();
        await route.abort('connectionreset');
    });
    await page.getByRole('button', { name: 'Schedule audited email retry', exact: true }).click();
    await expect(page.getByRole('alert').filter({ hasText: 'The server did not confirm' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Resolve the previous Admin operation' })).toBeVisible();
    await page.unroute('**/api/admin/email-deliveries/*/retry');
    await page.reload();
    await expect(page.getByRole('heading', { name: 'No records here.' })).toBeVisible();
    const result = page.waitForResponse(
        (response) => response.url().endsWith('/retry') && response.request().method() === 'POST',
    );
    await page.getByRole('button', { name: 'Recover same stock/email operation', exact: true }).click();
    const replay = await result;
    expect(replay.request().postData()).toBe(original);
    const actual = await replay.json();
    expect(actual.id).toBe(row.id);
    expect(actual.providerKey).toBe(row.providerKey);
    expect(actual.attemptCount).toBe(5);
    expect(actual.reviewRetryCount).toBe(1);
    await expect(page.getByRole('region', { name: 'Admin operation recovery' })).toHaveCount(0);
    const token = await csrf(page.request);
    const refused = await page.request.post(`/api/admin/email-deliveries/${row.id}/retry`, {
        data: { ...JSON.parse(original), operationId: crypto.randomUUID() },
        headers: { [token.headerName]: token.requestToken },
    });
    expect(refused.status()).toBe(409);
});

test('admin_empty_error_views', async ({ page }) => {
    await login(page);
    await page.goto('/admin/emails');
    await expect(page.getByRole('heading', { name: 'No records here.' })).toBeVisible();
    await page.route('**/api/admin/email-deliveries?*', (route) => route.abort('connectionfailed'));
    await page.getByRole('button', { name: 'Refresh email failures', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText('Admin data could not be loaded');
    await page.unroute('**/api/admin/email-deliveries?*');
    await page.getByRole('button', { name: 'Try again', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'No records here.' })).toBeVisible();
    await nav(page, 'Reports');
    await expect(page.getByRole('heading', { name: 'No records here.' }).first()).toBeVisible();
    await nav(page, 'Audit');
    await expect(page.getByText('Choose a valid UTC date range', { exact: false })).toBeVisible();
    await page.getByLabel('Audit view', { exact: true }).selectOption('stock');
    await page.getByRole('button', { name: 'Apply filters', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'No records here.' })).toBeVisible();
    await nav(page, 'Categories');
    await expect(page.getByRole('button', { name: 'Next page', exact: true })).toBeEnabled();
    await page.getByRole('button', { name: 'Next page', exact: true }).click();
    await expect(page).toHaveURL(/page=2/);
    await expect(page.locator('.admin-table tbody tr')).toHaveCount(2);
    await expect(page.getByRole('button', { name: 'Next page', exact: true })).toBeDisabled();
});
