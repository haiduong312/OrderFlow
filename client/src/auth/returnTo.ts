export function safeReturnTo(value: unknown): string {
    if (typeof value !== 'string' || value.includes('\\')) return '/account';
    const path = value.split('?')[0];
    return path === '/orders' || path === '/account' || path === '/cart' ? value : '/account';
}
