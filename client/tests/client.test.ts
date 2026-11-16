import { describe, expect, it, vi } from 'vitest';
import { ApiClient, ApiError } from '../src/api/client';
import { formatUsd, fromCents, toCents } from '../src/api/money';
import { keys } from '../src/api/queries';
import { safeReturnTo } from '../src/auth/returnTo';

const json = (value: unknown, status = 200, headers: Record<string, string> = {}) =>
    new Response(JSON.stringify(value), {
        status,
        headers: { 'Content-Type': 'application/json', ...headers },
    });

describe('HTTP contract', () => {
    it('does not submit a late token from an old identity generation', async () => {
        let release!: (value: Response) => void;
        const delayed = new Promise<Response>((yes) => {
            release = yes;
        });
        let tokens = 0;
        const sent: string[] = [];
        const fetcher = vi.fn<typeof fetch>().mockImplementation(async (path) => {
            if (String(path).endsWith('antiforgery')) {
                tokens++;
                if (tokens === 1) return delayed;
                return json({ requestToken: 'new-identity', headerName: 'X-CSRF-TOKEN' });
            }
            sent.push(String(path));
            return json({ ok: true });
        });
        const api = new ApiClient(fetcher);
        const old = api.request('/api/old-action', { method: 'POST' });
        api.invalidateCsrf();
        await api.request('/api/new-action', { method: 'POST' });
        release(json({ requestToken: 'old-identity', headerName: 'X-CSRF-TOKEN' }));
        await expect(old).rejects.toMatchObject({ name: 'AbortError' });
        expect(sent).toEqual(['/api/new-action']);
    });
    it('rejects failed HTTP statuses and preserves code, trace, and case-insensitive field messages', async () => {
        const api = new ApiClient(
            vi.fn<typeof fetch>().mockResolvedValue(
                json(
                    {
                        title: 'Invalid fields',
                        status: 200,
                        code: 'validation_failed',
                        traceId: 'server-trace',
                        errors: { Password: ['Use a symbol.'] },
                    },
                    400,
                ),
            ),
        );
        const error = await api.request('/api/probe').catch((value) => value as ApiError);
        expect(error).toBeInstanceOf(ApiError);
        if (!(error instanceof ApiError)) throw new Error('Expected ApiError');
        expect(error.status).toBe(400);
        expect(error.code).toBe('validation_failed');
        expect(error.traceId).toBe('server-trace');
        expect(error.field('password')).toEqual(['Use a symbol.']);
    });
    it('keeps non-JSON failures as errors and retains header trace/rate-limit information', async () => {
        const api = new ApiClient(
            vi.fn<typeof fetch>().mockResolvedValue(
                new Response('<html>unavailable</html>', {
                    status: 503,
                    headers: { 'X-Trace-Id': 'proxy-trace', 'Retry-After': '12' },
                }),
            ),
        );
        await expect(api.request('/api/probe')).rejects.toMatchObject({
            status: 503,
            code: 'http_error',
            traceId: 'proxy-trace',
            retryAfter: '12',
        });
    });
    it('rejects malformed success JSON rather than manufacturing empty data', async () => {
        const api = new ApiClient(
            vi.fn<typeof fetch>().mockResolvedValue(
                new Response('{', {
                    headers: { 'Content-Type': 'application/json', 'X-Trace-Id': 'bad-body' },
                }),
            ),
        );
        await expect(api.request('/api/probe')).rejects.toMatchObject({
            code: 'invalid_response',
            traceId: 'bad-body',
        });
    });
    it('uses same-origin cookies, cancellation, and accepts a real 204 without JSON', async () => {
        const fetcher = vi
            .fn<typeof fetch>()
            .mockImplementation(async (path) =>
                String(path).endsWith('antiforgery')
                    ? json({ requestToken: 'csrf-memory', headerName: 'X-CSRF-TOKEN' })
                    : new Response(null, { status: 204 }),
            );
        const api = new ApiClient(fetcher);
        const abort = new AbortController();
        expect(
            await api.request('/api/auth/logout', { method: 'POST', signal: abort.signal }),
        ).toBeUndefined();
        const options = fetcher.mock.calls[1][1]!;
        expect(options.credentials).toBe('same-origin');
        expect(options.signal).toBe(abort.signal);
        expect(new Headers(options.headers).get('X-CSRF-TOKEN')).toBe('csrf-memory');
    });
    it('does not automatically replay a CSRF-rejected mutation; the next attempt gets a new token', async () => {
        let tokens = 0;
        let posts = 0;
        const fetcher = vi.fn<typeof fetch>().mockImplementation(async (path) => {
            if (String(path).endsWith('antiforgery'))
                return json({ requestToken: `token-${++tokens}`, headerName: 'X-CSRF-TOKEN' });
            posts++;
            return posts === 1
                ? json(
                      {
                          title: 'CSRF rejected',
                          code: 'antiforgery_failed',
                          traceId: 'csrf-trace',
                          errors: {},
                      },
                      400,
                  )
                : json({ ok: true });
        });
        const api = new ApiClient(fetcher);
        await expect(api.request('/api/action', { method: 'POST' })).rejects.toMatchObject({
            code: 'antiforgery_failed',
        });
        expect(posts).toBe(1);
        expect(tokens).toBe(1);
        await api.request('/api/action', { method: 'POST' });
        expect(posts).toBe(2);
        expect(tokens).toBe(2);
        expect(new Headers(fetcher.mock.calls[3][1]?.headers).get('X-CSRF-TOKEN')).toBe('token-2');
    });
    it.each([
        'https://elsewhere.invalid/api/me',
        '//elsewhere.invalid/api/me',
        '/elsewhere',
        '/api/../outside',
        '/api/%2e%2e/outside',
        '/api/\\elsewhere',
        '/api/me#fragment',
    ])('rejects a non-API path %s before sending cookies', async (path) => {
        const fetcher = vi.fn<typeof fetch>();
        const api = new ApiClient(fetcher);
        await expect(api.request(path)).rejects.toThrow('same-origin');
        expect(fetcher).not.toHaveBeenCalled();
    });
});

