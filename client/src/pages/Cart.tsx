import { useEffect, useRef, useState } from 'react';
import { useQueries } from '@tanstack/react-query';
import { Link } from 'react-router';
import { ApiError } from '../api/client';
import { productOptions } from '../api/catalog';
import { formatUsd } from '../api/money';
import type { Product } from '../api/types';
import { useSession } from '../auth/context';
import { estimatedLine, estimatedTotal, itemIssue } from '../cart/estimate';
import { useCart } from '../state/cart';
import { QuantityForm } from '../components/QuantityForm';
import { ErrorPanel, Icon, PageTitle } from '../components/ui';

interface Review {
    signature: string;
    changes: { id: string; name: string; previous: string; current: string }[];
}

export function Cart() {
    const { controller, session } = useSession();
    const { items, update, remove, clear, persistenceUnavailable } = useCart();
    const results = useQueries({
        queries: items.map((item) => productOptions(controller.api, item.productId)),
    });
    const [checking, setChecking] = useState(false);
    const [review, setReview] = useState<Review | null>(null);
    const [announcement, setAnnouncement] = useState('');
    const checkEpoch = useRef(0);
    const itemSignature = JSON.stringify(items);
    useEffect(() => {
        checkEpoch.current++;
        setReview(null);
    }, [itemSignature]);
    useEffect(
        () => () => {
            checkEpoch.current++;
        },
        [],
    );
    const products = new Map<string, Product>();
    results.forEach((result, index) => {
        if (result.isSuccess && !result.isError) products.set(items[index]!.productId, result.data);
    });
    const fetching = results.some((result) => result.isFetching || result.isPending);
    const unavailable = results.some((result) => result.isError);
    const issues = items.some((item) => {
        const product = products.get(item.productId);
        return !product || !!itemIssue(product, item.quantity);
    });
    let total: string | null = null;
    let amountError = '';
    if (items.length && products.size === items.length && !unavailable) {
        try {
            total = estimatedTotal(items, products);
        } catch {
            amountError = 'This cart exceeds the supported USD amount. Reduce quantities or remove an item.';
        }
    }
    const signature = JSON.stringify([
        items,
        [...products.values()].map((product) => [
            product.id,
            product.price,
            product.available,
            product.version,
        ]),
    ]);
    const reviewed = review?.signature === signature && !fetching && !issues && !amountError;

    async function check() {
        const epoch = ++checkEpoch.current;
        const before = new Map(products);
        setReview(null);
        setChecking(true);
        setAnnouncement('');
        try {
            const checked = await Promise.all(results.map((result) => result.refetch()));
            if (epoch !== checkEpoch.current) return;
            const current = checked
                .filter((result) => result.isSuccess && !result.isError)
                .map((result) => result.data!);
            const changes = current.flatMap((product) => {
                const previous = before.get(product.id);
                return previous && previous.price !== product.price
                    ? [
                          {
                              id: product.id,
                              name: product.name,
                              previous: previous.price,
                              current: product.price,
                          },
                      ]
                    : [];
            });
            setReview({
                signature: JSON.stringify([
                    items,
                    current.map((product) => [product.id, product.price, product.available, product.version]),
                ]),
                changes,
            });
            setAnnouncement('Catalog check finished. Review any price or availability messages below.');
        } finally {
            setChecking(false);
        }
    }

    return (
        <>
            <PageTitle
                eyebrow="YOUR LIST, YOUR PACE"
                title="A cart worth a second look."
                description="Saved in this browser and shared across sign-ins. Only product IDs and quantities are stored."
                action={
                    <Link className="button secondary" to="/products">
                        Continue browsing <Icon name="arrow" size={17} />
                    </Link>
                }
            />
            {persistenceUnavailable && (
                <p className="notice" role="alert">
                    Browser storage is unavailable. Changes remain in this tab and may not survive a reload.
                </p>
            )}
            <p className="cart-announcement" role="status" aria-live="polite">
                {announcement}
            </p>
            {!items.length ? (
                <section className="state-box card">
                    <Icon name="cart" size={40} />
                    <h2>Your cart is empty.</h2>
                    <p>Find something useful and add it to your list.</p>
                    <Link className="button" to="/products">
                        Browse products
                    </Link>
                </section>
            ) : (
                <div className="cart-layout">
                    <section className="cart-items" aria-label="Cart items" aria-busy={checking}>
                        <div className="results-heading">
                            <h2>
                                {items.length} {items.length === 1 ? 'product' : 'products'}
                            </h2>
                            <button
                                className="text-button"
                                disabled={checking}
                                onClick={() => {
                                    clear();
                                    setAnnouncement('Cart cleared.');
                                }}
                            >
                                Clear cart
                            </button>
                        </div>
                        {items.map((item, index) => {
                            const result = results[index]!;
                            const product = products.get(item.productId);
                            const missing = result.error instanceof ApiError && result.error.status === 404;
                            const name = product?.name ?? `Product ${item.productId}`;
                            const issue = product ? itemIssue(product, item.quantity) : null;
                            let line: string | null = null;
                            if (product) {
                                try {
                                    line = estimatedLine(product, item.quantity);
                                } catch {
                                    /* The summary exposes the amount limit. */
                                }
                            }
                            return (
                                <article className="cart-item card" key={item.productId} aria-label={name}>
                                    <div className="cart-item-icon" aria-hidden="true">
                                        <Icon name="product" size={30} />
                                    </div>
                                    <div className="cart-item-copy">
                                        <h3>
                                            {product ? (
                                                <Link to={`/products/${item.productId}`}>{name}</Link>
                                            ) : missing ? (
                                                'Product unavailable'
                                            ) : (
                                                'Checking product…'
                                            )}
                                        </h3>
                                        {product && (
                                            <>
                                                <p className="product-sku">{product.sku}</p>
                                                <p>{formatUsd(product.price)} USD each</p>
                                                <p className="muted">
                                                    {product.available} currently available · estimate only
                                                </p>
                                            </>
                                        )}
                                        {result.isPending && (
                                            <p role="status">Loading current catalog details…</p>
                                        )}
                                        {result.isFetching && !result.isPending && (
                                            <p role="status">Checking price and availability…</p>
                                        )}
                                        {missing && (
                                            <p className="unavailable" role="alert">
                                                This product is no longer available. It may be archived or
                                                removed. Remove it from your cart.
                                            </p>
                                        )}
                                        {result.isError && !missing && (
                                            <ErrorPanel
                                                title="This item could not be checked"
                                                error={result.error}
                                                retry={() => {
                                                    void result.refetch();
                                                }}
                                            />
                                        )}
                                        {issue && (
                                            <p className="unavailable" role="alert">
                                                {issue}
                                            </p>
                                        )}
                                        <QuantityForm
                                            key={`${item.productId}:${item.quantity}`}
                                            id={`quantity-${item.productId}`}
                                            name={name}
                                            quantity={item.quantity}
                                            label={`Update quantity${product ? ` for ${product.name}` : ''}`}
                                            disabled={checking}
                                            onSave={(quantity) => {
                                                update(item.productId, quantity);
                                                setAnnouncement(`Quantity updated for ${name}.`);
                                            }}
                                        />
                                        <button
                                            className="text-button remove-item"
                                            disabled={checking}
                                            aria-label={`Remove ${name} from cart`}
                                            onClick={() => {
                                                remove(item.productId);
                                                setAnnouncement(`${name} removed from cart.`);
                                            }}
                                        >
                                            Remove item
                                        </button>
                                    </div>
                                    <div className="cart-line-total">
                                        <span className="muted">Estimated line</span>
                                        <strong>{line ? formatUsd(line) : 'Unavailable'}</strong>
                                    </div>
                                </article>
                            );
                        })}
                    </section>
                    <aside className="cart-summary card" aria-label="Cart summary">
                        <span className="icon-tile">
                            <Icon name="cart" size={24} />
                        </span>
                        <h2>At a glance</h2>
                        <dl>
                            <div>
                                <dt>Items</dt>
                                <dd>{items.reduce((count, item) => count + item.quantity, 0)}</dd>
                            </div>
                            <div className="summary-total">
                                <dt>Estimated total</dt>
                                <dd data-testid="cart-total">{total ? formatUsd(total) : 'Unavailable'}</dd>
                            </div>
                        </dl>
                        <p className="quiet-note">
                            USD · catalog prices. The full estimate is withheld if an item cannot be checked.
                        </p>
                        {amountError && (
                            <p className="unavailable" role="alert">
                                {amountError}
                            </p>
                        )}
                        {(issues || unavailable) && !fetching && (
                            <p className="unavailable">
                                Resolve unavailable items or quantities before checkout.
                            </p>
                        )}
                        {fetching && (
                            <p role="status" className="notice">
                                Checking catalog details…
                            </p>
                        )}
                        <button
                            className="button full"
                            disabled={checking || fetching}
                            onClick={() => {
                                void check();
                            }}
                        >
                            {checking ? 'Checking…' : 'Check prices & availability'}
                        </button>
                        {review?.changes.map((change) => (
                            <p className="notice" role="status" key={change.id}>
                                {change.name}: price changed from {formatUsd(change.previous)} to{' '}
                                {formatUsd(change.current)}. Review the new estimate.
                            </p>
                        ))}
                        {reviewed && (
                            <p className="notice" role="status">
                                Cart estimates checked. Nothing has been reserved or ordered.
                            </p>
                        )}
                        <p className="quiet-note">
                            A catalog check can include a price cached for up to 30 seconds. Stock and prices
                            can change afterward. Checkout revalidates both on the server when creating an
                            order.
                        </p>
                        {session.phase === 'anonymous' && (
                            <Link className="text-link" to="/login" state={{ from: '/cart' }}>
                                Sign in to your account <Icon name="arrow" size={16} />
                            </Link>
                        )}
                        <p className="cart-device-note">
                            Using a shared device? Clear your cart when finished. Signing out keeps this
                            browser cart.
                        </p>
                    </aside>
                </div>
            )}
        </>
    );
}
