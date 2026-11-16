import { expect, test } from '@playwright/test';
import type { APIRequestContext, Page } from '@playwright/test';

function required(name: string) {
    const value = process.env[name];
    if (!value) throw new Error(`Missing ${name}; run the PostgreSQL-backed FrontendBrowserTests harness.`);
    return value;
}
const emailA = () => required('ORDERFLOW_BROWSER_CUSTOMER_A');
const emailB = () => required('ORDERFLOW_BROWSER_CUSTOMER_B');
const password = () => required('ORDERFLOW_BROWSER_PASSWORD');

async function submitLogin(page: Page, email = emailA(), secret = password()) {
    await page.getByLabel('Email address', { exact: true }).fill(email);
    await page.getByLabel('Password', { exact: true }).fill(secret);
    await page.locator('form button[type=submit]').click();
}
async function login(page: Page, email = emailA()) {
    await page.goto('/login');
    await submitLogin(page, email);
    await expect(page).toHaveURL(/\/(account|orders)(\?|$)/);
}
async function token(context: APIRequestContext) {
    const response = await context.get('/api/auth/antiforgery');
    expect(response.status()).toBe(200);
    return (await response.json()) as { requestToken: string; headerName: string };
}
async function seedOrder(page: Page, email: string, quantity: number) {
    const request = await page
        .context()
        .browser()!
        .newContext({ baseURL: test.info().project.use.baseURL as string });
    try {
        const csrf = await token(request.request);
        const signed = await request.request.post('/api/auth/login', {
            data: { email, password: password() },
            headers: { [csrf.headerName]: csrf.requestToken },
        });
        expect(signed.status()).toBe(200);
        const fresh = await token(request.request);
        const created = await request.request.post('/api/orders', {
            data: { items: [{ productId: required('ORDERFLOW_BROWSER_PRODUCT'), quantity }] },
            headers: { [fresh.headerName]: fresh.requestToken, 'Idempotency-Key': crypto.randomUUID() },
        });
        expect(created.status()).toBe(201);
        return ((await created.json()) as { id: string }).id;
    } finally {
        await request.close();
    }
}

test('session_restore', async ({ page, context }) => {
    await login(page);
    await expect(page.locator('.account-person h2')).toHaveText(emailA());
    await page.reload();
    await expect(page.locator('.account-person h2')).toHaveText(emailA());
    const cookie = (await context.cookies()).find((value) => value.name === 'OrderFlow.Dev.Auth');
    expect(cookie?.httpOnly).toBe(true);
    expect(await page.evaluate(() => document.cookie)).not.toContain('OrderFlow.Dev.Auth');
    const storage = await page.evaluate(() => ({
        local: { ...localStorage },
        session: { ...sessionStorage },
    }));
    expect(Object.keys(storage.local).every((key) => key === 'orderflow-ui-v1')).toBe(true);
    expect(Object.keys(storage.session)).toHaveLength(0);
    expect(JSON.stringify(storage)).not.toContain(password());
    await page.screenshot({ path: test.info().outputPath('account-desktop.png'), fullPage: true });
});

