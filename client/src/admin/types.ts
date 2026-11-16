import type { Page, OrderStatus } from '../api/types';
export interface Inventory {
    productId: string;
    onHand: number;
    reserved: number;
    available: number;
    revision: number;
}
export interface Movement {
    id: string;
    productId: string;
    operationId: string;
    operation: string;
    kind: string;
    actorKind: string;
    actorUserId: string | null;
    reason: string;
    onHandDelta: number;
    reservedDelta: number;
    onHandBefore: number;
    reservedBefore: number;
    onHandAfter: number;
    reservedAfter: number;
    availableAfter: number;
    revision: number;
    occurredAt: string;
    orderId: string | null;
    reservationId: string | null;
}
export interface AuditPage<T> extends Page<T> {
    observedAt: string;
}
export interface PaymentAudit {
    id: string;
    orderId: string;
    status: string;
    amount: string;
    createdAt: string;
    updatedAt: string;
    needsAttention: boolean;
}
export interface CaptureAudit {
    id: string;
    orderId: string;
    attemptId: string;
    amount: string;
    allocated: boolean;
    recordedAt: string;
    providerOccurredAt: string | null;
}
export interface RefundAudit {
    id: string;
    captureId: string;
    orderId: string;
    status: string;
    amount: string;
    createdAt: string;
    updatedAt: string;
    succeededAt: string | null;
}
export interface SalesAmounts {
    paidSales: string;
    capturedRevenue: string;
    refunds: string;
    netRevenue: string;
    paidOrderCount: number;
}
export interface SalesReport {
    range: { from: string; to: string };
    observedAt: string;
    totals: SalesAmounts;
    days: { date: string; amounts: SalesAmounts }[];
    currency: 'USD';
}
export interface CountsReport {
    observedAt: string;
    counts: { status: OrderStatus; count: number }[];
}
export interface TopReport {
    observedAt: string;
    items: { productId: string; sku: string; name: string; units: number; paidSales: string }[];
}
export interface LowStock {
    productId: string;
    sku: string;
    name: string;
    onHand: number;
    reserved: number;
    available: number;
}
export interface EmailFailure {
    id: string;
    sourceEventId: string;
    orderId: string;
    template: string;
    provider: string;
    providerKey: string;
    status: 'Pending' | 'InFlight' | 'Sent' | 'NeedsReview';
    attemptCount: number;
    attemptsInCycle: number;
    reviewRetryCount: number;
    nextAttemptAt: string;
    lastFailureAt: string | null;
    lastErrorCode: string | null;
    outcomeUncertain: boolean;
    sentAt: string | null;
}
