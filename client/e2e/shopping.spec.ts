import { expect, test } from '@playwright/test';
import type { APIRequestContext, BrowserContext, Page } from '@playwright/test';
import type { Product } from '../src/api/types';

function required(name: string) {
    const value = process.env[name];
    if (!value) throw new Error(`Missing ${name}; run the PostgreSQL-backed FrontendBrowserTests harness.`);
    return value;
}
async function findProduct(page: Page, name: string): Promise<Product> {
    const response = await page.request.get(
        `/api/products?q=${encodeURIComponent(name)}&page=1&pageSize=100&sort=name`,
    );
    expect(response.status()).toBe(200);
    const product = ((await response.json()) as { items: Product[] }).items.find(
        (value) => value.name === name,
    );
    expect(product, `Fixture ${name} must exist in the real public API`).toBeDefined();
    return product!;
}
async function add(page: Page, product: Product, quantity = '1') {
    await page.goto(`/products/${product.id}`);
    await expect(page.getByRole('heading', { name: product.name, exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Add to cart', exact: true })).toBeEnabled();
    await page.getByLabel(`Quantity for ${product.name}`, { exact: true }).fill(quantity);
    await page.getByRole('button', { name: 'Add to cart', exact: true }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Nothing reserved.' })).toBeVisible();
}
async function csrf(request: APIRequestContext) {
    const response = await request.get('/api/auth/antiforgery');
    expect(response.status()).toBe(200);
    return (await response.json()) as { headerName: string; requestToken: string };
}
async function admin(page: Page): Promise<BrowserContext> {
    const context = await page
        .context()
        .browser()!
        .newContext({ baseURL: test.info().project.use.baseURL as string });
    const token = await csrf(context.request);
    const signed = await context.request.post('/api/auth/login', {
        data: {
            email: required('ORDERFLOW_BROWSER_ADMIN'),
            password: required('ORDERFLOW_BROWSER_PASSWORD'),
        },
        headers: { [token.headerName]: token.requestToken },
    });
    expect(signed.status()).toBe(200);
    return context;
}
async function signIn(page: Page, email: string) {
    await page.getByLabel('Email address', { exact: true }).fill(email);
    await page.getByLabel('Password', { exact: true }).fill(required('ORDERFLOW_BROWSER_PASSWORD'));
    await page.locator('form button[type=submit]').click();
}

test('catalog_filter_pages', async ({ page }) => {
    await page.goto('/products?q=Alpha&sort=name&page=1&pageSize=12');
    await expect(page.locator('.product-card')).toHaveCount(12);
    const first = await page.locator('.product-card h2').allTextContents();
    expect(first).toEqual(
        Array.from({ length: 12 }, (_, index) => `Alpha ${String(index + 1).padStart(2, '0')}`),
    );
    let release!: () => void;
    let reached!: () => void;
    const gate = new Promise<void>((yes) => {
        release = yes;
    });
    const arrived = new Promise<void>((yes) => {
        reached = yes;
    });
    await page.route('**/api/products?*', async (route) => {
        if (new URL(route.request().url()).searchParams.get('page') === '2') {
            reached();
            await gate;
        }
        await route.continue();
    });
    await page.getByRole('button', { name: 'Next products', exact: true }).click();
    await arrived;
    await expect(page.getByText('Loading products…')).toBeVisible();
    await expect(page.locator('.product-card')).toHaveCount(0); // Old page is not mislabeled as page two.
    release();
    await expect(page.locator('.product-card')).toHaveCount(2);
    expect(await page.locator('.product-card h2').allTextContents()).toEqual(['Alpha 13', 'Alpha 14']);
    await expect(page).toHaveURL(/page=2/);
    await page.unroute('**/api/products?*');
    await page.getByLabel('Sort by', { exact: true }).selectOption('price-asc');
    await expect(page).toHaveURL(/page=1/);
    await expect(page.locator('.product-card')).toHaveCount(12);
    const ordered = await page.locator('.product-card h2').allTextContents();
    await page.getByRole('button', { name: 'Refresh catalog', exact: true }).click();
    await expect(page.getByText('Refreshing catalog prices and availability…')).toHaveCount(0);
    expect(await page.locator('.product-card h2').allTextContents()).toEqual(ordered);
    await page.getByLabel('Per page', { exact: true }).selectOption('24');
    await expect(page.locator('.product-card')).toHaveCount(14);
    const request = page.waitForRequest((value) => {
        const url = new URL(value.url());
        return url.pathname === '/api/products' && url.searchParams.get('q') === 'Cents';
    });
    await page.getByLabel('Search products', { exact: true }).fill('Cents');
    await request; // Real debounce completes; no arbitrary timing sleep.
    await expect(page.locator('.product-card')).toHaveCount(2);
    await page.getByRole('button', { name: 'Desk essentials', exact: true }).click();
    await expect(page.locator('.product-card')).toHaveCount(1);
    await expect(page.locator('.product-card h2')).toHaveText('Cents A');
    await expect(page).toHaveURL(/categoryId=/);
    await page.getByLabel('Search products', { exact: true }).fill('No matching fixture');
    await expect(page.getByRole('heading', { name: 'No products here.' })).toBeVisible();
    await page.getByRole('button', { name: 'Reset filters' }).click();
    await expect(page.locator('.product-card')).toHaveCount(18); // Includes original fixture; excludes both hidden products.
    await expect(page.getByText('Hidden product', { exact: true })).toHaveCount(0);
    await expect(page.getByText('Category hidden', { exact: true })).toHaveCount(0);
    const tieFirst = await page.request.get('/api/products?q=Alpha&sort=price-asc&page=1&pageSize=12');
    const tieSecond = await page.request.get('/api/products?q=Alpha&sort=price-asc&page=2&pageSize=12');
    const ids = [...(await tieFirst.json()).items, ...(await tieSecond.json()).items].map(
        (value: Product) => value.id,
    );
    expect(new Set(ids).size).toBe(14);
});

test('catalog_categories_detail', async ({ page }) => {
    await page.goto('/products');
    await expect(page.getByRole('button', { name: 'Next categories' })).toBeEnabled();
    await page.getByRole('button', { name: 'Next categories' }).click();
    await page.getByRole('button', { name: 'Travel essentials', exact: true }).click();
    await expect(page.locator('.product-card')).toHaveCount(2);
    await page.getByRole('link', { name: 'Cents B', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Cents B', exact: true })).toBeVisible();
    await expect(page.locator('.detail-price')).toContainText('$0.20');
    await expect(page.getByLabel('Quantity for Cents B', { exact: true })).toBeVisible();
    await page.goto('/products/not-a-product-id');
    await expect(page.getByRole('heading', { name: 'This product is unavailable.' })).toBeVisible();
    await page.goto('/products/00000000-0000-4000-8000-000000000001');
    await expect(page.getByRole('heading', { name: 'This product is unavailable.' })).toBeVisible();
    await page.goto('/products');
    const empty = await findProduct(page, 'Out of stock');
    await page.goto(`/products/${empty.id}`);
    await expect(page.getByRole('button', { name: 'Add to cart', exact: true })).toBeDisabled();
    await expect(page.getByText('Currently out of stock', { exact: true })).toBeVisible();
    await page.goto('/products?sort=name&pageSize=12&page=1');
    await expect(page.locator('.product-card')).toHaveCount(12);
    await page.screenshot({ path: test.info().outputPath('catalog-desktop.png'), fullPage: true });
    for (const width of [320, 375, 768]) {
        await page.setViewportSize({ width, height: 1000 });
        await expect
            .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
            .toBeLessThanOrEqual(width);
    }
    await page.setViewportSize({ width: 375, height: 1000 });
    await page.screenshot({ path: test.info().outputPath('catalog-mobile.png'), fullPage: true });
});

test('cart_quantities_restore', async ({ page }) => {
    await page.goto('/products');
    const a = await findProduct(page, 'Cents A');
    const b = await findProduct(page, 'Cents B');
    await add(page, a, '3');
    await add(page, b);
    await page.getByRole('link', { name: 'View your cart', exact: true }).click();
    await expect(page.getByTestId('cart-total')).toHaveText('$0.50');
    const quantity = page.getByLabel('Quantity for Cents A', { exact: true });
    for (const invalid of ['0', '1.5', '1e2', '1001', '']) {
        await quantity.fill(invalid);
        await page.getByRole('button', { name: 'Update quantity for Cents A', exact: true }).click();
        await expect(quantity).toHaveAttribute('aria-invalid', 'true');
        await expect(page.getByRole('alert').filter({ hasText: 'Enter a whole quantity' })).toBeVisible();
        await expect(page.getByTestId('cart-total')).toHaveText('$0.50');
    }
    await quantity.fill('2');
    await page.getByRole('button', { name: 'Update quantity for Cents A', exact: true }).click();
    await expect(page.getByTestId('cart-total')).toHaveText('$0.40');
    await page.reload();
    await expect(page.getByLabel('Quantity for Cents A', { exact: true })).toHaveValue('2');
    await expect(page.getByTestId('cart-total')).toHaveText('$0.40');
    expect(await page.evaluate(() => JSON.parse(localStorage.getItem('orderflow-cart')!))).toEqual({
        version: 1,
        state: {
            items: [
                { productId: a.id, quantity: 2 },
                { productId: b.id, quantity: 1 },
            ],
        },
    });
    const current = await page.request.get(`/api/products/${a.id}`);
    expect((await current.json()).available).toBe(100); // Cart additions never reserve or optimistically decrement stock.
    for (const width of [320, 375, 768, 1440]) {
        await page.setViewportSize({ width, height: 1000 });
        await expect
            .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
            .toBeLessThanOrEqual(width);
        await expect(page.getByRole('complementary', { name: 'Cart summary' })).toBeVisible();
    }
    await page.screenshot({ path: test.info().outputPath('cart-desktop.png'), fullPage: true });
    await page.setViewportSize({ width: 375, height: 1000 });
    await page.screenshot({ path: test.info().outputPath('cart-mobile.png'), fullPage: true });
    await page.getByRole('button', { name: 'Remove Cents B from cart', exact: true }).click();
    await expect(page.getByTestId('cart-total')).toHaveText('$0.20');
    await page.getByRole('button', { name: 'Clear cart', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Your cart is empty.' })).toBeVisible();
});

test('cart_price_change', async ({ page }) => {
    await page.goto('/products');
    const a = await findProduct(page, 'Cents A');
    await add(page, a, '3');
    await page.getByRole('link', { name: 'View your cart', exact: true }).click();
    await expect(page.getByTestId('cart-total')).toHaveText('$0.30');
    const owner = await admin(page);
    try {
        const token = await csrf(owner.request);
        const updated = await owner.request.put(`/api/admin/products/${a.id}`, {
            data: {
                name: a.name,
                description: a.description,
                price: '0.15',
                categoryId: a.category.id,
                isActive: true,
                expectedVersion: a.version,
            },
            headers: { [token.headerName]: token.requestToken },
        });
        expect(updated.status()).toBe(200);
        await page.getByRole('button', { name: 'Check prices & availability', exact: true }).click();
        await expect(page.getByTestId('cart-total')).toHaveText('$0.45');
        await expect(
            page.getByRole('status').filter({ hasText: 'price changed from $0.10 to $0.15' }),
        ).toBeVisible();
        await expect(
            page.getByRole('status').filter({ hasText: 'Nothing has been reserved or ordered.' }),
        ).toBeVisible();
        expect(
            JSON.stringify(await page.evaluate(() => JSON.parse(localStorage.getItem('orderflow-cart')!))),
        ).not.toContain('price');
        await page.reload();
        await expect(page.getByTestId('cart-total')).toHaveText('$0.45');
    } finally {
        await owner.close();
    }
});

test('cart_archived_unavailable', async ({ page }) => {
    await page.goto('/products');
    const a = await findProduct(page, 'Cents A');
    const b = await findProduct(page, 'Cents B');
    await add(page, a);
    await add(page, b, '101');
    await page.getByRole('link', { name: 'View your cart', exact: true }).click();
    await expect(page.getByRole('alert').filter({ hasText: 'Only 100 currently available.' })).toBeVisible();
    const owner = await admin(page);
    try {
        const token = await csrf(owner.request);
        expect(
            (
                await owner.request.post(`/api/admin/products/${a.id}/archive`, {
                    data: { expectedVersion: a.version },
                    headers: { [token.headerName]: token.requestToken },
                })
            ).status(),
        ).toBe(200);
        await page.getByRole('button', { name: 'Check prices & availability', exact: true }).click();
        await expect(
            page.getByRole('alert').filter({ hasText: 'This product is no longer available.' }),
        ).toBeVisible();
        await expect(page.getByTestId('cart-total')).toHaveText('Unavailable');
        await expect(
            page.getByText('Cart estimates checked. Nothing has been reserved or ordered.'),
        ).toHaveCount(0);
        await page.getByRole('button', { name: `Remove Product ${a.id} from cart`, exact: true }).click();
        await page.route(`**/api/products/${b.id}`, (route) => route.abort('connectionfailed'));
        await page.getByRole('button', { name: 'Check prices & availability', exact: true }).click();
        await expect(page.getByRole('alert')).toContainText('This item could not be checked');
        await expect(page.getByTestId('cart-total')).toHaveText('Unavailable');
        await page.unroute(`**/api/products/${b.id}`);
        await page.getByRole('button', { name: 'Try again', exact: true }).click();
        await expect(page.getByLabel('Quantity for Cents B', { exact: true })).toBeVisible();
        await page.getByLabel('Quantity for Cents B', { exact: true }).fill('1');
        await page.getByRole('button', { name: 'Update quantity for Cents B', exact: true }).click();
        await page.getByRole('button', { name: 'Check prices & availability', exact: true }).click();
        await expect(
            page.getByText('Cart estimates checked. Nothing has been reserved or ordered.'),
        ).toBeVisible();
        await page.goto(`/products/${a.id}`);
        await expect(page.getByRole('heading', { name: 'This product is unavailable.' })).toBeVisible();
    } finally {
        await owner.close();
    }
});

test('cart_account_scope', async ({ page }) => {
    await page.goto('/products');
    const a = await findProduct(page, 'Cents A');
    await add(page, a);
    await page.getByRole('link', { name: 'View your cart', exact: true }).click();
    await expect(page.getByText('Signing out keeps this browser cart.')).toBeVisible();
    await page.getByRole('link', { name: 'Sign in to your account', exact: true }).click();
    await signIn(page, required('ORDERFLOW_BROWSER_CUSTOMER_A'));
    await expect(page).toHaveURL(/\/cart$/);
    await expect(page.getByLabel('Quantity for Cents A', { exact: true })).toHaveValue('1');
    await page.getByRole('button', { name: 'Sign out', exact: true }).click();
    await expect(page).toHaveURL(/\/login$/);
    await page.getByRole('link', { name: /^Cart / }).click();
    await expect(page.getByLabel('Quantity for Cents A', { exact: true })).toHaveValue('1');
    await page.getByRole('link', { name: 'Sign in to your account', exact: true }).click();
    await signIn(page, required('ORDERFLOW_BROWSER_CUSTOMER_B'));
    await expect(page).toHaveURL(/\/cart$/);
    await expect(page.getByLabel('Quantity for Cents A', { exact: true })).toHaveValue('1');
    await expect(page.locator('.user-email')).toHaveText(required('ORDERFLOW_BROWSER_CUSTOMER_B'));
});

test('cart_cross_tab', async ({ page, context }) => {
    await page.goto('/products');
    const a = await findProduct(page, 'Cents A');
    await add(page, a);
    const second = await context.newPage();
    await second.goto('/cart');
    await expect(second.getByLabel('Quantity for Cents A', { exact: true })).toHaveValue('1');
    await page.bringToFront();
    await page.getByRole('button', { name: 'Add to cart', exact: true }).click();
    await second.bringToFront();
    await expect(second.getByLabel('Quantity for Cents A', { exact: true })).toHaveValue('2');
    await second.getByRole('button', { name: 'Clear cart', exact: true }).click();
    await page.bringToFront();
    await page.getByRole('link', { name: 'View your cart', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Your cart is empty.' })).toBeVisible();
});
