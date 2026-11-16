import { expect, it } from 'vitest';
import { PendingOperations, checkoutBody, pendingKey } from '../src/checkout/pending';
import { ApiError } from '../src/api/client';
const A = '11111111-1111-4111-8111-111111111111';
const B = '22222222-2222-4222-8222-222222222222';
const key = '33333333-3333-4333-8333-333333333333';
const orderId = '44444444-4444-4444-8444-444444444444';
const items = [
    { productId: B.toUpperCase(), quantity: 2 },
    { productId: A, quantity: 1 },
];
function memory() {
    const values = new Map<string, string>();
    return {
        getItem: (name: string) => values.get(name) ?? null,
        setItem: (name: string, value: string) => {
            values.set(name, value);
        },
        removeItem: (name: string) => {
            values.delete(name);
        },
    };
}
const immediate = <T>(action: () => Promise<T>) => action();
function deferred() {
    let resolve!: () => void;
    const promise = new Promise<void>((yes) => {
        resolve = yes;
    });
    return { promise, resolve };
}
it('normalizes exactly once with lower-case IDs/sorted products/integers and no catalog fields', () => {
    expect(checkoutBody(items)).toBe(
        JSON.stringify({
            items: [
                { productId: A, quantity: 1 },
                { productId: B, quantity: 2 },
            ],
        }),
    );
    expect(() => checkoutBody([])).toThrow();
    expect(() => checkoutBody([{ productId: A, quantity: 1.5 }])).toThrow();
    expect(() =>
        checkoutBody([
            { productId: A, quantity: 1 },
            { productId: A, quantity: 2 },
        ]),
    ).toThrow('Duplicate');
});
it.each(['timeout', 'transport', '503'])(
    'keeps exact payload/key through %s, reload and a changed cart, then replays original',
    async (kind) => {
        const storage = memory();
        const first = new PendingOperations(A, storage, immediate, () => key);
        const failure =
            kind === '503'
                ? new ApiError({
                      status: 503,
                      title: 'Uncertain',
                      code: 'service_unavailable',
                      traceId: 'retained',
                      errors: {},
                  })
                : kind === 'timeout'
                  ? new DOMException('timeout', 'TimeoutError')
                  : new TypeError('connection reset');
        await expect(
            first.checkout(items, async (record) => {
                expect(JSON.parse(storage.getItem(pendingKey(A))!).checkout).toEqual(record);
                throw failure;
            }),
        ).rejects.toBe(failure);
        const original = first.getSnapshot().checkout!;
        const restored = new PendingOperations(A, storage, immediate, () => {
            throw new Error('Must not generate another key.');
        });
        await restored.checkout([{ productId: B, quantity: 99 }], async (record) => {
            expect(record).toEqual(original);
            return { id: orderId };
        });
        expect(restored.getSnapshot().checkout).toEqual({ ...original, orderId });
        await restored.closeCheckout(key, true);
        expect(restored.getSnapshot().checkout).toBeNull();
    },
);
it('does not send when durable storage is denied and fails closed for corrupt/future/oversized records', async () => {
    const store = new PendingOperations(
        A,
        {
            getItem: () => null,
            setItem: () => {
                throw new Error('quota');
            },
            removeItem: () => {},
        },
        immediate,
        () => key,
    );
    let sent = false;
    await expect(
        store.checkout(items, async () => {
            sent = true;
            return { id: orderId };
        }),
    ).rejects.toThrow();
    expect(sent).toBe(false);
    for (const raw of [
        '{broken',
        JSON.stringify({ version: 999, checkout: null, payments: [] }),
        'x'.repeat(24_577),
    ]) {
        const storage = memory();
        storage.setItem(pendingKey(A), raw);
        const service = new PendingOperations(A, storage, immediate, () => key);
        expect(service.getSnapshot().error).toContain('No new request');
        await expect(
            service.checkout(items, async () => {
                sent = true;
                return { id: orderId };
            }),
        ).rejects.toThrow();
        expect(sent).toBe(false);
        await service.resetInvalid();
        expect(service.getSnapshot()).toEqual({ checkout: null, payments: [] });
    }
});
it('isolates two authenticated user scopes without clearing unresolved owner recovery on logout', async () => {
    const storage = memory();
    const a = new PendingOperations(A, storage, immediate, () => key);
    await expect(
        a.checkout(items, async () => {
            throw new Error('lost');
        }),
    ).rejects.toThrow();
    const b = new PendingOperations(B, storage, immediate, () => B);
    expect(b.getSnapshot().checkout).toBeNull();
    await b.checkout(items, async () => ({ id: orderId }));
    expect(new PendingOperations(A, storage, immediate).getSnapshot().checkout?.key).toBe(key);
    expect(b.getSnapshot().checkout?.key).toBe(B);
    expect(JSON.stringify(a.getSnapshot())).not.toContain('email');
});
it('serializes two tab intentions and makes the second read the same committed record', async () => {
    const storage = memory();
    let queue = Promise.resolve();
    const lock = <T>(action: () => Promise<T>): Promise<T> => {
        const result = queue.then(action);
        queue = result.then(
            () => {},
            () => {},
        );
        return result;
    };
    const one = new PendingOperations(A, storage, lock, () => key);
    const two = new PendingOperations(A, storage, lock, () => B);
    const arrived = deferred();
    const gate = deferred();
    let posts = 0;
    const first = one.checkout(items, async () => {
        posts++;
        arrived.resolve();
        await gate.promise;
        return { id: orderId };
    });
    await arrived.promise;
    const second = two.checkout([{ productId: B, quantity: 5 }], async () => {
        posts++;
        return { id: A };
    });
    gate.resolve();
    const results = await Promise.all([first, second]);
    expect(posts).toBe(1);
    expect(results[0]).toEqual(results[1]);
    expect(results[1].key).toBe(key);
});
it('requires explicit recovery abandonment or resolved close and protects against a changed key', async () => {
    const service = new PendingOperations(A, memory(), immediate, () => key);
    await expect(
        service.checkout(items, async () => {
            throw new Error('lost');
        }),
    ).rejects.toThrow();
    await expect(service.closeCheckout(key, true)).rejects.toThrow('Resolve');
    await expect(service.closeCheckout(B, false)).rejects.toThrow('changed');
    expect(service.getSnapshot().checkout?.key).toBe(key);
    await service.closeCheckout(key, false);
    expect(service.getSnapshot().checkout).toBeNull();
});
it('replays an uncertain payment key and only replaces a resolved failed attempt on an explicit new request', async () => {
    const storage = memory();
    let sequence = 0;
    const service = new PendingOperations(A, storage, immediate, () => (sequence++ === 0 ? key : B));
    await expect(
        service.payment(orderId, async () => {
            throw new DOMException('timeout', 'TimeoutError');
        }),
    ).rejects.toThrow();
    expect(service.getSnapshot().payments).toEqual([{ orderId, key }]);
    await expect(service.payment(orderId, async () => ({ id: A, orderId }), true)).rejects.toThrow('Resolve');
    const reload = new PendingOperations(A, storage, immediate, () => B);
    await reload.payment(orderId, async (record) => {
        expect(record.key).toBe(key);
        return { id: A, orderId };
    });
    await reload.payment(orderId, async () => {
        throw new Error('Already acknowledged; read durable status.');
    });
    await reload.payment(
        orderId,
        async (record) => {
            expect(record.key).toBe(B);
            return { id: key, orderId };
        },
        true,
    );
    expect(reload.getSnapshot().payments).toEqual([{ orderId, key: B, attemptId: key }]);
});

