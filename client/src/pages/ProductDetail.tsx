import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from 'react-router';
import { productOptions, isProductId } from '../api/catalog';
import { ApiError } from '../api/client';
import { formatUsd } from '../api/money';
import { useSession } from '../auth/context';
import { useCart } from '../state/cart';
import { QuantityForm } from '../components/QuantityForm';
import { ErrorPanel, Icon, Loading } from '../components/ui';

export function ProductDetail() {
    const { id = '' } = useParams();
    if (!isProductId(id)) return <UnavailableProduct />;
    return <ProductContent key={id.toLowerCase()} id={id.toLowerCase()} />;
}
function UnavailableProduct() {
    return (
        <section className="state-box card">
            <Icon name="product" size={38} />
            <h1>This product is unavailable.</h1>
            <p>It may have been archived or removed from the public catalog.</p>
            <Link className="button secondary" to="/products">
                Browse products
            </Link>
        </section>
    );
}
function ProductContent({ id }: { id: string }) {
    const { controller } = useSession();
    const product = useQuery(productOptions(controller.api, id));
    const add = useCart((state) => state.add);
    const [message, setMessage] = useState('');
    if (product.isPending) return <Loading text="Loading product…" />;
    if (product.isError)
        return product.error instanceof ApiError && product.error.status === 404 ? (
            <UnavailableProduct />
        ) : (
            <ErrorPanel
                title="Product could not be loaded"
                error={product.error}
                retry={() => {
                    void product.refetch();
                }}
            />
        );
    const item = product.data;
    if (!item.isActive || !item.category.isActive) return <UnavailableProduct />;
    return (
        <>
            <Link className="text-link back-link" to="/products">
                ← Back to products
            </Link>
            <section className="product-detail card">
                <div className="detail-art product-art" aria-hidden="true">
                    <Icon name="product" size={110} />
                    <span>{item.category.name}</span>
                </div>
                <div className="detail-copy">
                    <p className="eyebrow">{item.category.name}</p>
                    <h1 tabIndex={-1}>{item.name}</h1>
                    <p className="product-sku">{item.sku}</p>
                    <p className="detail-description">{item.description || 'No additional description.'}</p>
                    <p className="detail-price">
                        {formatUsd(item.price)} <span>USD</span>
                    </p>
                    <p className={`stock-note ${item.available === 0 ? 'unavailable' : ''}`}>
                        {item.available > 0
                            ? `${item.available} currently available`
                            : 'Currently out of stock'}
                    </p>
                    {product.isFetching && (
                        <p role="status" className="notice">
                            Checking product details…
                        </p>
                    )}
                    <QuantityForm
                        id={`add-${id}`}
                        name={item.name}
                        label="Add to cart"
                        disabled={item.available === 0 || product.isFetching}
                        onSave={(quantity) => {
                            add(id, quantity);
                            setMessage(`${quantity} added to your cart. Nothing reserved.`);
                        }}
                    />
                    <p className="cart-announcement" role="status">
                        {message}
                    </p>
                    <Link className="text-link" to="/cart">
                        View your cart <Icon name="arrow" size={17} />
                    </Link>
                    <p className="quiet-note">
                        This is a catalog estimate. Adding to cart does not reserve inventory; checkout
                        confirms price and stock.
                    </p>
                    <button
                        className="text-button"
                        onClick={() => {
                            void product.refetch();
                        }}
                    >
                        Refresh product
                    </button>
                </div>
            </section>
        </>
    );
}
