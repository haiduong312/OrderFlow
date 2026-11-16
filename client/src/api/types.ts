// Public JSON money stays a string; arithmetic uses bigint cents, never Number.
export type Money = string;
export interface User {
    id: string;
    email: string;
    roles: string[];
}
export interface Credentials {
    email: string;
    password: string;
}
export type OrderStatus =
    'PendingPayment' | 'Confirmed' | 'Processing' | 'Shipped' | 'Completed' | 'Cancelled' | 'Expired';
export interface OrderSummary {
    id: string;
    customerId: string;
    status: OrderStatus;
    total: Money;
    currency: 'USD';
    createdAt: string;
    expiresAt: string;
    version: number;
    expiry: { observedAt: string; effectiveStatus: string; isExpired: boolean; canCancel: boolean };
}
export interface Page<T> {
    items: T[];
    page: number;
    pageSize: number;
    totalCount: number;
}
export interface OrderParameters {
    page: number;
    pageSize: number;
    status?: OrderStatus;
}

export interface OrderItem {
    productId: string;
    sku: string;
    name: string;
    unitPrice: Money;
    quantity: number;
    lineTotal: Money;
}
export interface CreatedOrder {
    id: string;
    status: OrderStatus;
    version: number;
    createdAt: string;
    expiresAt: string;
    total: Money;
    currency: 'USD';
    items: OrderItem[];
}
export interface OrderDetail extends OrderSummary {
    items: OrderItem[];
    history: {
        version: number;
        fromStatus: OrderStatus | null;
        status: OrderStatus;
        actorKind: string;
        actorUserId: string | null;
        occurredAt: string;
        reason: string;
    }[];
}
export interface PaymentAttempt {
    id: string;
    orderId: string;
    status: 'Queued' | 'Pending' | 'Unknown' | 'Failed' | 'Captured';
    amount: Money;
    currency: 'USD';
    createdAt: string;
    updatedAt: string;
    observedProviderState: 'Pending' | 'Succeeded' | 'Failed' | null;
    needsAttention: boolean;
    lastErrorCode: string | null;
}
export interface PaymentStatus {
    orderId: string;
    observedAt: string;
    page: number;
    pageSize: number;
    attempts: PaymentAttempt[];
    attemptCount: number;
    captures: {
        id: string;
        attemptId: string;
        amount: Money;
        currency: 'USD';
        allocated: boolean;
        recordedAt: string;
    }[];
    captureCount: number;
    refunds: {
        id: string;
        captureId: string;
        amount: Money;
        currency: 'USD';
        status: 'Pending' | 'Succeeded' | 'NeedsReview';
        createdAt: string;
        updatedAt: string;
        succeededAt: string | null;
    }[];
    refundCount: number;
}
export interface CustomerNotification {
    id: string;
    sourceEventId: string;
    type: string;
    orderId: string;
    createdAt: string;
    readAt: string | null;
}

export interface Category {
    id: string;
    name: string;
    isActive: boolean;
    version: number;
}
export interface Product {
    id: string;
    sku: string;
    name: string;
    description: string;
    price: Money;
    currency: 'USD';
    category: Omit<Category, 'version'>;
    isActive: boolean;
    version: number;
    available: number;
}
export interface CategoryParameters {
    q: string;
    page: number;
    pageSize: number;
    sort: 'newest' | 'name';
}
export interface ProductParameters extends Omit<CategoryParameters, 'sort'> {
    categoryId: string | null;
    sort: 'newest' | 'name' | 'price-asc' | 'price-desc';
}

export function validateUser(value: User): User {
    if (
        !value ||
        typeof value.id !== 'string' ||
        !value.id ||
        typeof value.email !== 'string' ||
        !Array.isArray(value.roles) ||
        !value.roles.every((role) => typeof role === 'string')
    ) {
        throw new Error('The API returned an invalid current-user response.');
    }
    return value;
}
