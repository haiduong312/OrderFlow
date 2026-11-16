import { expect, it } from 'vitest';
import { ApiClient } from '../src/api/client';
import { catalogKeys, categoriesOptions, productOptions, productsOptions } from '../src/api/catalog';
import { catalogParameters, catalogSearch } from '../src/catalog/parameters';
import { createQueryClient } from '../src/api/queries';
import type { ProductParameters } from '../src/api/types';

const id = '11111111-1111-4111-8111-111111111111';
const base: ProductParameters = { q: '', categoryId: null, page: 1, pageSize: 12, sort: 'newest' };
const json = (value: unknown) =>
    new Response(JSON.stringify(value), { headers: { 'Content-Type': 'application/json' } });
it('keys include every product server filter/page/sort and category parameter', () => {
    for (const patch of [
        { q: 'desk' },
        { categoryId: id },
        { page: 2 },
        { pageSize: 24 },
        { sort: 'name' as const },
    ])
        expect(catalogKeys.products({ ...base, ...patch })).not.toEqual(catalogKeys.products(base));
    const category = { q: '', page: 1, pageSize: 20, sort: 'name' as const };
    for (const patch of [{ q: 'travel' }, { page: 2 }, { pageSize: 100 }, { sort: 'newest' as const }])
        expect(catalogKeys.categories({ ...category, ...patch })).not.toEqual(
            catalogKeys.categories(category),
        );
    expect(catalogKeys.product(id.toUpperCase())).toEqual(catalogKeys.product(id));
    expect(catalogKeys.product(id)).not.toEqual(catalogKeys.product('22222222-2222-4222-8222-222222222222'));
});
it('forwards every parameter and AbortSignal through real QueryClient query execution', async () => {
    const queries = createQueryClient();
    const calls: { path: string; signal: AbortSignal | null | undefined }[] = [];
    const api = new ApiClient(async (path, options) => {
        calls.push({ path: String(path), signal: options?.signal });
        return json({ items: [], totalCount: 0, page: 2, pageSize: 24 });
    });
    const parameters = {
        ...base,
        q: 'desk & pen',
        categoryId: id,
        page: 2,
        pageSize: 24,
        sort: 'price-asc' as const,
    };
    await queries.fetchQuery(productsOptions(api, parameters));
    const url = new URL(calls[0]!.path, 'http://test.invalid');
    expect(Object.fromEntries(url.searchParams)).toEqual({
        q: 'desk & pen',
        categoryId: id,
        page: '2',
        pageSize: '24',
        sort: 'price-asc',
    });
    expect(calls[0]!.signal).toBeInstanceOf(AbortSignal);
    await queries.fetchQuery(categoriesOptions(api, { q: 'paper', page: 3, pageSize: 20, sort: 'name' }));
    expect(calls[1]!.path).toContain('/api/categories?q=paper&page=3&pageSize=20&sort=name');
    expect(productsOptions(api, base)).not.toHaveProperty('placeholderData');
    queries.clear();
});
it('aborts an obsolete actual fetch when its query is cancelled', async () => {
    let reached!: () => void;
    const arrived = new Promise<void>((yes) => {
        reached = yes;
    });
    let aborted = false;
    const api = new ApiClient(
        async (_path, options) =>
            new Promise<Response>((_yes, no) => {
                options!.signal!.addEventListener('abort', () => {
                    aborted = true;
                    no(new DOMException('cancelled', 'AbortError'));
                });
                reached();
            }),
    );
    const queries = createQueryClient();
    const pending = queries.fetchQuery(productsOptions(api, base));
    const rejected = expect(pending).rejects.toBeDefined();
    await arrived;
    await queries.cancelQueries({ queryKey: catalogKeys.products(base), exact: true });
    await rejected;
    expect(aborted).toBe(true);
    queries.clear();
});
it('detail revalidation rejects mismatched/invalid products rather than caching false cart prices', async () => {
    const queries = createQueryClient();
    const api = new ApiClient(async () => json({ id, name: 'Invalid product', price: 0.1 }));
    await expect(queries.fetchQuery(productOptions(api, id))).rejects.toThrow('invalid product');
    expect(queries.getQueryData(catalogKeys.product(id))).toBeUndefined();
    queries.clear();
});
it('normalizes bounded URL values and roundtrips valid parameters including literal keywords', () => {
    const parameters = {
        ...base,
        q: 'A%_ & pen',
        categoryId: id,
        page: 3,
        pageSize: 24,
        sort: 'price-desc' as const,
    };
    expect(catalogParameters(catalogSearch(parameters))).toEqual(parameters);
    expect(
        catalogParameters(new URLSearchParams('page=-1&pageSize=999&sort=injected&categoryId=bad')),
    ).toEqual(base);
    expect(catalogParameters(new URLSearchParams('page=9007199254740991'))).toEqual(base);
    expect(catalogParameters(new URLSearchParams({ q: 'x'.repeat(101) })).q).toHaveLength(100);
});
