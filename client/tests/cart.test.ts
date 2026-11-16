import { expect, it, vi } from 'vitest';
import {
    CART_KEY,
    CART_VERSION,
    MAX_LINES,
    createCartStore,
    migrateCart,
    parseQuantity,
    sanitizeItems,
} from '../src/state/cart';
import { estimatedLine, estimatedTotal, itemIssue } from '../src/cart/estimate';
import type { Product } from '../src/api/types';

const A = '11111111-1111-4111-8111-111111111111';
const B = '22222222-2222-4222-8222-222222222222';
function memory(initial?: string) {
    const values = new Map(initial ? [[CART_KEY, initial]] : []);
    return {
        getItem: (key: string) => values.get(key) ?? null,
        setItem: (key: string, value: string) => {
            values.set(key, value);
        },
        removeItem: (key: string) => {
            values.delete(key);
        },
    };
}
const product = (id = A, price = '0.10'): Product => ({
    id,
    price,
    sku: 'EXACT-CENTS',
    name: 'Exact cents',
    description: '',
    currency: 'USD',
    category: { id: B, name: 'Category', isActive: true },
    isActive: true,
    version: 1,
    available: 10,
});

it('cart actions update immutably, merge duplicate additions, remove and clear', () => {
    const store = createCartStore(memory());
    store.getState().add(A, 2);
    const before = store.getState().items;
    store.getState().add(B, 1);
    expect(store.getState().items).not.toBe(before);
    expect(before).toEqual([{ productId: A, quantity: 2 }]);
    expect(store.getState().items[0]).toBe(before[0]);
    store.getState().add(A.toUpperCase(), 3);
    expect(store.getState().items).toEqual([
        { productId: A, quantity: 5 },
        { productId: B, quantity: 1 },
    ]);
    expect(before[0]!.quantity).toBe(2);
    store.getState().update(A, 1000);
    expect(() => store.getState().add(A, 1)).toThrow('at most 1000');
    expect(store.getState().items[0]!.quantity).toBe(1000);
    store.getState().remove(B);
    expect(store.getState().items).toHaveLength(1);
    store.getState().clear();
    expect(store.getState().items).toEqual([]);
});

it.each([0, -1, 1.2, NaN, Infinity, 1001, Number.MAX_SAFE_INTEGER])(
    'rejects invalid action quantity %s without changing the cart',
    (quantity) => {
        const store = createCartStore(memory());
        store.getState().add(A, 2);
        const before = store.getState().items;
        expect(() => store.getState().add(B, quantity)).toThrow();
        expect(() => store.getState().update(A, quantity)).toThrow();
        expect(store.getState().items).toBe(before);
    },
);

