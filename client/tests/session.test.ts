import { expect, it } from 'vitest';
import { ApiClient, ApiError } from '../src/api/client';
import { createQueryClient, isPrivate, keys } from '../src/api/queries';
import { SessionController } from '../src/auth/session';
import type { User } from '../src/api/types';

const A: User = { id: 'user-a', email: 'a@example.invalid', roles: ['Customer'] };
const B: User = { id: 'user-b', email: 'b@example.invalid', roles: ['Customer'] };
const json = (body: unknown, status = 200) =>
    new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });
function deferred<T>() {
    let resolve!: (value: T) => void;
    const promise = new Promise<T>((yes) => {
        resolve = yes;
    });
    return { promise, resolve };
}

it('a late old-user 401 cannot expire the new verified account', async () => {
    let identity = A;
    const client = createQueryClient();
    const api = new ApiClient(async (path) => {
        if (String(path).endsWith('antiforgery'))
            return json({ requestToken: 'token', headerName: 'X-CSRF-TOKEN' });
        if (String(path).endsWith('login')) identity = B;
        return json(identity);
    });
    const session = new SessionController(client, api);
    await session.refresh();
    let reject!: (error: unknown) => void;
    const late = new Promise<never>((_, no) => {
        reject = no;
    });
    const old = session.guard(A.id, () => late);
    await session.login({ email: B.email, password: 'not-persisted' });
    reject(
        new ApiError({
            status: 401,
            title: 'Old session expired',
            code: 'unauthenticated',
            traceId: 'old-401',
            errors: {},
        }),
    );
    await expect(old).rejects.toMatchObject({ name: 'AbortError' });
    expect(session.getSnapshot()).toEqual({ phase: 'authenticated', user: B });
    client.clear();
});

it('purges private cache before login is sent and rejects late old-user results even if abort was ignored', async () => {
    let identity: User | null = A;
    const client = createQueryClient();
    const sent = deferred<void>();
    const allowLogin = deferred<void>();
    const api = new ApiClient(async (path) => {
        if (String(path).endsWith('antiforgery'))
            return json({ headerName: 'X-CSRF-TOKEN', requestToken: `for-${identity?.id}` });
        if (String(path).endsWith('login')) {
            expect(client.getQueryCache().getAll().filter(isPrivate)).toHaveLength(0);
            sent.resolve();
            await allowLogin.promise;
            identity = B;
            return json(A); // Login DTO is deliberately different; /me determines the cookie identity.
        }
        return identity
            ? json(identity)
            : json({ title: 'Unauthorized', code: 'unauthenticated', errors: {} }, 401);
    });
    const session = new SessionController(client, api);
    await session.refresh();
    client.setQueryData(keys.orders(A.id, { page: 1, pageSize: 20 }), { privateMarker: 'A-only' });
    client.setQueryData(keys.health, { status: 'Healthy' });
    const delayed = deferred<string>();
    const old = session.guard(A.id, () => delayed.promise);
    const changing = session.login({ email: B.email, password: 'not-persisted' });
    await sent.promise;
    expect(session.getSnapshot().phase).toBe('changing');
    expect(
        JSON.stringify(
            client
                .getQueryCache()
                .getAll()
                .map((query) => query.state.data),
        ),
    ).not.toContain('A-only');
    allowLogin.resolve();
    await changing;
    expect(session.getSnapshot()).toEqual({ phase: 'authenticated', user: B });
    delayed.resolve('old-user-result');
    await expect(old).rejects.toMatchObject({ name: 'AbortError' });
    expect(client.getQueryData(keys.health)).toEqual({ status: 'Healthy' });
    client.clear();
});

it('a failed logout stays an error and re-verifies the surviving cookie instead of pretending success', async () => {
    const client = createQueryClient();
    const api = new ApiClient(async (path) =>
        String(path).endsWith('antiforgery')
            ? json({ requestToken: 'token', headerName: 'X-CSRF-TOKEN' })
            : String(path).endsWith('logout')
              ? json(
                    {
                        title: 'Database unavailable',
                        code: 'service_unavailable',
                        traceId: 'failed-logout',
                        errors: {},
                    },
                    503,
                )
              : json(A),
    );
    const session = new SessionController(client, api);
    await session.refresh();
    client.setQueryData(keys.orders(A.id, { page: 1, pageSize: 20 }), { privateMarker: 'old-data' });
    await expect(session.logout()).rejects.toMatchObject({ status: 503, traceId: 'failed-logout' });
    expect(session.getSnapshot()).toEqual({ phase: 'authenticated', user: A });
    expect(client.getQueryData(keys.orders(A.id, { page: 1, pageSize: 20 }))).toBeUndefined();
    client.clear();
});

it('a failed session restore is unavailable, while only 401 means anonymous', async () => {
    const client = createQueryClient();
    let status = 500;
    const api = new ApiClient(async () =>
        json({ title: 'Failed', code: 'internal_error', traceId: 'restore-failure', errors: {} }, status),
    );
    const session = new SessionController(client, api);
    await session.refresh();
    expect(session.getSnapshot().phase).toBe('unavailable');
    status = 401;
    await session.refresh();
    expect(session.getSnapshot().phase).toBe('anonymous');
    client.clear();
});

it('registration never replaces the current session with the newly created account', async () => {
    const client = createQueryClient();
    const api = new ApiClient(async (path) =>
        String(path).endsWith('antiforgery')
            ? json({ requestToken: 'token', headerName: 'X-CSRF-TOKEN' })
            : String(path).endsWith('register')
              ? json(B, 201)
              : json(A),
    );
    const session = new SessionController(client, api);
    await session.refresh();
    await session.register({ email: B.email, password: 'not-persisted' });
    expect(session.getSnapshot()).toEqual({ phase: 'authenticated', user: A });
    client.clear();
});
