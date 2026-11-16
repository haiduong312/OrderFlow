import { useCallback, useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router';
import { useSession } from '../auth/context';
import { categoriesOptions, productsOptions } from '../api/catalog';
import type { Product, ProductParameters } from '../api/types';
import { formatUsd } from '../api/money';
import { catalogParameters, catalogSearch } from '../catalog/parameters';
import { useCart } from '../state/cart';
import { ErrorPanel, Icon, Loading, PageTitle } from '../components/ui';

function Search({ value, change }: { value: string; change: (value: string) => void }) {
    const [draft, setDraft] = useState(value);
    useEffect(() => {
        setDraft(value);
    }, [value]);
    useEffect(() => {
        if (draft.trim() === value) return;
        const timer = window.setTimeout(() => change(draft.trim()), 300);
        return () => window.clearTimeout(timer);
    }, [draft, value, change]);
    return (
        <form
            className="catalog-search"
            role="search"
            onSubmit={(event) => {
                event.preventDefault();
                change(draft.trim());
            }}
        >
            <label htmlFor="product-search">Search products</label>
            <input
                id="product-search"
                type="search"
                maxLength={100}
                placeholder="Name or SKU…"
                value={draft}
                onChange={(event) => setDraft(event.target.value)}
                aria-describedby="search-hint"
            />
            <span id="search-hint" className="field-hint">
                Search updates after a short pause.
            </span>
        </form>
    );
}

function Categories({ selected, change }: { selected: string | null; change: (id: string | null) => void }) {
    const { controller } = useSession();
    const [page, setPage] = useState(1);
    const result = useQuery(categoriesOptions(controller.api, { q: '', page, pageSize: 20, sort: 'name' }));
    return (
        <aside className="category-filter card" aria-label="Product categories">
            <h2>Categories</h2>
            <button
                className={`category-choice ${selected === null ? 'selected' : ''}`}
                aria-pressed={selected === null}
                onClick={() => change(null)}
            >
                All products
            </button>
            {result.isPending ? (
                <Loading text="Loading categories…" />
            ) : result.isError ? (
                <ErrorPanel
                    title="Categories could not be loaded"
                    error={result.error}
                    retry={() => {
                        void result.refetch();
                    }}
                />
            ) : (
                <>
                    {result.data.items.map((category) => (
                        <button
                            key={category.id}
                            className={`category-choice ${selected === category.id ? 'selected' : ''}`}
                            aria-pressed={selected === category.id}
                            onClick={() => change(category.id)}
                        >
                            {category.name}
                        </button>
                    ))}
                    {!result.data.items.length && <p className="muted">No categories on this page.</p>}
                    {selected && !result.data.items.some((category) => category.id === selected) && (
                        <p className="quiet-note">
                            A category from another page or saved link is selected. Choose All products to
                            reset.
                        </p>
                    )}
                    {result.data.totalCount > 20 && (
                        <div className="category-paging">
                            <button
                                className="button secondary small"
                                aria-label="Previous categories"
                                disabled={page <= 1}
                                onClick={() => setPage(page - 1)}
                            >
                                Previous
                            </button>
                            <span>Page {page}</span>
                            <button
                                className="button secondary small"
                                aria-label="Next categories"
                                disabled={page * 20 >= result.data.totalCount}
                                onClick={() => setPage(page + 1)}
                            >
                                Next
                            </button>
                        </div>
                    )}
                </>
            )}
        </aside>
    );
}

function ProductCard({ product, added }: { product: Product; added: (message: string) => void }) {
    const add = useCart((state) => state.add);
    const [error, setError] = useState('');
    function addOne() {
        try {
            add(product.id, 1);
            setError('');
            added(`${product.name} added to cart. Nothing reserved.`);
        } catch (failure) {
            setError(failure instanceof Error ? failure.message : 'Could not add this product.');
        }
    }
    return (
        <article className="product-card card">
            <Link className="product-art" to={`/products/${product.id}`} aria-label={`View ${product.name}`}>
                <Icon name="product" size={48} />
                <span>{product.category.name}</span>
            </Link>
            <div className="product-card-body">
                <span className="product-sku">{product.sku}</span>
                <h2>
                    <Link to={`/products/${product.id}`}>{product.name}</Link>
                </h2>
                <p className="product-description">
                    {product.description || 'View product details and current availability.'}
                </p>
                <div className="product-price-row">
                    <strong>{formatUsd(product.price)}</strong>
                    <span>USD</span>
                </div>
                <p className={`stock-note ${product.available === 0 ? 'unavailable' : ''}`}>
                    {product.available > 0
                        ? `${product.available} currently available`
                        : 'Currently out of stock'}
                </p>
                <button
                    className="button full"
                    disabled={!product.isActive || !product.category.isActive || product.available === 0}
                    aria-label={`Add ${product.name} to cart`}
                    onClick={addOne}
                >
                    <Icon name="cart" size={18} /> Add to cart
                </button>
                {error && (
                    <p className="field-error" role="alert">
                        {error}
                    </p>
                )}
            </div>
        </article>
    );
}

export function Catalog() {
    const { controller } = useSession();
    const [search, setSearch] = useSearchParams();
    const parameters = catalogParameters(search);
    const [message, setMessage] = useState('');
    const change = useCallback(
        (patch: Partial<ProductParameters>) => {
            const current = catalogParameters(search);
            setSearch(catalogSearch({ ...current, ...patch, page: patch.page ?? 1 }));
        },
        [search, setSearch],
    );
    const keyword = useCallback((q: string) => change({ q }), [change]);
    const products = useQuery(productsOptions(controller.api, parameters));
    return (
        <>
            <PageTitle
                eyebrow="FIND YOUR NEXT FAVORITE"
                title="A little something useful."
                description="Browse the catalog, take a closer look, and make a list of your own."
                action={
                    <Link className="button secondary" to="/cart">
                        <Icon name="cart" /> View cart
                    </Link>
                }
            />
            <div className="shopping-notice">
                <Icon name="shield" size={18} />
                <span>Adding to your cart does not reserve stock. Prices and availability can change.</span>
            </div>
            <p className="cart-announcement" role="status" aria-live="polite">
                {message}
            </p>
            <div className="catalog-layout">
                <Categories
                    selected={parameters.categoryId}
                    change={(categoryId) => change({ categoryId })}
                />
                <section className="catalog-results" aria-label="Products" aria-busy={products.isFetching}>
                    <div className="catalog-toolbar card">
                        <Search value={parameters.q} change={keyword} />
                        <div className="field">
                            <label htmlFor="product-sort">Sort by</label>
                            <select
                                id="product-sort"
                                value={parameters.sort}
                                onChange={(event) =>
                                    change({ sort: event.target.value as ProductParameters['sort'] })
                                }
                            >
                                <option value="newest">Newest first</option>
                                <option value="name">Name</option>
                                <option value="price-asc">Price: low to high</option>
                                <option value="price-desc">Price: high to low</option>
                            </select>
                        </div>
                        <div className="field">
                            <label htmlFor="product-page-size">Per page</label>
                            <select
                                id="product-page-size"
                                value={parameters.pageSize}
                                onChange={(event) => change({ pageSize: Number(event.target.value) })}
                            >
                                <option value={12}>12</option>
                                <option value={24}>24</option>
                                <option value={48}>48</option>
                            </select>
                        </div>
                    </div>
                    {products.isPending ? (
                        <Loading text="Loading products…" />
                    ) : products.isError ? (
                        <ErrorPanel
                            title="Products could not be loaded"
                            error={products.error}
                            retry={() => {
                                void products.refetch();
                            }}
                        />
                    ) : (
                        <>
                            <div className="results-heading">
                                <p>
                                    {products.data.totalCount} products · Page {parameters.page} of{' '}
                                    {Math.max(1, Math.ceil(products.data.totalCount / parameters.pageSize))}
                                </p>
                                <button
                                    className="text-button"
                                    onClick={() => {
                                        void products.refetch();
                                    }}
                                >
                                    Refresh catalog
                                </button>
                            </div>
                            {products.isFetching && (
                                <p role="status" className="notice">
                                    Refreshing catalog prices and availability…
                                </p>
                            )}
                            {products.data.items.length ? (
                                <div className="product-grid">
                                    {products.data.items.map((product) => (
                                        <ProductCard key={product.id} product={product} added={setMessage} />
                                    ))}
                                </div>
                            ) : (
                                <div className="state-box card">
                                    <Icon name="product" size={34} />
                                    <h2>No products here.</h2>
                                    <p>Try another keyword or category, or return to the first page.</p>
                                    <button
                                        className="button secondary"
                                        onClick={() => change({ q: '', categoryId: null, page: 1 })}
                                    >
                                        Reset filters
                                    </button>
                                </div>
                            )}
                            <nav className="pagination" aria-label="Product pages">
                                <span>Page {parameters.page}</span>
                                <div>
                                    <button
                                        className="button secondary small"
                                        disabled={parameters.page <= 1 || products.isFetching}
                                        onClick={() => change({ page: parameters.page - 1 })}
                                    >
                                        Previous products
                                    </button>
                                    <button
                                        className="button secondary small"
                                        disabled={
                                            parameters.page * parameters.pageSize >=
                                                products.data.totalCount || products.isFetching
                                        }
                                        onClick={() => change({ page: parameters.page + 1 })}
                                    >
                                        Next products
                                    </button>
                                </div>
                            </nav>
                        </>
                    )}
                </section>
            </div>
        </>
    );
}