test('registration_validation', async ({ page }) => {
    const email = `new-${crypto.randomUUID()}@example.invalid`;
    await page.goto('/register');
    await page.getByLabel('Email address', { exact: true }).fill(email);
    await page.getByLabel('Password', { exact: true }).fill('abcdefghijklmnop');
    const invalid = page.waitForResponse((response) => response.url().endsWith('/api/auth/register'));
    await page.getByRole('button', { name: 'Create account', exact: true }).click();
    const rejected = await invalid;
    expect(rejected.status()).toBe(400);
    const problem = await rejected.json();
    await expect(page.locator('#password-error')).toContainText('uppercase');
    await expect(page.getByLabel('Password', { exact: true })).toHaveAttribute('aria-invalid', 'true');
    await expect(page.getByRole('alert')).toContainText(problem.traceId);
    await page.getByLabel('Password', { exact: true }).fill(password());
    await page.getByRole('button', { name: 'Create account', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Your account is ready.' })).toBeVisible();
    expect((await page.request.get('/api/auth/me')).status()).toBe(401); // 201 registration is not login.
    await page.getByRole('link', { name: 'Continue to sign in' }).click();
    await submitLogin(page, email);
    await expect(page.locator('.account-person h2')).toHaveText(email);
});

test('credentials_csrf', async ({ page }) => {
    await page.goto('/login');
    const invalid = page.waitForResponse((response) => response.url().endsWith('/api/auth/login'));
    await submitLogin(page, emailA(), 'WrongPassword!123');
    const rejected = await invalid;
    expect(rejected.status()).toBe(401);
    const problem = await rejected.json();
    await expect(page.getByRole('alert')).toContainText('invalid_credentials');
    await expect(page.getByRole('alert')).toContainText(problem.traceId);
    let rejectedPosts = 0;
    await page.route('**/api/auth/login', async (route) => {
        rejectedPosts++;
        await route.continue({
            headers: { ...route.request().headers(), 'x-csrf-token': 'invalid-test-token' },
        });
    });
    const badCsrf = page.waitForResponse((response) => response.url().endsWith('/api/auth/login'));
    await submitLogin(page);
    const csrf = await badCsrf;
    expect(csrf.status()).toBe(400);
    expect((await csrf.json()).code).toBe('antiforgery_failed');
    await expect(page.getByRole('alert')).toContainText('Your security check has expired');
    expect(rejectedPosts).toBe(1); // The rejected mutation is not automatically replayed.
    await page.unroute('**/api/auth/login');
    const fresh = page.waitForResponse((response) => response.url().endsWith('/api/auth/antiforgery'));
    await submitLogin(page);
    expect((await fresh).status()).toBe(200);
    await expect(page.locator('.account-person h2')).toHaveText(emailA());
    await page.getByRole('button', { name: 'Sign out', exact: true }).click();
    await expect(page).toHaveURL(/\/login$/); // Fresh authenticated and then anonymous CSRF state works on logout too.
});

test('missing_expired_auth', async ({ page, context }) => {
    await page.goto('/orders');
    await expect(page).toHaveURL(/\/login$/);
    await submitLogin(page);
    await expect(page).toHaveURL(/\/orders/);
    await expect(page.getByText('No orders here yet.')).toBeVisible();
    await context.clearCookies({ name: 'OrderFlow.Dev.Auth' });
    await page.getByLabel('Status', { exact: true }).selectOption('Confirmed');
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByText('Your session has ended. Please sign in again.')).toBeVisible();
    await submitLogin(page);
    await expect(page).toHaveURL(/\/orders/);
    const advanced = await page.request.post('/api/_browser/advance-clock', {
        headers: { 'X-Browser-Test-Control': required('ORDERFLOW_BROWSER_CONTROL') },
    });
    expect(advanced.status()).toBe(204); // Advances injected API TimeProvider by 9h; no real sleep.
    await page.getByLabel('Status', { exact: true }).selectOption('Completed');
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByText('Your session has ended. Please sign in again.')).toBeVisible();
    expect((await page.request.get('/api/auth/me')).status()).toBe(401);
});

test('account_switch_isolation', async ({ page }) => {
    const a = await seedOrder(page, emailA(), 1);
    const b = await seedOrder(page, emailB(), 2);
    await login(page);
    await page.getByRole('link', { name: 'My orders', exact: true }).click();
    await expect(page.getByText(a, { exact: true })).toBeVisible();
    await expect(page.getByText(b, { exact: true })).toHaveCount(0);
    // Stay in one SPA/QueryClient: a hard reload here would hide a broken cache boundary.
    await page.getByRole('link', { name: 'My account', exact: true }).click();
    await page.getByRole('link', { name: 'Switch account', exact: true }).click();
    await submitLogin(page, emailB());
    await expect(page.locator('.account-person h2')).toHaveText(emailB());
    await page.getByRole('link', { name: 'My orders', exact: true }).click();
    await expect(page.getByText(b, { exact: true })).toBeVisible();
    await expect(page.getByText(a, { exact: true })).toHaveCount(0);
    await page.getByRole('button', { name: 'Sign out', exact: true }).click();
    await expect(page).toHaveURL(/\/login$/);
    await page.goBack();
    await expect(page.getByText(b, { exact: true })).toHaveCount(0);
    await login(page);
    await page.goto('/orders');
    await expect(page.getByText(a, { exact: true })).toBeVisible();
    await expect(page.getByText(b, { exact: true })).toHaveCount(0);
    const privateAccess = await page.request.get('/api/admin/reports/sales?from=2026-11-01&to=2026-11-14');
    expect(privateAccess.status()).toBe(403);
    await page.evaluate(() =>
        localStorage.setItem(
            'orderflow-ui-v1',
            JSON.stringify({
                version: 1,
                state: { density: 'compact', roles: ['Admin'], user: { roles: ['Admin'] } },
            }),
        ),
    );
    await page.reload();
    await expect(page.getByText(a, { exact: true })).toBeVisible();
    await page.getByRole('button', { name: 'Compact layout' }).click();
    const stored = await page.evaluate(() => JSON.parse(localStorage.getItem('orderflow-ui-v1')!));
    expect(Object.keys(stored.state)).toEqual(['density']);
    expect((await page.request.get('/api/admin/reports/sales?from=2026-11-01&to=2026-11-14')).status()).toBe(
        403,
    );
});

