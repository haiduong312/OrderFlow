import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { PersistStorage } from 'zustand/middleware';
import { isProductId } from '../api/catalog';

export const CART_KEY = 'orderflow-cart';
export const CART_VERSION = 1;
export const MAX_LINES = 100;
export const MAX_QUANTITY = 1000;
export interface CartItem {
    productId: string;
    quantity: number;
}
interface SavedCart {
    items: CartItem[];
}
interface Cart extends SavedCart {
    persistenceUnavailable: boolean;
    add: (productId: string, quantity: number) => void;
    update: (productId: string, quantity: number) => void;
    remove: (productId: string) => void;
    clear: () => void;
}
export function validQuantity(quantity: number) {
    return Number.isSafeInteger(quantity) && quantity >= 1 && quantity <= MAX_QUANTITY;
}
export function parseQuantity(value: string): number {
    if (!/^[1-9][0-9]{0,3}$/.test(value) || !validQuantity(Number(value)))
        throw new Error(`Enter a whole quantity from 1 to ${MAX_QUANTITY}.`);
    return Number(value);
}
export function sanitizeItems(value: unknown): CartItem[] {
    if (!Array.isArray(value)) return [];
    const items: CartItem[] = [];
    const seen = new Set<string>();
    // Bound work as well as the accepted output. Corrupt/hostile persisted data is untrusted input.
    for (const row of value.slice(0, MAX_LINES)) {
        if (
            !row ||
            typeof row.productId !== 'string' ||
            !isProductId(row.productId) ||
            !validQuantity(row.quantity)
        )
            continue;
        const productId = row.productId.toLowerCase();
        if (seen.has(productId)) continue; // First valid occurrence wins; never silently sum duplicates.
        seen.add(productId);
        items.push({ productId, quantity: row.quantity });
    }
    return items;
}
export function migrateCart(_value: unknown, _version: number): SavedCart {
    // v1 is the first released shape. Unknown/older/future formats have no supported conversion.
    return { items: [] };
}

export function createCartStore(storage?: Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>) {
    let failed = false;
    let notifyFailure = () => {};
    const failure = () => {
        if (!failed) console.warn('OrderFlow cart persistence unavailable; continuing in memory.');
        failed = true;
        notifyFailure(); // Never log storage values, product selections or exception text.
    };
    const backing = () => storage ?? (typeof window === 'undefined' ? null : window.localStorage);
    const persistence: PersistStorage<SavedCart> = {
        getItem: (name) => {
            try {
                const raw = backing()?.getItem(name);
                if (typeof raw !== 'string' || raw.length > 16_384) return null;
                if (new TextEncoder().encode(raw).byteLength > 16_384) return null;
                const value = JSON.parse(raw) as { state?: SavedCart; version?: number };
                if (!value || !Number.isSafeInteger(value.version)) return null;
                return { state: { items: sanitizeItems(value.state?.items) }, version: value.version };
            } catch {
                failure();
                return null;
            }
        },
        setItem: (name, value) => {
            try {
                backing()?.setItem(name, JSON.stringify(value));
            } catch {
                failure();
            }
        },
        removeItem: (name) => {
            try {
                backing()?.removeItem(name);
            } catch {
                failure();
            }
        },
    };
    const store = create<Cart>()(
        persist(
            (set, get) => ({
                items: [],
                persistenceUnavailable: false,
                add: (id, quantity) => {
                    if (!isProductId(id) || !validQuantity(quantity))
                        throw new Error('Invalid cart item or quantity.');
                    const productId = id.toLowerCase();
                    const existing = get().items.find((item) => item.productId === productId);
                    if (existing) {
                        const total = existing.quantity + quantity;
                        if (!validQuantity(total))
                            throw new Error(`An item can have at most ${MAX_QUANTITY} units.`);
                        get().update(productId, total);
                    } else {
                        if (get().items.length >= MAX_LINES)
                            throw new Error(`A cart can contain at most ${MAX_LINES} different products.`);
                        set((state) => ({ items: [...state.items, { productId, quantity }] }));
                    }
                },
                update: (id, quantity) => {
                    if (!isProductId(id) || !validQuantity(quantity))
                        throw new Error('Invalid cart item or quantity.');
                    set((state) => ({
                        items: state.items.map((item) =>
                            item.productId === id.toLowerCase() ? { ...item, quantity } : item,
                        ),
                    }));
                },
                remove: (id) =>
                    set((state) => ({
                        items: state.items.filter((item) => item.productId !== id.toLowerCase()),
                    })),
                clear: () => set({ items: [] }),
            }),
            {
                name: CART_KEY,
                version: CART_VERSION,
                storage: persistence,
                partialize: (state) => ({
                    items: state.items.map(({ productId, quantity }) => ({ productId, quantity })),
                }),
                merge: (stored, current) => ({
                    ...current,
                    items: sanitizeItems((stored as SavedCart | null)?.items),
                }),
                migrate: migrateCart,
            },
        ),
    );
    // setState itself persists; report an error without recursively retrying failed storage.
    notifyFailure = () => {
        if (!store.getState().persistenceUnavailable) store.setState({ persistenceUnavailable: true });
    };
    if (failed) notifyFailure();
    return store;
}
export const useCart = createCartStore();
