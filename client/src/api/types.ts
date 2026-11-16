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
