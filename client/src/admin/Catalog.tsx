import { useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import type { Category, Page, Product } from '../api/types';
import { isProductId } from '../api/catalog';
import { formatUsd, toCents } from '../api/money';
import { useAdminQuery } from './api';
import {
    Field,
    FilterForm,
    Paging,
    QueryPanel,
    Table,
    fieldError,
    pageNumber,
    text,
    useAdminRecovery,
    useCommand,
} from './shared';

function CategoryPicker({
    value,
    change,
    currentName,
}: {
    value: string;
    change: (id: string) => void;
    currentName?: string;
}) {
    const [q, setQ] = useState('');
    const [page, setPage] = useState(1);
    const categories = useAdminQuery<Page<Category>>('categories', { q, page, pageSize: 20, sort: 'name' });
    return (
        <div className="admin-category-picker">
            <Field id="category-search" label="Find category">
                <input
                    id="category-search"
                    type="search"
                    onKeyDown={(event) => {
                        if (event.key === 'Enter') event.preventDefault();
                    }}
                    maxLength={100}
                    value={q}
                    onChange={(event) => {
                        setQ(event.target.value);
                        setPage(1);
                    }}
                />
            </Field>
            <QueryPanel query={categories}>
                {(result) => (
                    <>
                        <Field id="product-category" label="Active category">
                            <select
                                id="product-category"
                                value={value}
                                required
                                onChange={(event) => change(event.target.value)}
                            >
                                <option value="">Choose an active category</option>
                                {value && !result.items.some((row) => row.id === value) && (
                                    <option value={value}>
                                        {currentName ?? value} (selected from another page)
                                    </option>
                                )}
                                {result.items.map((row) => (
                                    <option key={row.id} value={row.id} disabled={!row.isActive}>
                                        {row.name}
                                        {row.isActive ? '' : ' (archived)'}
                                    </option>
                                ))}
                            </select>
                        </Field>
                        <Paging value={result} change={setPage} />
                    </>
                )}
            </QueryPanel>
        </div>
    );
}
export function AdminProducts() {
    const [search, setSearch] = useSearchParams();
    const page = pageNumber(search);
    const parameters = {
        q: search.get('q') ?? '',
        categoryId: search.get('categoryId') ?? '',
        sort: search.get('sort') ?? 'newest',
        page,
        pageSize: 20,
    };
    const query = useAdminQuery<Page<Product>>('products', parameters);
    return (
        <>
            <div className="section-heading">
                <h2>Products</h2>
                <Link className="button" to="/admin/products/new">
                    Create product
                </Link>
            </div>
            <FilterForm
                key={search.toString()}
                submit={(data) =>
                    setSearch({
                        q: text(data, 'q'),
                        categoryId: text(data, 'categoryId'),
                        sort: text(data, 'sort'),
                        page: '1',
                    })
                }
            >
                <Field id="admin-product-search" label="Keyword (name or SKU)">
                    <input id="admin-product-search" name="q" maxLength={100} defaultValue={parameters.q} />
                </Field>
                <Field id="admin-category-filter" label="Category UUID (optional)">
                    <input
                        id="admin-category-filter"
                        name="categoryId"
                        defaultValue={parameters.categoryId}
                    />
                </Field>
                <Field id="admin-product-sort" label="Product sort">
                    <select id="admin-product-sort" name="sort" defaultValue={parameters.sort}>
                        <option value="newest">Newest</option>
                        <option value="name">Name</option>
                        <option value="price-asc">Price ascending</option>
                        <option value="price-desc">Price descending</option>
                    </select>
                </Field>
            </FilterForm>
            <section className="card">
                <QueryPanel query={query}>
                    {(result) => (
                        <>
                            <Table
                                rows={result.items}
                                id={(row) => row.id}
                                caption="Admin products, including archived"
                                columns={[
                                    {
                                        label: 'Product',
                                        cell: (row) => (
                                            <Link className="text-link" to={`/admin/products/${row.id}`}>
                                                {row.name}
                                                <small>{row.sku}</small>
                                            </Link>
                                        ),
                                    },
                                    { label: 'Category', cell: (row) => row.category.name },
                                    { label: 'Price USD', cell: (row) => formatUsd(row.price) },
                                    {
                                        label: 'Visibility',
                                        cell: (row) =>
                                            row.isActive
                                                ? row.category.isActive
                                                    ? 'Active'
                                                    : 'Category archived'
                                                : 'Archived',
                                    },
                                    { label: 'Available', cell: (row) => row.available },
                                    { label: 'Version', cell: (row) => row.version },
                                    {
                                        label: 'Stock',
                                        cell: (row) => (
                                            <Link
                                                className="text-link"
                                                to={`/admin/products/${row.id}/stock`}
                                            >
                                                Manage stock
                                            </Link>
                                        ),
                                    },
                                ]}
                            />
                            <Paging
                                value={result}
                                change={(next) => {
                                    const value = new URLSearchParams(search);
                                    value.set('page', String(next));
                                    setSearch(value);
                                }}
                            />
                        </>
                    )}
                </QueryPanel>
            </section>
        </>
    );
}
export function AdminProduct() {
    const { id } = useParams();
    if (id === 'new') return <ProductForm />;
    return id && isProductId(id) ? (
        <ProductRead id={id.toLowerCase()} />
    ) : (
        <p role="alert">Invalid product UUID.</p>
    );
}
function ProductRead({ id }: { id: string }) {
    const query = useAdminQuery<Product>(`products/${id}`);
    return (
        <>
            <div className="section-heading">
                <h2>Product management</h2>
                <button
                    className="button secondary"
                    onClick={() => {
                        void query.refetch();
                    }}
                >
                    Reload product
                </button>
            </div>
            <QueryPanel query={query} blockWhileFetching>
                {(row) => <ProductForm key={`${row.id}:${row.version}`} product={row} />}
            </QueryPanel>
        </>
    );
}
function ProductForm({ product }: { product?: Product }) {
    const write = useCommand<Product>();
    const { command, error: recoveryError } = useAdminRecovery();
    const navigate = useNavigate();
    const [categoryId, setCategory] = useState(product?.category.id ?? '');
    const [validation, setValidation] = useState('');
    const blocked = write.isPending || !!command || !!recoveryError;
    return (
        <section className="card admin-panel">
            <h2>{product ? 'Edit product' : 'Create product'}</h2>
            {product && (
                <p>
                    Version {product.version} · <code>{product.id}</code> ·{' '}
                    <Link className="text-link" to={`/admin/products/${product.id}/stock`}>
                        Stock and movements
                    </Link>
                </p>
            )}
            {validation && (
                <p className="field-error" role="alert">
                    {validation}
                </p>
            )}
            <form
                className="admin-form"
                onSubmit={(event) => {
                    event.preventDefault();
                    const data = new FormData(event.currentTarget);
                    const price = text(data, 'price');
                    try {
                        if (toCents(price) <= 0n) throw new Error('Price must be positive.');
                        setValidation('');
                    } catch {
                        setValidation(
                            'Enter a positive USD price as a decimal string with exactly two fractional digits.',
                        );
                        return;
                    }
                    const body = {
                        name: text(data, 'name'),
                        description: text(data, 'description'),
                        price,
                        categoryId,
                        ...(product
                            ? { isActive: data.get('isActive') === 'on', expectedVersion: product.version }
                            : { sku: text(data, 'sku').toUpperCase() }),
                    };
                    write.mutate(
                        {
                            resource: product ? `products/${product.id}` : 'products',
                            method: product ? 'PUT' : 'POST',
                            body,
                        },
                        { onSuccess: (row) => navigate(`/admin/products/${row.id}`, { replace: true }) },
                    );
                }}
            >
                <fieldset disabled={blocked}>
                    {!product && (
                        <Field id="product-sku" label="SKU" error={fieldError(write.error, 'sku')}>
                            <input
                                id="product-sku"
                                name="sku"
                                required
                                maxLength={64}
                                pattern="[A-Za-z0-9-]+"
                            />
                        </Field>
                    )}
                    <Field id="product-name" label="Product name" error={fieldError(write.error, 'name')}>
                        <input
                            id="product-name"
                            name="name"
                            required
                            maxLength={200}
                            defaultValue={product?.name ?? ''}
                        />
                    </Field>
                    <Field id="product-description" label="Description">
                        <textarea
                            id="product-description"
                            name="description"
                            maxLength={2000}
                            defaultValue={product?.description ?? ''}
                        />
                    </Field>
                    <Field
                        id="product-price"
                        label="Price USD"
                        hint="Exact decimal string, for example 12.50."
                        error={fieldError(write.error, 'price')}
                    >
                        <input
                            id="product-price"
                            name="price"
                            inputMode="decimal"
                            required
                            defaultValue={product?.price ?? ''}
                            aria-describedby="product-price-hint"
                        />
                    </Field>
                    <CategoryPicker
                        value={categoryId}
                        change={setCategory}
                        currentName={product?.category.name}
                    />
                    {product && (
                        <label className="check-label">
                            <input type="checkbox" name="isActive" defaultChecked={product.isActive} />
                            Product active
                        </label>
                    )}
                    <button className="button" type="submit">
                        {write.isPending ? 'Confirming…' : product ? 'Save product' : 'Create product'}
                    </button>
                </fieldset>
            </form>
            {product?.isActive && (
                <button
                    className="button secondary danger-action"
                    disabled={blocked}
                    onClick={() =>
                        write.mutate({
                            resource: `products/${product.id}/archive`,
                            method: 'POST',
                            body: { expectedVersion: product.version },
                        })
                    }
                >
                    Archive product
                </button>
            )}
            {write.isSuccess && (
                <p className="notice" role="status">
                    Server confirmed product changes.
                </p>
            )}
        </section>
    );
}
export function AdminCategories() {
    const [search, setSearch] = useSearchParams();
    const page = pageNumber(search);
    const q = search.get('q') ?? '';
    const query = useAdminQuery<Page<Category>>('categories', { q, page, pageSize: 20, sort: 'name' });
    const [selected, setSelected] = useState<string | null>(null);
    return (
        <>
            <div className="section-heading">
                <h2>Categories</h2>
                <button className="button" onClick={() => setSelected('new')}>
                    Create category
                </button>
            </div>
            <FilterForm key={q} submit={(data) => setSearch({ q: text(data, 'q'), page: '1' })}>
                <Field id="category-q" label="Category keyword">
                    <input id="category-q" name="q" maxLength={100} defaultValue={q} />
                </Field>
            </FilterForm>
            <section className="card">
                <QueryPanel query={query}>
                    {(result) => (
                        <>
                            <Table
                                rows={result.items}
                                id={(row) => row.id}
                                caption="Category management"
                                columns={[
                                    {
                                        label: 'Category',
                                        cell: (row) => (
                                            <button
                                                className="text-button"
                                                onClick={() => setSelected(row.id)}
                                            >
                                                {row.name}
                                            </button>
                                        ),
                                    },
                                    {
                                        label: 'Status',
                                        cell: (row) => (row.isActive ? 'Active' : 'Archived'),
                                    },
                                    { label: 'Version', cell: (row) => row.version },
                                    { label: 'ID', cell: (row) => <code>{row.id}</code> },
                                ]}
                            />
                            <Paging value={result} change={(next) => setSearch({ q, page: String(next) })} />
                        </>
                    )}
                </QueryPanel>
            </section>
            {selected === 'new' ? (
                <CategoryForm key="new" close={() => setSelected(null)} />
            ) : (
                selected && <CategoryRead key={selected} id={selected} close={() => setSelected(null)} />
            )}
        </>
    );
}
function CategoryRead({ id, close }: { id: string; close: () => void }) {
    const query = useAdminQuery<Category>(`categories/${id}`);
    return (
        <QueryPanel query={query} blockWhileFetching>
            {(row) => <CategoryForm key={`${row.id}:${row.version}`} category={row} close={close} />}
        </QueryPanel>
    );
}
function CategoryForm({ category, close }: { category?: Category; close: () => void }) {
    const write = useCommand<Category>(close);
    const { command, error } = useAdminRecovery();
    const blocked = write.isPending || !!command || !!error;
    return (
        <section className="card admin-panel">
            <h2>{category ? `Edit category: ${category.name}` : 'New category'}</h2>
            <p>
                Archiving hides its public products. Restore the category to reveal products that are
                individually active.
            </p>
            <form
                className="admin-form"
                onSubmit={(event) => {
                    event.preventDefault();
                    const data = new FormData(event.currentTarget);
                    write.mutate({
                        resource: category ? `categories/${category.id}` : 'categories',
                        method: category ? 'PUT' : 'POST',
                        body: {
                            name: text(data, 'name'),
                            ...(category
                                ? {
                                      isActive: data.get('isActive') === 'on',
                                      expectedVersion: category.version,
                                  }
                                : {}),
                        },
                    });
                }}
            >
                <fieldset disabled={blocked}>
                    <Field id="category-name" label="Category name" error={fieldError(write.error, 'name')}>
                        <input
                            id="category-name"
                            name="name"
                            required
                            maxLength={200}
                            defaultValue={category?.name ?? ''}
                        />
                    </Field>
                    {category && (
                        <label className="check-label">
                            <input type="checkbox" name="isActive" defaultChecked={category.isActive} />
                            Category active
                        </label>
                    )}
                    <button className="button" type="submit">
                        Save category
                    </button>
                </fieldset>
            </form>
            {category?.isActive && (
                <button
                    className="button secondary danger-action"
                    disabled={blocked}
                    onClick={() =>
                        write.mutate({
                            resource: `categories/${category.id}/archive`,
                            method: 'POST',
                            body: { expectedVersion: category.version },
                        })
                    }
                >
                    Archive category
                </button>
            )}
            <button className="text-button" onClick={close}>
                Close category editor
            </button>
        </section>
    );
}
