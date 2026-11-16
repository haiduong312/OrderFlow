import { expect, test } from '@playwright/test';
import type { Page } from '@playwright/test';
import type { CreatedOrder } from '../src/api/types';
function required(name: string) {
    const value = process.env[name];
    if (!value) throw new Error(`Run the real CheckoutBrowserTests harness (${name} missing).`);
    return value;
}
async function login(page: Page, account = 'A') {
    await page.goto('/login');
    await page
        .getByLabel('Email address', { exact: true })
        .fill(required(`ORDERFLOW_BROWSER_CUSTOMER_${account}`));
    await page.getByLabel('Password', { exact: true }).fill(required('ORDERFLOW_BROWSER_PASSWORD'));
    await page.locator('form button[type=submit]').click();
    await expect(page).toHaveURL(/\/account$/);
}
async function cart(page: Page) {
    await page.goto(`/products/${required('ORDERFLOW_BROWSER_PRODUCT')}`);
    await expect(page.getByRole('button', { name: 'Add to cart', exact: true })).toBeEnabled();
    await page.getByLabel('Quantity for Checkout fixture product', { exact: true }).fill('2');
    await page.getByRole('button', { name: 'Add to cart', exact: true }).click();
    await page.getByRole('link', { name: 'View your cart', exact: true }).click();
    await expect(page.getByTestId('cart-total')).toHaveText('$25.00');
    await page.getByRole('link', { name: 'Continue to checkout', exact: true }).click();
}
async function create(page: Page) {
    await cart(page);
    await page.getByRole('button', { name: 'Create order & reserve stock', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Order created', exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'View order & finish checkout', exact: true }).click();
    await expect(page.getByTestId('order-status')).toHaveText('PendingPayment');
    return new URL(page.url()).pathname.split('/').at(-1)!;
}
async function control(page: Page, action: string) {
    const result = await page.request.post(`/api/_checkout-ui/${action}`, {
        headers: { 'X-Checkout-Test-Control': required('ORDERFLOW_CHECKOUT_CONTROL') },
    });
    expect(result.status()).toBe(204);
}
async function startPayment(page: Page) {
    await page.getByRole('button', { name: 'Start payment', exact: true }).click();
    await expect(page.locator('.payment-activity article').filter({ hasText: 'Queued' })).toBeVisible();
}
async function pending(page: Page) {
    const me = await (await page.request.get('/api/auth/me')).json();
    return page.evaluate((id) => JSON.parse(localStorage.getItem(`orderflow-operations:v1:${id}`)!), me.id);
}

test('flow_happy_payment', async ({ page }) => {
    await login(page);
    const id = await create(page);
    await expect(page.getByText('Reservation countdown:', { exact: false })).toBeVisible();
    await startPayment(page);
    await control(page, 'submit');
    await control(page, 'capture');
    await control(page, 'notify');
    await expect(page.getByTestId('order-status')).toHaveText('Confirmed');
    await expect(page.getByText('Applied to this order', { exact: false })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Cancel eligible order', exact: true })).toHaveCount(0);
    await page.goto(`/orders/${id}?success=true&paid=true&return=success`);
    await expect(page.getByTestId('order-status')).toHaveText('Confirmed');
    await page.getByRole('link', { name: 'My orders', exact: true }).click();
    await expect(page.getByText(id, { exact: true })).toBeVisible();
    expect((await (await page.request.get('/api/orders')).json()).totalCount).toBe(1);
    await page.goto(`/orders/${id}`);
    await page.screenshot({ path: test.info().outputPath('order-confirmed-desktop.png'), fullPage: true });
    for (const width of [320, 375, 768]) {
        await page.setViewportSize({ width, height: 1000 });
        await expect
            .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
            .toBeLessThanOrEqual(width);
    }
});

test('flow_lost_create', async ({ page }) => {
    await login(page);
    await cart(page);
    let originalKey = '';
    let originalBody = '';
    let created!: CreatedOrder;
    await page.route('**/api/orders', async (route) => {
        if (route.request().method() !== 'POST') {
            await route.continue();
            return;
        }
        originalKey = route.request().headers()['idempotency-key']!;
        originalBody = route.request().postData()!;
        const response = await route.fetch();
        expect(response.status()).toBe(201);
        created = await response.json();
        await route.abort('connectionreset'); // Actual server commit happened; only the response is lost.
    });
    await page.getByRole('button', { name: 'Create order & reserve stock', exact: true }).click();
    await expect(page.getByText('Checkout outcome unresolved', { exact: true })).toBeVisible();
    await expect(page.getByRole('alert')).toBeVisible();
    expect((await pending(page)).checkout).toEqual({ key: originalKey, body: originalBody });
    await page.unroute('**/api/orders');
    await page.goto('/cart');
    await page.getByLabel('Quantity for Checkout fixture product', { exact: true }).fill('3');
    await page
        .getByRole('button', { name: 'Update quantity for Checkout fixture product', exact: true })
        .click();
    await page.getByRole('link', { name: 'Continue to checkout', exact: true }).click();
    await page.reload();
    await expect(page.getByText('Your cart has changed.', { exact: false })).toBeVisible();
    const replay = page.waitForRequest(
        (request) => request.url().endsWith('/api/orders') && request.method() === 'POST',
    );
    await page.getByRole('button', { name: 'Recover original checkout', exact: true }).click();
    const request = await replay;
    expect(request.headers()['idempotency-key']).toBe(originalKey);
    expect(request.postData()).toBe(originalBody);
    await expect(page.getByRole('heading', { name: 'Order created', exact: true })).toBeVisible();
    const saved = (await pending(page)).checkout;
    expect(saved.orderId).toBe(created.id);
    const order = await (await page.request.get(`/api/orders/${created.id}`)).json();
    expect(order.expiresAt).toBe(created.expiresAt);
    expect(order.items[0].quantity).toBe(2);
    expect((await (await page.request.get('/api/orders')).json()).totalCount).toBe(1);
    await page.getByRole('button', { name: 'View order & finish checkout', exact: true }).click();
    await page.goto('/cart');
    await expect(page.getByLabel('Quantity for Checkout fixture product', { exact: true })).toHaveValue('3');
});

test('flow_checkout_account_isolation', async ({ page }) => {
    await login(page);
    await cart(page);
    let ownerOrder = '';
    await page.route('**/api/orders', async (route) => {
        if (route.request().method() !== 'POST') {
            await route.continue();
            return;
        }
        const result = await route.fetch();
        ownerOrder = (await result.json()).id;
        await route.abort('connectionreset');
    });
    await page.getByRole('button', { name: 'Create order & reserve stock', exact: true }).click();
    await expect(page.getByRole('alert')).toBeVisible();
    const first = (await pending(page)).checkout;
    await page.unroute('**/api/orders');
    await page.getByRole('button', { name: 'Sign out', exact: true }).click();
    await expect(page).toHaveURL(/\/login$/);
    await login(page, 'B');
    await page.goto('/checkout');
    await expect(page.getByText('Checkout outcome unresolved', { exact: true })).toHaveCount(0);
    await expect(
        page.getByRole('button', { name: 'Create order & reserve stock', exact: true }),
    ).toBeEnabled();
    expect((await page.request.get(`/api/orders/${ownerOrder}`)).status()).toBe(404);
    expect((await page.request.get(`/api/orders/${ownerOrder}/payment-status`)).status()).toBe(404);
    await page.getByRole('button', { name: 'Create order & reserve stock', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Order created', exact: true })).toBeVisible();
    expect((await pending(page)).checkout.key).not.toBe(first.key);
    await page.getByRole('button', { name: 'Sign out', exact: true }).click();
    await login(page);
    await page.goto('/checkout');
    expect((await pending(page)).checkout).toEqual(first);
    await page.getByRole('button', { name: 'Recover original checkout', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Order created', exact: true })).toBeVisible();
    expect((await pending(page)).checkout.orderId).toBe(ownerOrder);
});

test('flow_payment_timeout', async ({ page }) => {
    test.setTimeout(45_000);
    await login(page);
    const id = await create(page);
    let release!: () => void;
    const gate = new Promise<void>((yes) => {
        release = yes;
    });
    let originalKey = '';
    let attemptId = '';
    await page.route(`**/api/orders/${id}/payment-attempts`, async (route) => {
        originalKey = route.request().headers()['idempotency-key']!;
        const result = await route.fetch();
        expect(result.status()).toBe(202);
        attemptId = (await result.json()).id;
        await gate;
        // The browser timeout has already cancelled/handled this route; do not abort it twice.
    });
    await page.getByRole('button', { name: 'Start payment', exact: true }).click();
    await expect(page.getByRole('alert')).toBeVisible({ timeout: 22_000 });
    expect((await pending(page)).payments).toEqual([{ orderId: id, key: originalKey }]);
    release();
    await page.unroute(`**/api/orders/${id}/payment-attempts`);
    await page.reload();
    const replay = page.waitForRequest(
        (request) =>
            request.url().endsWith(`/api/orders/${id}/payment-attempts`) && request.method() === 'POST',
    );
    await page.getByRole('button', { name: 'Recover original payment request', exact: true }).click();
    const request = await replay;
    expect(request.headers()['idempotency-key']).toBe(originalKey);
    expect(request.postData()).toBe('{}');
    await expect.poll(async () => (await pending(page)).payments[0]?.attemptId).toBe(attemptId);
    const status = await (await page.request.get(`/api/orders/${id}/payment-status`)).json();
    expect(status.attemptCount).toBe(1);
    await expect(page.getByTestId('order-status')).toHaveText('PendingPayment');
});

test('flow_expired_order', async ({ page }) => {
    await login(page);
    const id = await create(page);
    await expect(page.getByRole('button', { name: 'Start payment', exact: true })).toBeEnabled();
    await control(page, 'advance-expiry');
    await page.getByRole('button', { name: 'Start payment', exact: true }).click();
    await expect(page.getByRole('alert')).toContainText('order_expired');
    await expect(page.getByTestId('order-status')).toHaveText('Expired');
    const status = await (await page.request.get(`/api/orders/${id}/payment-status`)).json();
    expect(status.attemptCount).toBe(0);
    await page.goto(`/orders/${id}?success=true&paid=true`);
    await expect(page.getByTestId('order-status')).toHaveText('Expired');
});

test('flow_late_refund', async ({ page }) => {
    await login(page);
    await create(page);
    await startPayment(page);
    await control(page, 'submit');
    await page.getByRole('button', { name: 'Cancel eligible order', exact: true }).click();
    await expect(page.getByTestId('order-status')).toHaveText('Cancelled');
    await control(page, 'capture');
    await control(page, 'notify');
    await expect(page.getByText('Not applied to this order', { exact: false })).toBeVisible();
    await expect(page.getByText('Refund pending', { exact: false })).toBeVisible();
    await expect(page.getByTestId('order-status')).toHaveText('Cancelled');
    await control(page, 'refund');
    await page.getByRole('button', { name: 'Refresh order', exact: true }).click();
    await expect(page.getByText('Refund confirmed', { exact: false })).toBeVisible();
    await expect(page.getByTestId('order-status')).toHaveText('Cancelled');
});

test('flow_cancel_notifications', async ({ page }) => {
    await login(page);
    const id = await create(page);
    await control(page, 'notify');
    await page.getByRole('link', { name: 'Notifications', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Order created', exact: true })).toBeVisible();
    await page.getByRole('button', { name: /^Mark notification .* as read$/ }).click();
    await expect(page.getByText('0 unread', { exact: true })).toBeVisible();
    await page.goto(`/orders/${id}`);
    await page.getByRole('button', { name: 'Cancel eligible order', exact: true }).click();
    await expect(page.getByTestId('order-status')).toHaveText('Cancelled');
    await control(page, 'notify');
    await page.getByRole('link', { name: 'Notifications', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Order cancelled', exact: true })).toBeVisible();
    await expect(page.getByText('1 unread', { exact: true })).toBeVisible();
    expect((await (await page.request.get('/api/notifications')).json()).totalCount).toBe(2); // Duplicate source/hint dispatch did not create extra rows/unread increments.
});

test('flow_realtime_reconnect', async ({ page }) => {
    await login(page);
    await create(page);
    await startPayment(page);
    await control(page, 'submit');
    await expect(page.getByTestId('live-state')).toHaveText('Live updates connected');
    let release!: () => void;
    let reached!: () => void;
    const gate = new Promise<void>((yes) => {
        release = yes;
    });
    const arrived = new Promise<void>((yes) => {
        reached = yes;
    });
    await page.route('**/api/hubs/notifications/negotiate?*', async (route) => {
        reached();
        await gate;
        await route.continue();
    });
    await control(page, 'disconnect');
    await arrived; // A real server-aborted socket actually reaches renegotiation before the mutation.
    await control(page, 'capture');
    await control(page, 'notify-durable');
    release();
    await expect(page.getByTestId('live-state')).toHaveText('Live updates connected');
    await expect(page.getByTestId('order-status')).toHaveText('Confirmed');
    await page.unroute('**/api/hubs/notifications/negotiate?*');
    await control(page, 'notify');
    await page.getByRole('link', { name: 'Notifications', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Payment confirmed', exact: true })).toBeVisible();
    expect((await (await page.request.get('/api/notifications')).json()).totalCount).toBe(2);
});

test('flow_refund_review', async ({ page }) => {
    await login(page);
    await create(page);
    await startPayment(page);
    await control(page, 'submit');
    await page.getByRole('button', { name: 'Cancel eligible order', exact: true }).click();
    await expect(page.getByTestId('order-status')).toHaveText('Cancelled');
    await control(page, 'capture');
    await control(page, 'refund-review');
    await page.getByRole('button', { name: 'Refresh order', exact: true }).click();
    await expect(page.getByText('Refund needs review', { exact: false })).toBeVisible();
    await expect(page.getByText('Do not assume funds have been returned.', { exact: false })).toBeVisible();
    await expect(page.getByTestId('order-status')).toHaveText('Cancelled');
});