test('cross_tab_isolation', async ({ page, context }) => {
    const a = await seedOrder(page, emailA(), 1);
    const b = await seedOrder(page, emailB(), 2);
    await login(page);
    await page.goto('/orders');
    await expect(page.getByText(a, { exact: true })).toBeVisible();
    const second = await context.newPage();
    await second.goto('/login');
    await submitLogin(second, emailB());
    await expect(second.locator('.account-person h2')).toHaveText(emailB());
    await page.bringToFront();
    await expect(page.getByText(b, { exact: true })).toBeVisible();
    await expect(page.getByText(a, { exact: true })).toHaveCount(0);
    await second.bringToFront();
    await expect(second.getByRole('button', { name: 'Sign out' })).toBeVisible();
    await second.getByRole('button', { name: 'Sign out' }).click();
    await expect(second).toHaveURL(/\/login$/);
    await page.bringToFront();
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByText(b, { exact: true })).toHaveCount(0);
});

test('loading_empty_error', async ({ page }) => {
    await login(page);
    let release!: () => void;
    const gate = new Promise<void>((yes) => {
        release = yes;
    });
    let reached!: () => void;
    const arrived = new Promise<void>((yes) => {
        reached = yes;
    });
    await page.route('**/api/orders?*', async (route) => {
        reached();
        await gate;
        await route.continue();
    });
    await page.goto('/orders');
    await arrived;
    await expect(page.getByText('Loading your orders…')).toBeVisible();
    release();
    await expect(page.getByText('No orders here yet.')).toBeVisible();
    await page.unroute('**/api/orders?*');
    await page.route('**/api/orders?*', (route) => route.abort('connectionfailed'));
    await page.getByLabel('Status', { exact: true }).selectOption('Completed');
    await expect(page.getByRole('alert')).toContainText('Orders could not be loaded');
    await page.unroute('**/api/orders?*');
    await page.getByRole('button', { name: 'Try again' }).click();
    await expect(page.getByText('No orders here yet.')).toBeVisible();
});

test('unavailable_restore', async ({ page }) => {
    await page.route('**/api/auth/me', (route) => route.abort('connectionfailed'));
    await page.goto('/account');
    await expect(page.getByRole('alert')).toContainText('Your session could not be confirmed');
    await expect(page).toHaveURL(/\/account$/); // Infrastructure failure is not invented guest/login success.
    await page.unroute('**/api/auth/me');
    await page.getByRole('button', { name: 'Try again' }).click();
    await expect(page).toHaveURL(/\/login$/);
});

test('responsive_navigation', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByRole('heading', { name: 'Everything in order.' })).toBeVisible();
    await page.screenshot({ path: test.info().outputPath('overview-desktop.png'), fullPage: true });
    for (const width of [320, 768, 1440]) {
        await page.setViewportSize({ width, height: 900 });
        await expect
            .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
            .toBeLessThanOrEqual(width);
    }
    await page.setViewportSize({ width: 375, height: 812 });
    await expect
        .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
        .toBeLessThanOrEqual(375);
    const open = page.getByRole('button', { name: 'Open navigation' });
    await open.click();
    const navigation = page.getByRole('dialog', { name: 'Workspace navigation' });
    await expect(navigation).toBeVisible();
    await expect(navigation.getByRole('button', { name: 'Close navigation' })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(open).toBeFocused();
    await open.click();
    await navigation.getByRole('link', { name: 'My account' }).click();
    await expect(page).toHaveURL(/\/login$/);
    await expect(page.getByLabel('Email address', { exact: true })).toBeVisible();
    await page.getByLabel('Email address', { exact: true }).focus();
    await page.keyboard.press('Tab');
    await expect(page.getByLabel('Password', { exact: true })).toBeFocused();
    await expect
        .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
        .toBeLessThanOrEqual(375);
    await page.screenshot({ path: test.info().outputPath('login-mobile.png'), fullPage: true });
});
