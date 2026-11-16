import { queryOptions } from '@tanstack/react-query';
import type { ApiClient } from './client';
import type { Category, CategoryParameters, Page, Product, ProductParameters } from './types';
import { toCents } from './money';

export const catalogKeys = {
    products: (parameters: ProductParameters) => ['public', 'products', { ...parameters }] as const,
    product: (id: string) => ['public', 'product', id.toLowerCase()] as const,
    categories: (parameters: CategoryParameters) => ['public', 'categories', { ...parameters }] as const,
};

export const isProductId = (id: string) =>
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) &&
    id !== '00000000-0000-0000-0000-000000000000';

export function validateProduct(product: Product): Product {
    if (
        !product ||
        !isProductId(product.id) ||
        typeof product.name !== 'string' ||
        typeof product.sku !== 'string' ||
        typeof product.description !== 'string' ||
        product.currency !== 'USD' ||
        typeof product.price !== 'string' ||
        toCents(product.price) <= 0n ||
        !Number.isInteger(product.available) ||
        product.available < 0 ||
        !Number.isInteger(product.version) ||
        product.version < 1 ||
        typeof product.isActive !== 'boolean' ||
        !product.category ||
        !isProductId(product.category.id) ||
        typeof product.category.name !== 'string' ||
        typeof product.category.isActive !== 'boolean'
    )
        throw new Error('The API returned an invalid product.');
    return product;
}

export const productsOptions = (api: ApiClient, parameters: ProductParameters) =>
    queryOptions({
        queryKey: catalogKeys.products(parameters),
        queryFn: async ({ signal }) => {
            const query = new URLSearchParams({
                q: parameters.q,
                page: String(parameters.page),
                pageSize: String(parameters.pageSize),
                sort: parameters.sort,
            });
            if (parameters.categoryId) query.set('categoryId', parameters.categoryId);
            const page = await api.request<Page<Product>>(`/api/products?${query}`, { signal });
            page.items.forEach(validateProduct);
            return page;
        },
        // Deliberately no previous-filter/page placeholder: a new key shows a loading state.
    });

export const productOptions = (api: ApiClient, id: string) =>
    queryOptions({
        queryKey: catalogKeys.product(id),
        queryFn: async ({ signal }) => {
            const product = validateProduct(await api.request<Product>(`/api/products/${id}`, { signal }));
            if (product.id.toLowerCase() !== id.toLowerCase()) throw new Error('Product identity mismatch.');
            return product;
        },
        staleTime: 0,
        refetchOnMount: 'always' as const,
        refetchOnWindowFocus: true,
    });

export const categoriesOptions = (api: ApiClient, parameters: CategoryParameters) =>
    queryOptions({
        queryKey: catalogKeys.categories(parameters),
        queryFn: ({ signal }) => {
            const query = new URLSearchParams({
                q: parameters.q,
                page: String(parameters.page),
                pageSize: String(parameters.pageSize),
                sort: parameters.sort,
            });
            return api.request<Page<Category>>(`/api/categories?${query}`, { signal });
        },
    });
