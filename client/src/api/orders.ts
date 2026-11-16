import { queryOptions } from '@tanstack/react-query';
import type { ApiClient } from './client';
import type { PrivateScope } from './queries';
import type { CustomerNotification, OrderDetail, Page, PaymentStatus } from './types';

export const orderKeys = {
    detail: (userId: string, id: string) => ['private', userId, 'order', id] as const,
    payments: (userId: string, id: string, page: number, pageSize: number) =>
        ['private', userId, 'payments', id, { page, pageSize }] as const,
    notifications: (userId: string, page: number, pageSize: number, unreadOnly: boolean) =>
        ['private', userId, 'notifications', { page, pageSize, unreadOnly }] as const,
    unread: (userId: string) => ['private', userId, 'unread'] as const,
};
export const orderOptions = (api: ApiClient, scope: PrivateScope, userId: string, id: string) =>
    queryOptions({
        queryKey: orderKeys.detail(userId, id),
        queryFn: ({ signal }) =>
            scope.guard(userId, async () => {
                const order = await api.request<OrderDetail>(`/api/orders/${id}`, { signal });
                if (
                    order.customerId.toLowerCase() !== userId.toLowerCase() ||
                    order.id.toLowerCase() !== id
                ) {
                    scope.changed();
                    throw new DOMException('Identity changed.', 'AbortError');
                }
                return order;
            }),
        staleTime: 0,
        refetchInterval: 15_000, // Durable catch-up also works when realtime is disabled or hints are lost.
    });
export const paymentsOptions = (
    api: ApiClient,
    scope: PrivateScope,
    userId: string,
    id: string,
    page = 1,
    pageSize = 20,
) =>
    queryOptions({
        queryKey: orderKeys.payments(userId, id, page, pageSize),
        queryFn: ({ signal }) =>
            scope.guard(userId, async () => {
                const result = await api.request<PaymentStatus>(
                    `/api/orders/${id}/payment-status?page=${page}&pageSize=${pageSize}`,
                    { signal },
                );
                if (result.orderId !== id) throw new Error('Payment order mismatch.');
                return result;
            }),
        staleTime: 0,
        refetchInterval: 15_000,
    });
export const notificationOptions = (
    api: ApiClient,
    scope: PrivateScope,
    userId: string,
    page: number,
    pageSize: number,
    unreadOnly: boolean,
) =>
    queryOptions({
        queryKey: orderKeys.notifications(userId, page, pageSize, unreadOnly),
        queryFn: ({ signal }) =>
            scope.guard(userId, () =>
                api.request<Page<CustomerNotification>>(
                    `/api/notifications?page=${page}&pageSize=${pageSize}&unreadOnly=${unreadOnly}`,
                    { signal },
                ),
            ),
        staleTime: 0,
        refetchInterval: 30_000,
    });
export const unreadOptions = (api: ApiClient, scope: PrivateScope, userId: string) =>
    queryOptions({
        queryKey: orderKeys.unread(userId),
        queryFn: ({ signal }) =>
            scope.guard(userId, () =>
                api.request<{ count: number }>('/api/notifications/unread-count', { signal }),
            ),
        refetchInterval: 30_000,
    });
