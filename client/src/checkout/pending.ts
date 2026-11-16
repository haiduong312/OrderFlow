import { isProductId } from '../api/catalog';
import type { CartItem } from '../state/cart';
import { MAX_LINES, validQuantity } from '../state/cart';

export interface CheckoutIntention {
    key: string;
    body: string;
    orderId?: string;
}
export interface PaymentIntention {
    orderId: string;
    key: string;
    attemptId?: string;
}
export interface PendingState {
    checkout: CheckoutIntention | null;
    payments: PaymentIntention[];
    error?: string;
}
type Storage = Pick<globalThis.Storage, 'getItem' | 'setItem' | 'removeItem'>;
type Lock = <T>(action: () => Promise<T>) => Promise<T>;
const empty = (): PendingState => ({ checkout: null, payments: [] });
export class RecoveryError extends Error {
    constructor(message: string) {
        super(message);
        this.name = 'RecoveryError';
    }
}

export function checkoutBody(items: readonly CartItem[]): string {
    if (!items.length || items.length > MAX_LINES) throw new Error('Choose 1–100 distinct products.');
    const seen = new Set<string>();
    const normalized = items
        .map((item) => {
            if (!isProductId(item.productId) || !validQuantity(item.quantity))
                throw new Error('Invalid checkout item.');
            const productId = item.productId.toLowerCase();
            if (seen.has(productId)) throw new Error('Duplicate checkout product.');
            seen.add(productId);
            return { productId, quantity: item.quantity };
        })
        .sort((a, b) => (a.productId < b.productId ? -1 : a.productId > b.productId ? 1 : 0));
    return JSON.stringify({ items: normalized });
}
export function pendingKey(userId: string) {
    return `orderflow-operations:v1:${userId.toLowerCase()}`;
}
function validate(value: unknown): PendingState {
    const state = value as {
        version: number;
        checkout: CheckoutIntention | null;
        payments: PaymentIntention[];
    };
    if (
        !state ||
        state.version !== 1 ||
        !Array.isArray(state.payments) ||
        state.payments.length > 20 ||
        !(state.checkout === null || (typeof state.checkout === 'object' && state.checkout))
    )
        throw new Error('Invalid recovery format.');
    let checkout: CheckoutIntention | null = null;
    if (state.checkout) {
        const row = state.checkout;
        if (
            !isProductId(row.key) ||
            typeof row.body !== 'string' ||
            checkoutBody((JSON.parse(row.body) as { items: CartItem[] }).items) !== row.body ||
            (row.orderId !== undefined && !isProductId(row.orderId))
        )
            throw new Error('Invalid checkout recovery.');
        checkout = { key: row.key, body: row.body, ...(row.orderId ? { orderId: row.orderId } : {}) };
    }
    const seen = new Set<string>();
    const payments = state.payments.map((row) => {
        if (
            !row ||
            !isProductId(row.orderId) ||
            !isProductId(row.key) ||
            (row.attemptId !== undefined && !isProductId(row.attemptId)) ||
            seen.has(row.orderId)
        )
            throw new Error('Invalid payment recovery.');
        seen.add(row.orderId);
        return { orderId: row.orderId, key: row.key, ...(row.attemptId ? { attemptId: row.attemptId } : {}) };
    });
    return { checkout, payments };
}

