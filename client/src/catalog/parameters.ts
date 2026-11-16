import type { ProductParameters } from '../api/types';
import { isProductId } from '../api/catalog';

export function catalogParameters(search: URLSearchParams): ProductParameters {
    const integer = (value: string | null, fallback: number) =>
        value && /^[1-9][0-9]*$/.test(value) && Number.isSafeInteger(Number(value))
            ? Number(value)
            : fallback;
    const size = integer(search.get('pageSize'), 12);
    const pageSize = [12, 24, 48].includes(size) ? size : 12;
    const page = integer(search.get('page'), 1);
    const category = search.get('categoryId');
    const sort = search.get('sort');
    return {
        q: (search.get('q') ?? '').trim().slice(0, 100),
        categoryId: category && isProductId(category) ? category.toLowerCase() : null,
        sort: sort === 'name' || sort === 'price-asc' || sort === 'price-desc' ? sort : 'newest',
        page: (page - 1) * pageSize <= 2_147_483_647 ? page : 1,
        pageSize,
    };
}
export function catalogSearch(parameters: ProductParameters): URLSearchParams {
    const search = new URLSearchParams({
        page: String(parameters.page),
        pageSize: String(parameters.pageSize),
        sort: parameters.sort,
    });
    if (parameters.q) search.set('q', parameters.q);
    if (parameters.categoryId) search.set('categoryId', parameters.categoryId);
    return search;
}