describe('exact money and scoped query identity', () => {
    it.each([
        'https://elsewhere.invalid',
        '//elsewhere.invalid',
        '/\\elsewhere',
        '/login',
        '/register',
        '/unknown',
        null,
    ])('rejects unsafe or unsupported return destinations %s', (value) => {
        expect(safeReturnTo(value)).toBe('/account');
    });
    it('retains a supported private destination and filters after sign in', () => {
        expect(safeReturnTo('/orders?page=2&status=Completed')).toBe('/orders?page=2&status=Completed');
    });
    it('formats exact cents, including values beyond Number precision', () => {
        expect(fromCents(toCents('0.10') * 3n)).toBe('0.30');
        expect(formatUsd('9999999999999999.99')).toBe('$9,999,999,999,999,999.99');
        expect(() => fromCents(1000000000000000000n)).toThrow();
    });
    it.each(['1.001', '1e2', '01.00', '-1.00', ' 1.00', '1,000.00', '1.0'])(
        'rejects malformed public money %s',
        (value) => expect(() => toCents(value)).toThrow(),
    );
    it('separates every order filter/page and current-user scope', () => {
        const first = keys.orders('A', { page: 1, pageSize: 20 });
        expect(first).not.toEqual(keys.orders('B', { page: 1, pageSize: 20 }));
        expect(first).not.toEqual(keys.orders('A', { page: 2, pageSize: 20 }));
        expect(first).not.toEqual(keys.orders('A', { page: 1, pageSize: 100 }));
        expect(first).not.toEqual(keys.orders('A', { page: 1, pageSize: 20, status: 'Completed' }));
    });
});
