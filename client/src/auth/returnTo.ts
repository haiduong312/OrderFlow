export function safeReturnTo(value: unknown): string {
    if (typeof value !== 'string' || value.includes('\\')) return '/account';
    const path = value.split('?')[0];
    return path === '/orders' ||
        path === '/account' ||
        path === '/cart' ||
        path === '/checkout' ||
        path === '/notifications' ||
        /^\/orders\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(path)
        ? value
        : '/account';
}
