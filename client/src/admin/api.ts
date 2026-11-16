import { queryOptions, useMutation, useQuery } from '@tanstack/react-query';
import type { QueryClient } from '@tanstack/react-query';
import { useSession } from '../auth/context';
import type { ApiClient } from '../api/client';
import type { PrivateScope } from '../api/queries';
export type Parameters = Record<string, string | number | boolean | null | undefined>;
export const adminKey = (userId: string, resource: string, parameters: Parameters = {}) =>
    ['private', userId, 'admin', resource, { ...parameters }] as const;
export function adminOptions<T>(
    api: ApiClient,
    scope: PrivateScope,
    userId: string,
    resource: string,
    parameters: Parameters = {},
) {
    return queryOptions({
        queryKey: adminKey(userId, resource, parameters),
        staleTime: 0,
        queryFn: ({ signal }) =>
            scope.guard(userId, () => {
                const query = new URLSearchParams();
                Object.entries(parameters).forEach(([key, value]) => {
                    if (value !== undefined && value !== null && value !== '') query.set(key, String(value));
                });
                return api.request<T>(`/api/admin/${resource}${query.size ? `?${query}` : ''}`, { signal });
            }),
    });
}
export function useAdminQuery<T>(resource: string, parameters: Parameters = {}, enabled = true) {
    const { controller, session } = useSession();
    const userId = session.phase === 'authenticated' ? session.user.id : '';
    return useQuery({
        ...adminOptions<T>(controller.api, controller, userId, resource, parameters),
        enabled,
    });
}
export function affectedResources(resource: string): { admin: string[]; public: string[] } {
    const id = resource.split('/')[1];
    if (resource === 'categories' || resource.startsWith('categories/'))
        return {
            admin: [
                'categories',
                ...(id ? [`categories/${id}`] : []),
                'products',
                '@product-details',
                'reports/low-stock',
            ],
            public: ['categories', ...(id ? [`category:${id}`] : []), 'products', 'product'],
        };
    if (/^products\/.+\/stock-/.test(resource))
        return {
            admin: [
                `products/${id}/inventory`,
                `products/${id}/stock-history`,
                'products',
                `products/${id}`,
                'audit/stock-movements',
                'reports/low-stock',
            ],
            public: ['products', `product:${id}`],
        };
    if (resource === 'products' || resource.startsWith('products/'))
        return {
            admin: ['products', ...(id ? [`products/${id}`] : []), 'reports/low-stock'],
            public: ['products', ...(id ? [`product:${id}`] : [])],
        };
    if (resource.startsWith('orders/'))
        return {
            admin: ['orders', `orders/${id}`, `audit/orders/${id}/history`, 'reports/order-counts'],
            public: [],
        };
    return { admin: ['email-deliveries'], public: [] };
}
export async function invalidateAdmin(client: QueryClient, userId: string, resource: string) {
    const affected = affectedResources(resource);
    await client.invalidateQueries({
        predicate: (query) => {
            const key = query.queryKey;
            if (key[0] === 'public')
                return affected.public.some((path) =>
                    path.includes(':')
                        ? key[1] === path.split(':')[0] && key[2] === path.split(':')[1]
                        : key[1] === path,
                );
            if (key[0] !== 'private' || key[1] !== userId || key[2] !== 'admin') return false;
            return affected.admin.some(
                (path) =>
                    key[3] === path ||
                    (path === '@product-details' && /^products\/[^/]+$/.test(String(key[3]))),
            );
        },
    });
}
export function useAdminMutation<T, V>(
    execute: (variables: V) => Promise<T>,
    resource: (variables: V) => string,
    feedback?: { start: () => void; failure: (error: unknown) => void; success: (data: T) => void },
) {
    const { controller, session } = useSession();
    const userId = session.phase === 'authenticated' ? session.user.id : '';
    return useMutation({
        mutationKey: ['private', userId, 'admin-write'],
        gcTime: 0,
        retry: false,
        onMutate: () => feedback?.start(),
        onError: (error) => feedback?.failure(error),
        onSuccess: (data) => feedback?.success(data),
        mutationFn: (variables: V) => controller.guard(userId, () => execute(variables)),
        onSettled: (_data, _error, variables) =>
            invalidateAdmin(controller.queries, userId, resource(variables)),
    });
}
