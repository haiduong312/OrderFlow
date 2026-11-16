import { expect, it } from 'vitest';
import { AdminRecovery, adminRecoveryKey, replayable, validateCommand } from '../src/admin/commands';
import type { AdminCommand } from '../src/admin/commands';
import { adminKey, adminOptions, affectedResources, invalidateAdmin } from '../src/admin/api';
import { allowedFulfillment, integerInput, reportMoney, validRange } from '../src/admin/rules';
import { createQueryClient } from '../src/api/queries';
import { ApiClient, ApiError } from '../src/api/client';
const A = '11111111-1111-4111-8111-111111111111';
const B = '22222222-2222-4222-8222-222222222222';
const op = '33333333-3333-4333-8333-333333333333';
const stock: AdminCommand = {
    resource: `products/${A}/stock-adjustments`,
    method: 'POST',
    body: { operationId: op, delta: -2, reason: 'Counted stock' },
};
const immediate = <T>(task: () => Promise<T>) => task();
function memory() {
    const values = new Map<string, string>();
    return {
        getItem: (key: string) => values.get(key) ?? null,
        setItem: (key: string, value: string) => {
            values.set(key, value);
        },
    };
}
it('retains the original stock operationId/payload after transport loss/503 and reload, blocks changed writes, and recovers it', async () => {
    const storage = memory();
    const one = new AdminRecovery(A, storage, immediate);
    await expect(
        one.execute(stock, async (command) => {
            expect(JSON.parse(storage.getItem(adminRecoveryKey(A))!).command).toEqual(command);
            throw new TypeError('connection lost');
        }),
    ).rejects.toThrow();
    const restored = new AdminRecovery(A, storage, immediate);
    await expect(
        restored.execute({ ...stock, body: { ...stock.body, delta: 99 } }, async () => 1),
    ).rejects.toThrow('Resolve');
    const unavailable = new ApiError({
        status: 503,
        title: 'Unavailable',
        code: 'service_unavailable',
        traceId: 'retained',
        errors: {},
    });
    await expect(
        restored.execute(
            stock,
            async () => {
                throw unavailable;
            },
            true,
        ),
    ).rejects.toBe(unavailable);
    expect(restored.getSnapshot().command).toEqual(stock);
    await restored.execute(
        stock,
        async (command) => {
            expect(command).toEqual(stock);
            return { revision: 5 };
        },
        true,
    );
    expect(restored.getSnapshot().command).toBeNull();
});
it('retains original email delivery/operation identity and never adds counter/provider/payment commands', async () => {
    const email: AdminCommand = {
        resource: `email-deliveries/${A}/retry`,
        method: 'POST',
        body: { operationId: op, reason: 'Sink restored', acknowledgeDuplicateRisk: true },
    };
    expect(replayable(email)).toBe(true);
    const service = new AdminRecovery(A, memory(), immediate);
    await expect(
        service.execute(email, async () => {
            throw new Error('timeout');
        }),
    ).rejects.toThrow();
    await service.execute(
        email,
        async (actual) => {
            expect(actual).toEqual(email);
            return { id: A, attemptCount: 8 };
        },
        true,
    );
    expect(() => validateCommand({ ...email, body: { ...email.body, attemptCount: 0 } })).toThrow();
    for (const resource of [
        `refunds/${A}/retry`,
        'jobs/execute',
        'outbox/replay',
        `orders/${A}/payments`,
        'sql',
    ])
        expect(() => validateCommand({ resource, method: 'POST', body: {} })).toThrow('Unsupported');
});
it('does not replay a non-idempotent category create and allows only explicit closure after uncertainty', async () => {
    const category: AdminCommand = { resource: 'categories', method: 'POST', body: { name: 'Workspace' } };
    const service = new AdminRecovery(A, memory(), immediate);
    await expect(
        service.execute(category, async () => {
            throw new Error('lost');
        }),
    ).rejects.toThrow();
    expect(replayable(category)).toBe(false);
    await expect(service.execute(category, async () => 1, true)).rejects.toThrow('Resolve');
    await service.close();
    expect(service.getSnapshot().command).toBeNull();
});
it('a definite first-request rejection permits editing, while a rejection after uncertainty keeps the record', async () => {
    const invalid = new ApiError({
        status: 409,
        title: 'Stock conflict',
        code: 'inventory_conflict',
        traceId: 'stock',
        errors: {},
    });
    const service = new AdminRecovery(A, memory(), immediate);
    await expect(
        service.execute(stock, async () => {
            throw invalid;
        }),
    ).rejects.toBe(invalid);
    expect(service.getSnapshot().command).toBeNull();
    await expect(
        service.execute(stock, async () => {
            throw new Error('lost');
        }),
    ).rejects.toThrow();
    await expect(
        service.execute(
            stock,
            async () => {
                throw invalid;
            },
            true,
        ),
    ).rejects.toBe(invalid);
    expect(service.getSnapshot().command).toEqual(stock);
});
it('fails closed on malformed/future/oversized/missing recovery and storage denial, and scopes users', async () => {
    for (const raw of ['{broken', '{"version":2,"command":null}', '{"version":1}', 'x'.repeat(12_289)]) {
        const storage = memory();
        storage.setItem(adminRecoveryKey(A), raw);
        const service = new AdminRecovery(A, storage, immediate);
        let sent = false;
        expect(service.getSnapshot().error).not.toBeNull();
        await expect(
            service.execute(stock, async () => {
                sent = true;
            }),
        ).rejects.toThrow();
        expect(sent).toBe(false);
    }
    const storage = memory();
    const one = new AdminRecovery(A, storage, immediate);
    await expect(
        one.execute(stock, async () => {
            throw new Error('lost');
        }),
    ).rejects.toThrow();
    expect(new AdminRecovery(B, storage, immediate).getSnapshot().command).toBeNull();
    const denied = new AdminRecovery(
        A,
        {
            getItem: () => null,
            setItem: () => {
                throw new Error('quota');
            },
        },
        immediate,
    );
    let sent = false;
    await expect(
        denied.execute(stock, async () => {
            sent = true;
        }),
    ).rejects.toThrow();
    expect(sent).toBe(false);
    const bounded = new AdminRecovery(B, memory(), immediate);
    await expect(
        bounded.execute(
            { resource: 'categories', method: 'POST', body: { name: 'é'.repeat(12_288) } },
            async () => {
                sent = true;
            },
        ),
    ).rejects.toThrow('12 KiB');
    expect(sent).toBe(false);
});
it('Admin query keys include scope/resource and all server parameters; query execution forwards AbortSignal', async () => {
    const base = { q: '', categoryId: '', sort: 'name', page: 1, pageSize: 20 };
    for (const patch of [
        { q: 'desk' },
        { categoryId: A },
        { sort: 'price-asc' },
        { page: 2 },
        { pageSize: 100 },
    ])
        expect(adminKey(A, 'products', { ...base, ...patch })).not.toEqual(adminKey(A, 'products', base));
    expect(adminKey(A, 'products', base)).not.toEqual(adminKey(B, 'products', base));
    expect(adminKey(A, 'products', base)).not.toEqual(adminKey(A, 'orders', base));
    const client = createQueryClient();
    let signal: AbortSignal | null | undefined;
    let path = '';
    const api = new ApiClient(async (url, options) => {
        path = String(url);
        signal = options?.signal;
        return new Response(JSON.stringify({ items: [] }), {
            headers: { 'Content-Type': 'application/json' },
        });
    });
    await client.fetchQuery(
        adminOptions(
            api,
            { guard: async (_id, work) => work(), changed: () => {} },
            A,
            'audit/stock-movements',
            { from: '2035-01-01', to: '2035-01-02', page: 2, pageSize: 20, productId: B },
        ),
    );
    expect(signal).toBeInstanceOf(AbortSignal);
    expect(path).toContain('page=2&pageSize=20&productId=');
    client.clear();
});
it('invalidates the changed entity/module filters only, preserving other Admin/customer/private data', async () => {
    const client = createQueryClient();
    const matching = [
        adminKey(A, 'products', { q: 'x' }),
        adminKey(A, `products/${A}/inventory`),
        adminKey(A, `products/${A}/stock-history`),
        adminKey(A, 'reports/low-stock'),
    ];
    const untouched = [
        adminKey(B, 'products'),
        adminKey(A, `products/${B}/inventory`),
        adminKey(A, 'reports/sales'),
        ['private', A, 'orders'],
        ['public', 'product', B],
    ];
    [...matching, ...untouched].forEach((key) => client.setQueryData(key, { marker: true }));
    await invalidateAdmin(client, A, stock.resource);
    matching.forEach((key) => expect(client.getQueryState(key)?.isInvalidated).toBe(true));
    untouched.forEach((key) => expect(client.getQueryState(key)?.isInvalidated).toBe(false));
    expect(affectedResources(`orders/${A}/transitions`).admin).toContain(`audit/orders/${A}/history`);
    client.clear();
});
it('fulfillment choices mirror only the supported graph and never mark PendingPayment as paid', () => {
    expect(allowedFulfillment('Confirmed')).toBe('Processing');
    expect(allowedFulfillment('Processing')).toBe('Shipped');
    expect(allowedFulfillment('Shipped')).toBe('Completed');
    for (const state of ['PendingPayment', 'Completed', 'Cancelled', 'Expired'] as const)
        expect(allowedFulfillment(state)).toBeNull();
});
it('keeps signed large reporting money exact and bounds UTC ranges and integer stock inputs', () => {
    expect(reportMoney('-12.50')).toBe('-$12.50');
    expect(reportMoney('900719925474099123456789.10')).toBe('$900,719,925,474,099,123,456,789.10');
    expect(validRange('2035-01-01', '2035-01-02')).toBe(true);
    expect(validRange('2035-01-01', '2035-01-01')).toBe(false);
    expect(validRange('2035-01-01', '2037-01-01')).toBe(false);
    expect(validRange('2035-02-30', '2035-03-02')).toBe(false);
    expect(integerInput('-2', -2147483648)).toBe(-2);
    for (const value of ['1.2', '1e3', '2147483648', 'NaN'])
        expect(() => integerInput(value, -2147483648)).toThrow();
});
it('blocks a second submit before disabled controls render and sends only one durable stock intention', async () => {
    let release!: () => void;
    let entered!: () => void;
    const gate = new Promise<void>((resolve) => {
        release = resolve;
    });
    const reached = new Promise<void>((resolve) => {
        entered = resolve;
    });
    const service = new AdminRecovery(A, memory(), immediate);
    let sends = 0;
    const first = service.execute(stock, async () => {
        sends++;
        entered();
        await gate;
        return 1;
    });
    await reached;
    expect(service.getSnapshot().running).toBe(true);
    await expect(
        service.execute({ ...stock, body: { ...stock.body, operationId: B } }, async () => {
            sends++;
        }),
    ).rejects.toThrow('already in progress');
    release();
    await first;
    expect(sends).toBe(1);
    expect(service.getSnapshot().command).toBeNull();
    expect(service.getSnapshot().running).toBe(false);
});
