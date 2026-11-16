import { useRef, useState } from 'react';
import { useQueries, useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router';
import { useSession } from '../auth/context';
import { useOperations } from '../checkout/context';
import { checkoutBody } from '../checkout/pending';
import { useCart } from '../state/cart';
import { productOptions } from '../api/catalog';
import { orderOptions } from '../api/orders';
import type { CreatedOrder } from '../api/types';
import { formatUsd } from '../api/money';
import { estimatedTotal, itemIssue } from '../cart/estimate';
import { ErrorPanel, Loading, PageTitle } from '../components/ui';

export function Checkout() {
    const { controller, session } = useSession();
    const { operations, pending } = useOperations();
    const items = useCart((state) => state.items);
    const userId = session.phase === 'authenticated' ? session.user.id : '';
    const record = pending.checkout;
    const selected = record ? (JSON.parse(record.body) as { items: typeof items }).items : items;
    const products = useQueries({
        queries: selected.map((item) => productOptions(controller.api, item.productId)),
    });
    const confirmed = useQuery({
        ...orderOptions(controller.api, controller, userId, record?.orderId ?? ''),
        enabled: !!record?.orderId,
    });
    const [busy, setBusy] = useState(false);
    const inFlight = useRef(false);
    const [error, setError] = useState<unknown>(null);
    const [acknowledge, setAcknowledge] = useState(false);
    const navigate = useNavigate();
    async function action(task: () => Promise<void>) {
        if (inFlight.current) return;
        inFlight.current = true;
        setBusy(true);
        setError(null);
        try {
            await task();
        } catch (failure) {
            setError(failure);
        } finally {
            inFlight.current = false;
            setBusy(false);
        }
    }
    const checked = products.every(
        (result, index) =>
            result.isSuccess && !result.isFetching && !itemIssue(result.data, selected[index]!.quantity),
    );
    let estimate: string | null = null;
    try {
        if (products.every((result) => result.isSuccess))
            estimate = estimatedTotal(
                selected,
                new Map(products.map((result) => [result.data!.id, result.data!])),
            );
    } catch {
        /* Full estimate remains unavailable. */
    }
    const changed = !!record && (items.length === 0 || checkoutBody(items) !== record.body);
    const verified = confirmed.isSuccess && checkoutBody(confirmed.data.items) === record?.body;
    return (
        <>
            <PageTitle
                eyebrow="ONE INTENTION, ONE REQUEST"
                title="Review and reserve."
                description="The server confirms current prices and reserves available stock when it creates your order."
            />
            {error != null && <ErrorPanel title="The operation could not be confirmed" error={error} />}
            {pending.error ? (
                <section className="card checkout-panel">
                    <p role="alert">{pending.error}</p>
                    <Link className="text-link" to="/orders">
                        Review my orders
                    </Link>
                    <label className="check-label">
                        <input
                            type="checkbox"
                            checked={acknowledge}
                            onChange={(event) => setAcknowledge(event.target.checked)}
                        />
                        I understand an order or payment may already exist. Resetting local recovery does not
                        cancel it.
                    </label>
                    <button
                        className="button secondary"
                        disabled={!acknowledge || busy}
                        onClick={() => {
                            void action(() => controller.guard(userId, () => operations.resetInvalid()));
                        }}
                    >
                        Reset local recovery record
                    </button>
                </section>
            ) : record?.orderId ? (
                <section className="card checkout-panel">
                    {confirmed.isPending ? (
                        <Loading text="Verifying the created order…" />
                    ) : confirmed.isError ? (
                        <ErrorPanel
                            title="Verify this order before finishing"
                            error={confirmed.error}
                            retry={() => {
                                void confirmed.refetch();
                            }}
                        />
                    ) : verified ? (
                        <>
                            <h2>Order created</h2>
                            <p>
                                Order <code>{confirmed.data.id}</code> · Authoritative total{' '}
                                <strong>{formatUsd(confirmed.data.total)} USD</strong>
                            </p>
                            <p>
                                The cart is kept until you finish this checkout record. A modified cart will
                                be retained.
                            </p>
                            <button
                                className="button"
                                disabled={busy}
                                onClick={() => {
                                    void action(async () => {
                                        await controller.guard(userId, () =>
                                            operations.closeCheckout(record.key, true),
                                        );
                                        const current = useCart.getState().items;
                                        if (current.length && checkoutBody(current) === record.body)
                                            useCart.getState().clear();
                                        navigate(`/orders/${record.orderId}`);
                                    });
                                }}
                            >
                                View order & finish checkout
                            </button>
                        </>
                    ) : (
                        <p role="alert">
                            The recovery record does not match the server order. Review your orders before
                            discarding it.
                        </p>
                    )}
                </section>
            ) : !selected.length ? (
                <section className="state-box card">
                    <h2>Your cart is empty.</h2>
                    <Link className="button" to="/products">
                        Browse products
                    </Link>
                </section>
            ) : (
                <section className="card checkout-panel">
                    {record && (
                        <div className="notice">
                            <strong>Checkout outcome unresolved</strong>
                            <p>
                                The exact original payload and key are retained. Recovering replays that
                                request and cannot change it to your current cart.
                            </p>
                            {changed && (
                                <p>
                                    Your cart has changed. Resolve or explicitly discard this original
                                    intention before creating a different order.
                                </p>
                            )}
                        </div>
                    )}
                    <ul className="checkout-lines">
                        {selected.map((item, index) => (
                            <li key={item.productId}>
                                <span>{products[index]?.data?.name ?? item.productId}</span>
                                <strong>× {item.quantity}</strong>
                            </li>
                        ))}
                    </ul>
                    <p>
                        Estimated total:{' '}
                        <strong>{estimate ? `${formatUsd(estimate)} USD` : 'Unavailable'}</strong>. The
                        created order supplies the authoritative amount.
                    </p>
                    {!record && !checked && (
                        <p className="notice">
                            Checking the catalog, or an item needs attention. Return to your cart to resolve
                            availability or load errors.
                        </p>
                    )}
                    <div className="action-row">
                        <button
                            className="button"
                            disabled={busy || (!record && (!checked || !estimate))}
                            onClick={() => {
                                void action(async () => {
                                    await operations.checkout(items, (intention) =>
                                        controller.guard(userId, () =>
                                            controller.api.request<CreatedOrder>('/api/orders', {
                                                method: 'POST',
                                                bodyText: intention.body,
                                                idempotencyKey: intention.key,
                                                timeoutMs: 15_000,
                                            }),
                                        ),
                                    );
                                    await controller.queries.invalidateQueries({
                                        queryKey: ['private', userId, 'orders'],
                                    });
                                });
                            }}
                        >
                            {busy
                                ? 'Confirming…'
                                : record
                                  ? 'Recover original checkout'
                                  : 'Create order & reserve stock'}
                        </button>
                        <Link className="button secondary" to="/cart">
                            Return to cart
                        </Link>
                    </div>
                    <p className="quiet-note">
                        If the response is lost or a server error occurs, keep this recovery record and retry
                        the original request. Nothing is marked paid from this screen or from return URL
                        parameters.
                    </p>
                </section>
            )}
            {record && (
                <section className="card checkout-panel abandonment">
                    <h2>Close or discard this intention</h2>
                    <p>
                        Prefer recovering the original request. Discarding only removes local recovery; an
                        order may already exist and keep reserving stock. Check your orders and cancel an
                        eligible order explicitly. A new checkout may create another order.
                    </p>
                    <Link className="text-link" to="/orders">
                        Review my orders
                    </Link>
                    <label className="check-label">
                        <input
                            type="checkbox"
                            checked={acknowledge}
                            onChange={(event) => setAcknowledge(event.target.checked)}
                        />
                        I understand discarding recovery does not cancel an existing order.
                    </label>
                    <button
                        className="button secondary"
                        disabled={!acknowledge || busy}
                        onClick={() => {
                            void action(async () => {
                                await controller.guard(userId, () =>
                                    operations.closeCheckout(record.key, false),
                                );
                                setAcknowledge(false);
                            });
                        }}
                    >
                        Discard checkout recovery
                    </button>
                </section>
            )}
        </>
    );
}