// This is durable operation recovery, not authorization or a cache of private server DTOs.
export class PendingOperations {
    private state: PendingState = empty();
    private listeners = new Set<() => void>();
    readonly storageKey: string;
    constructor(
        readonly userId: string,
        private readonly storage: Storage = {
            getItem: (key) => window.localStorage.getItem(key),
            setItem: (key, value) => window.localStorage.setItem(key, value),
            removeItem: (key) => window.localStorage.removeItem(key),
        },
        private readonly lock: Lock = (action) => {
            if (!navigator.locks)
                throw new RecoveryError(
                    'Safe checkout recovery requires browser Web Locks. Use a supported browser.',
                );
            return navigator.locks.request(pendingKey(userId), action);
        },
        private readonly uuid: () => string = () => crypto.randomUUID(),
    ) {
        if (!isProductId(userId)) throw new Error('Invalid user scope.');
        this.storageKey = pendingKey(userId);
        this.reload();
    }
    getSnapshot = () => this.state;
    subscribe = (listener: () => void) => {
        this.listeners.add(listener);
        return () => {
            this.listeners.delete(listener);
        };
    };
    private publish(state: PendingState) {
        this.state = state;
        this.listeners.forEach((listener) => listener());
    }
    reload = () => {
        try {
            const raw = this.storage.getItem(this.storageKey);
            if (!raw && (this.state.checkout || this.state.payments.some((row) => !row.attemptId)))
                throw new RecoveryError('An unresolved recovery record disappeared.');
            if (raw && (raw.length > 24_576 || new TextEncoder().encode(raw).byteLength > 24_576))
                throw new Error('Oversized recovery.');
            this.publish(raw ? validate(JSON.parse(raw)) : empty());
        } catch {
            this.publish({
                ...this.state,
                error: 'Recovery data is unavailable or invalid. No new request will be sent. Review your orders before explicitly resetting this record.',
            });
        }
    };
    private save(state: PendingState) {
        // Synchronous durable write must succeed BEFORE any POST. Do not fallback to memory here.
        try {
            this.storage.setItem(
                this.storageKey,
                JSON.stringify({ version: 1, checkout: state.checkout, payments: state.payments }),
            );
        } catch {
            throw new RecoveryError(
                'Recovery could not be saved. Do not start a different intention. Check browser storage permissions, then recover the original request or review your orders.',
            );
        }
        this.publish(state);
    }
    private async run<T>(action: () => Promise<T>): Promise<T> {
        return this.lock(async () => {
            this.reload();
            if (this.state.error) throw new RecoveryError(this.state.error);
            return action();
        });
    }
    checkout(
        items: readonly CartItem[],
        send: (record: CheckoutIntention) => Promise<{ id: string }>,
    ): Promise<CheckoutIntention> {
        return this.run(async () => {
            let record = this.state.checkout;
            if (!record) {
                record = { key: this.uuid(), body: checkoutBody(items) };
                this.save({ ...this.state, checkout: record });
            }
            if (record.orderId) return record;
            const response = await send(record); // Network/5xx/abort failure leaves the exact original key/body untouched.
            if (!isProductId(response.id))
                throw new Error('Invalid order acknowledgement. Replay the original request.');
            record = { ...record, orderId: response.id };
            this.save({ ...this.state, checkout: record });
            return record;
        });
    }
    payment(
        orderId: string,
        send: (record: PaymentIntention) => Promise<{ id: string; orderId: string }>,
        replaceFailed = false,
    ): Promise<PaymentIntention> {
        return this.run(async () => {
            if (!isProductId(orderId)) throw new Error('Invalid order.');
            let record = this.state.payments.find((row) => row.orderId === orderId);
            if (replaceFailed && record && !record.attemptId)
                throw new Error('Resolve the original payment request first.');
            if (!record || replaceFailed) {
                const others = this.state.payments.filter((row) => row.orderId !== orderId);
                if (others.length >= 20) {
                    const resolved = others.findIndex((row) => !!row.attemptId);
                    if (resolved < 0)
                        throw new Error('Resolve another pending payment before starting a new one.');
                    others.splice(resolved, 1);
                }
                record = { orderId, key: this.uuid() };
                this.save({ ...this.state, payments: [...others, record] });
            }
            if (record.attemptId) return record;
            const response = await send(record);
            if (response.orderId !== orderId || !isProductId(response.id))
                throw new Error('Invalid payment acknowledgement. Replay the same request.');
            record = { ...record, attemptId: response.id };
            this.save({
                ...this.state,
                payments: this.state.payments.map((row) => (row.orderId === orderId ? record! : row)),
            });
            return record;
        });
    }
    closeCheckout(key: string, requireResolved: boolean) {
        return this.run(async () => {
            if (this.state.checkout?.key !== key)
                throw new Error('Checkout changed in another tab. Refresh and review it.');
            if (requireResolved && !this.state.checkout.orderId) throw new Error('Resolve checkout first.');
            this.save({ ...this.state, checkout: null });
        });
    }
    closePayment(orderId: string, key: string) {
        return this.run(async () => {
            if (!this.state.payments.some((row) => row.orderId === orderId && row.key === key))
                throw new RecoveryError('The payment recovery record changed. Refresh first.');
            this.save({
                ...this.state,
                payments: this.state.payments.filter((row) => row.orderId !== orderId),
            });
        });
    }
    resetInvalid() {
        return this.lock(async () => {
            this.save(empty()); // Write an explicit reset so another tab can distinguish it from lost storage.
        });
    }
}
