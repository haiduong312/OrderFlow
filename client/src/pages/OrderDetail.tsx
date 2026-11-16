import { useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from 'react-router';
import { useSession } from '../auth/context';
import { useOperations } from '../checkout/context';
import { isProductId } from '../api/catalog';
import { orderOptions, paymentsOptions } from '../api/orders';
import type { OrderDetail as OrderDto, PaymentAttempt, PaymentStatus } from '../api/types';
import { formatUsd } from '../api/money';
import { ErrorPanel, Loading, PageTitle } from '../components/ui';
import { ReservationClock } from '../components/ReservationClock';

export function OrderDetail() {
    const { id = '' } = useParams();
    return isProductId(id) ? (
        <OrderContent key={id.toLowerCase()} id={id.toLowerCase()} />
    ) : (
        <section className="state-box">
            <h1>Order unavailable</h1>
            <Link to="/orders">My orders</Link>
        </section>
    );
}
function OrderContent({ id }: { id: string }) {
    const { controller, session } = useSession();
    const { operations, pending } = useOperations();
    const userId = session.phase === 'authenticated' ? session.user.id : '';
    const [page, setPage] = useState(1);
    const order = useQuery(orderOptions(controller.api, controller, userId, id));
    const payment = useQuery(paymentsOptions(controller.api, controller, userId, id, page));
    const [busy, setBusy] = useState(false);
    const inFlight = useRef(false);
    const [error, setError] = useState<unknown>(null);
    const [discard, setDiscard] = useState(false);
    const intent = pending.payments.find((row) => row.orderId === id);
    const unresolved = !!intent && !intent.attemptId;
    const failed = payment.data?.attempts[0]?.status === 'Failed';
    const active = payment.data?.attempts.some((attempt) =>
        ['Queued', 'Pending', 'Unknown'].includes(attempt.status),
    );
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
            await controller.queries.invalidateQueries(
                { queryKey: ['private', userId] },
                { cancelRefetch: false },
            );
            inFlight.current = false;
            setBusy(false);
        }
    }
    if (order.isPending) return <Loading text="Loading your order…" />;
    if (order.isError)
        return (
            <ErrorPanel
                title="This order could not be loaded"
                error={order.error}
                retry={() => {
                    void order.refetch();
                }}
            />
        );
    const item = order.data;
    const eligible = item.status === 'PendingPayment' && item.expiry.canCancel;
    return (
        <>
            <PageTitle
                eyebrow="SERVER-CONFIRMED ORDER"
                title={`Order ${id.slice(0, 8)}`}
                description="Order state, immutable line prices and financial recovery come from the server."
                action={
                    <button
                        className="button secondary"
                        onClick={() => {
                            void order.refetch();
                            void payment.refetch();
                        }}
                    >
                        Refresh order
                    </button>
                }
            />
            <Link className="text-link" to="/orders">
                ← All my orders
            </Link>
            <section className="card order-overview">
                <span className="badge" data-testid="order-status">
                    {item.status}
                </span>
                <h2>Order total: {formatUsd(item.total)} USD</h2>
                <p>
                    <code>{item.id}</code>
                </p>
                {order.isFetching && <p role="status">Refreshing authoritative order state…</p>}
                <p>
                    Server observed {new Date(item.expiry.observedAt).toLocaleString()} · dates use your
                    device time zone.
                </p>
                {item.status === 'PendingPayment' && (
                    <ReservationClock expiresAt={item.expiresAt} observedAt={item.expiry.observedAt} />
                )}
                {item.expiry.isExpired && item.status === 'PendingPayment' && (
                    <p className="notice">
                        Server reports the deadline has passed; persisted expiry finalization is pending. This
                        UI does not change order state.
                    </p>
                )}
                {item.status === 'Expired' && (
                    <p className="notice">
                        This order expired. A verified late capture is handled separately and cannot restore
                        it.
                    </p>
                )}
                {error != null && <ErrorPanel title="Action outcome needs verification" error={error} />}
                {pending.error && (
                    <p className="notice" role="alert">
                        {pending.error} <Link to="/checkout">Review recovery storage</Link>
                    </p>
                )}
                <div className="action-row">
                    {(unresolved || (eligible && !active)) && (
                        <button
                            className="button"
                            disabled={
                                busy || !!pending.error || payment.isPending || payment.isError || page !== 1
                            }
                            onClick={() => {
                                void action(async () => {
                                    await operations.payment(
                                        id,
                                        (record) =>
                                            controller.guard(userId, () =>
                                                controller.api.request<PaymentAttempt>(
                                                    `/api/orders/${id}/payment-attempts`,
                                                    {
                                                        method: 'POST',
                                                        bodyText: '{}',
                                                        idempotencyKey: record.key,
                                                        timeoutMs: 15_000,
                                                    },
                                                ),
                                            ),
                                        !unresolved && failed,
                                    );
                                });
                            }}
                        >
                            {busy
                                ? 'Confirming…'
                                : unresolved
                                  ? 'Recover original payment request'
                                  : failed
                                    ? 'Start a new payment attempt'
                                    : 'Start payment'}
                        </button>
                    )}
                    {eligible && (
                        <button
                            className="button secondary"
                            disabled={busy}
                            onClick={() => {
                                void action(async () => {
                                    await controller.guard(userId, () =>
                                        controller.api.request<OrderDto>(`/api/orders/${id}/cancel`, {
                                            method: 'POST',
                                            body: {},
                                            timeoutMs: 15_000,
                                        }),
                                    );
                                });
                            }}
                        >
                            Cancel eligible order
                        </button>
                    )}
                </div>
                {unresolved && (
                    <p className="notice">
                        Payment initiation outcome unresolved. Recover using the original key, even if the
                        order has since expired. A lost response must not create a new intention.
                    </p>
                )}
                {unresolved && (
                    <div className="abandonment">
                        <label className="check-label">
                            <input
                                type="checkbox"
                                checked={discard}
                                onChange={(event) => setDiscard(event.target.checked)}
                            />
                            I understand discarding local payment recovery does not cancel an in-flight
                            payment or refund.
                        </label>
                        <button
                            className="text-button"
                            disabled={!discard || busy}
                            onClick={() => {
                                void action(async () => {
                                    await controller.guard(userId, () =>
                                        operations.closePayment(id, intent!.key),
                                    );
                                    setDiscard(false);
                                });
                            }}
                        >
                            Discard local payment recovery
                        </button>
                    </div>
                )}
                {active && (
                    <p className="notice">
                        Payment is in progress. Queued, Pending, Unknown or a provider observation is not
                        proof of a paid order. Wait for verified settlement.
                    </p>
                )}
                {import.meta.env.DEV && (
                    <p className="quiet-note">
                        Development uses the operator-controlled simulator API. No customer simulator payment
                        URL is supported. Wait for verified callback/reconciliation; return parameters never
                        confirm payment.
                    </p>
                )}
            </section>
            <section className="card order-lines">
                <h2>Order items</h2>
                <p className="quiet-note">Prices were captured when the order was created.</p>
                {item.items.map((line) => (
                    <article key={line.productId}>
                        <div>
                            <h3>{line.name}</h3>
                            <p className="product-sku">{line.sku}</p>
                            <p>
                                {formatUsd(line.unitPrice)} × {line.quantity}
                            </p>
                        </div>
                        <strong>{formatUsd(line.lineTotal)}</strong>
                    </article>
                ))}
            </section>
            {payment.isPending ? (
                <Loading text="Loading payment and refund status…" />
            ) : payment.isError ? (
                <ErrorPanel
                    title="Payment status could not be verified"
                    error={payment.error}
                    retry={() => {
                        void payment.refetch();
                    }}
                />
            ) : (
                <PaymentActivity
                    value={payment.data}
                    fetching={payment.isFetching}
                    page={page}
                    change={setPage}
                />
            )}
            <section className="card order-history">
                <h2>Order state history</h2>
                <ol>
                    {item.history.map((entry) => (
                        <li key={entry.version}>
                            <strong>{entry.status}</strong>
                            <span>
                                {new Date(entry.occurredAt).toLocaleString()} · {entry.actorKind}
                            </span>
                        </li>
                    ))}
                </ol>
            </section>
        </>
    );
}
function PaymentActivity({
    value,
    fetching,
    page,
    change,
}: {
    value: PaymentStatus;
    fetching: boolean;
    page: number;
    change: (page: number) => void;
}) {
    const pages = Math.max(
        1,
        Math.ceil(Math.max(value.attemptCount, value.captureCount, value.refundCount) / value.pageSize),
    );
    return (
        <section className="card payment-activity">
            <h2>Payment & refund activity</h2>
            <p className="quiet-note">
                Observed {new Date(value.observedAt).toLocaleString()}. Each section uses the same page
                number; older entries can be on another page.
            </p>
            {fetching && <p role="status">Refreshing payment activity…</p>}
            <h3>Payment attempts</h3>
            {!value.attempts.length && <p>No payment attempts on this page.</p>}
            {value.attempts.map((attempt) => (
                <article key={attempt.id}>
                    <strong>
                        {attempt.status} · {formatUsd(attempt.amount)} USD
                    </strong>
                    <code>{attempt.id}</code>
                    {attempt.needsAttention && (
                        <p className="unavailable">
                            Payment needs review. Its outcome is not confirmed by a browser response.
                        </p>
                    )}
                    {attempt.observedProviderState === 'Succeeded' && attempt.status !== 'Captured' && (
                        <p className="notice">
                            Provider reported success; verified merchant settlement is still pending.
                        </p>
                    )}
                    {attempt.lastErrorCode && (
                        <span className="error-reference">{attempt.lastErrorCode}</span>
                    )}
                </article>
            ))}
            <h3>Verified captured funds</h3>
            {!value.captures.length && <p>No recorded captures on this page.</p>}
            {value.captures.map((capture) => (
                <article key={capture.id}>
                    <strong>
                        {formatUsd(capture.amount)} USD ·{' '}
                        {capture.allocated ? 'Applied to this order' : 'Not applied to this order'}
                    </strong>
                    {!capture.allocated && (
                        <p className="notice">
                            Late or surplus capture: funds were captured without paying this order. Full
                            compensation is tracked below.
                        </p>
                    )}
                </article>
            ))}
            <h3>Refunds</h3>
            {!value.refunds.length && <p>No refunds on this page.</p>}
            {value.refunds.map((refund) => (
                <article key={refund.id}>
                    <strong>
                        {formatUsd(refund.amount)} USD ·{' '}
                        {refund.status === 'Succeeded'
                            ? 'Refund confirmed'
                            : refund.status === 'NeedsReview'
                              ? 'Refund needs review'
                              : 'Refund pending'}
                    </strong>
                    <p>
                        {refund.status === 'Succeeded'
                            ? `Confirmed ${new Date(refund.succeededAt!).toLocaleString()}.`
                            : refund.status === 'NeedsReview'
                              ? 'Completion is unresolved and needs support review. Do not assume funds have been returned.'
                              : 'Refund intention recorded; completion has not been confirmed.'}
                    </p>
                </article>
            ))}
            <nav className="pagination" aria-label="Payment activity pages">
                <span>
                    Page {page} of {pages}
                </span>
                <div>
                    <button
                        className="button secondary small"
                        disabled={page <= 1 || fetching}
                        onClick={() => change(page - 1)}
                    >
                        Previous payment activity
                    </button>
                    <button
                        className="button secondary small"
                        disabled={page >= pages || fetching}
                        onClick={() => change(page + 1)}
                    >
                        Next payment activity
                    </button>
                </div>
            </nav>
        </section>
    );
}
