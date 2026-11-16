export function safeReturnTo(value: unknown): string {
    if (typeof value !== 'string' || value.includes('\\')) return '/account';
    const path = value.split('?')[0];
    const admin =
        [
            '/admin',
            '/admin/products',
            '/admin/products/new',
            '/admin/categories',
            '/admin/orders',
            '/admin/reports',
            '/admin/audit',
            '/admin/emails',
        ].includes(path) ||
        /^\/admin\/(products|orders)\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(?:\/stock)?$/i.test(
            path,
        );
    return admin ||
        path === '/orders' ||
        path === '/account' ||
        path === '/cart' ||
        path === '/checkout' ||
        path === '/notifications' ||
        /^\/orders\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(path)
        ? value
        : '/account';
}
