import { QueryClient, queryOptions } from '@tanstack/react-query';
import type { ApiClient } from './client';
import { ApiError } from './client';
import type { OrderParameters, OrderSummary, Page, User } from './types';
import { validateUser } from './types';

export const keys = {
    // Identity is unknown at bootstrap. This private singleton is only consumed by SessionController.
    session: ['private', 'session'] as const,
    health: ['public', 'health', 'live'] as const,
    orders: (userId: string, parameters: OrderParameters) =>
        [
            'private',
            userId,
            'orders',
            {
                page: parameters.page,
                pageSize: parameters.pageSize,
                status: parameters.status ?? null,
            },
        ] as const,
};
export const isPrivate = (query: { queryKey: readonly unknown[] }) => query.queryKey[0] === 'private';

export function createQueryClient() {
    return new QueryClient({
        defaultOptions: {
            queries: { retry: false, staleTime: 30_000, gcTime: 5 * 60_000, refetchOnWindowFocus: false },
            mutations: { retry: false },
        },
    });
}

export const sessionOptions = (api: ApiClient) =>
    queryOptions({
        queryKey: keys.session,
        staleTime: 0,
        gcTime: 0,
        queryFn: async ({ signal }): Promise<User | null> => {
            try {
                return validateUser(await api.request<User>('/api/auth/me', { signal }));
            } catch (error) {
                if (error instanceof ApiError && error.status === 401) return null;
                throw error; // A DB/network/configuration failure is not an anonymous session.
            }
        },
    });
export const healthOptions = (api: ApiClient) =>
    queryOptions({
        queryKey: keys.health,
        queryFn: ({ signal }) => api.request<{ status: string }>('/api/health/live', { signal }),
    });

export interface PrivateScope {
    guard<T>(userId: string, action: () => Promise<T>): Promise<T>;
    changed(): void;
}
export const ordersOptions = (
    api: ApiClient,
    scope: PrivateScope,
    userId: string,
    parameters: OrderParameters,
) =>
    queryOptions({
        queryKey: keys.orders(userId, parameters),
        queryFn: ({ signal }) =>
            scope.guard(userId, async () => {
                const query = new URLSearchParams({
                    page: String(parameters.page),
                    pageSize: String(parameters.pageSize),
                });
                if (parameters.status) query.set('status', parameters.status);
                const page = await api.request<Page<OrderSummary>>(`/api/orders?${query}`, {
                    signal,
                });
                if (page.items.some((item) => item.customerId.toLowerCase() !== userId.toLowerCase())) {
                    scope.changed();
                    throw new DOMException('The session changed while reading orders.', 'AbortError');
                }
                return page;
            }),
    });