it.each(['', '0', '-1', '1.5', '1e2', ' 2', '02', '1001', 'Infinity'])(
    'rejects ambiguous/invalid quantity input %j',
    (value) => {
        expect(() => parseQuantity(value)).toThrow('whole quantity');
    },
);
it('accepts exact integer input at both boundaries', () => {
    expect(parseQuantity('1')).toBe(1);
    expect(parseQuantity('1000')).toBe(1000);
});
it('bounds line count and rejects invalid or empty product identities', () => {
    const store = createCartStore(memory());
    for (let n = 0; n < MAX_LINES; n++)
        store.getState().add(`11111111-1111-4111-8111-${String(n).padStart(12, '0')}`, 1);
    expect(() => store.getState().add(B, 1)).toThrow('100 different');
    expect(() => store.getState().add('bad-id', 1)).toThrow();
    expect(() => store.getState().add('00000000-0000-0000-0000-000000000000', 1)).toThrow();
    expect(store.getState().items).toHaveLength(100);
});
it('reloads only IDs/quantities and strips unknown snapshot/auth fields on hydration and persistence', () => {
    const storage = memory(
        JSON.stringify({
            version: CART_VERSION,
            state: {
                items: [
                    { productId: A, quantity: 2, price: '0.01', available: 99999 },
                    { productId: A, quantity: 900 },
                    { productId: B, quantity: -1 },
                ],
                roles: ['Admin'],
                user: { id: 'private-user' },
                total: '0.00',
            },
        }),
    );
    const store = createCartStore(storage);
    expect(store.getState().items).toEqual([{ productId: A, quantity: 2 }]);
    expect(store.getState()).not.toHaveProperty('roles');
    store.getState().add(B, 3);
    const saved = JSON.parse(storage.getItem(CART_KEY)!);
    expect(saved).toEqual({
        state: {
            items: [
                { productId: A, quantity: 2 },
                { productId: B, quantity: 3 },
            ],
        },
        version: 1,
    });
    expect(createCartStore(storage).getState().items).toEqual(saved.state.items);
});
it.each([0, 2, 999])(
    'resets unknown persisted version %s instead of guessing an unsafe migration',
    (version) => {
        const storage = memory(
            JSON.stringify({ version, state: { items: [{ productId: A, quantity: 2 }] } }),
        );
        const store = createCartStore(storage);
        expect(store.getState().items).toEqual([]);
        expect(JSON.parse(storage.getItem(CART_KEY)!).version).toBe(1);
        expect(migrateCart({ items: [] }, version)).toEqual({ items: [] });
    },
);
it('handles malformed/unavailable storage visibly while retaining in-memory cart actions', () => {
    const warning = vi.spyOn(console, 'warn').mockImplementation(() => {});
    try {
        const broken = createCartStore({
            getItem: () => {
                throw new Error('private browser detail');
            },
            setItem: () => {
                throw new Error('private browser detail');
            },
            removeItem: () => {},
        });
        broken.getState().add(A, 2);
        expect(broken.getState().items).toEqual([{ productId: A, quantity: 2 }]);
        expect(broken.getState().persistenceUnavailable).toBe(true);
        expect(warning).toHaveBeenCalledTimes(1);
        expect(JSON.stringify(warning.mock.calls)).not.toContain('private browser detail');
        const corrupt = createCartStore(memory('{bad-json'));
        expect(corrupt.getState().items).toEqual([]);
        expect(corrupt.getState().persistenceUnavailable).toBe(true);
    } finally {
        warning.mockRestore();
    }
});
it('bounds persisted parsing/work and sanitizes invalid rows without trusting action overrides', () => {
    const store = createCartStore(memory('x'.repeat(16_385)));
    expect(store.getState().items).toEqual([]);
    const multibyte = createCartStore(memory('€'.repeat(6000)));
    expect(multibyte.getState().items).toEqual([]);
    expect(multibyte.getState().persistenceUnavailable).toBe(false); // Ignore oversized UTF-8 before JSON parsing.
    expect(sanitizeItems(null)).toEqual([]);
    expect(
        sanitizeItems([
            { productId: A, quantity: '2' },
            { productId: B, quantity: 1 },
        ]),
    ).toEqual([{ productId: B, quantity: 1 }]);
    expect(sanitizeItems(Array(101).fill({ productId: A, quantity: 1 }))).toHaveLength(1);
});
it('rehydrates shared-device changes and clears when another tab removes storage', async () => {
    const storage = memory();
    const one = createCartStore(storage);
    const two = createCartStore(storage);
    one.getState().add(A, 3);
    await two.persist.rehydrate();
    expect(two.getState().items).toEqual(one.getState().items);
    storage.removeItem(CART_KEY);
    await two.persist.rehydrate();
    expect(two.getState().items).toEqual([]);
});
it('derives exact repeated-cent and beyond-Number-precision totals from current query prices', () => {
    const products = new Map([
        [A, product()],
        [B, product(B, '0.20')],
    ]);
    const items = [
        { productId: A, quantity: 3 },
        { productId: B, quantity: 1 },
    ];
    expect(estimatedLine(product(), 3)).toBe('0.30');
    expect(estimatedTotal(items, products)).toBe('0.50');
    products.set(A, product(A, '0.15'));
    expect(estimatedTotal(items, products)).toBe('0.65');
    products.set(A, product(A, '90071992547409.91'));
    expect(
        estimatedTotal(
            [
                { productId: A, quantity: 1 },
                { productId: B, quantity: 1 },
            ],
            products,
        ),
    ).toBe('90071992547410.11');
});
it('withholds totals for unknown products or overflow rather than inventing zero/rounded prices', () => {
    expect(() => estimatedTotal([{ productId: A, quantity: 1 }], new Map())).toThrow(
        'no confirmed catalog price',
    );
    expect(() => estimatedLine(product(A, '9999999999999999.99'), 2)).toThrow('supported range');
});
it('explains archive/category visibility and live available quantities without mutating inventory', () => {
    const current = product();
    expect(itemIssue(current, 10)).toBeNull();
    expect(itemIssue(current, 11)).toContain('Only 10');
    expect(itemIssue({ ...current, available: 0 }, 1)).toContain('out of stock');
    expect(itemIssue({ ...current, isActive: false }, 1)).toContain('no longer available');
    expect(itemIssue({ ...current, category: { ...current.category, isActive: false } }, 1)).toContain(
        'no longer available',
    );
    expect(current.available).toBe(10);
});