it('does not silently create a new key if unresolved recovery disappears while the tab is open', async () => {
    const storage = memory();
    const service = new PendingOperations(A, storage, immediate, () => key);
    await expect(
        service.checkout(items, async () => {
            throw new Error('lost');
        }),
    ).rejects.toThrow();
    storage.removeItem(pendingKey(A));
    service.reload();
    expect(service.getSnapshot().checkout?.key).toBe(key);
    expect(service.getSnapshot().error).toContain('No new request');
    let sent = false;
    await expect(
        service.checkout(items, async () => {
            sent = true;
            return { id: orderId };
        }),
    ).rejects.toThrow();
    expect(sent).toBe(false);
    await service.resetInvalid();
    expect(service.getSnapshot().checkout).toBeNull();
});
it('an explicit payment-recovery discard is fenced by its original order/key', async () => {
    const service = new PendingOperations(A, memory(), immediate, () => key);
    await expect(
        service.payment(orderId, async () => {
            throw new Error('lost');
        }),
    ).rejects.toThrow();
    await expect(service.closePayment(orderId, B)).rejects.toThrow('changed');
    expect(service.getSnapshot().payments[0]?.key).toBe(key);
    await service.closePayment(orderId, key);
    expect(service.getSnapshot().payments).toEqual([]);
});
