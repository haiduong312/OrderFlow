import { useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router';
import { useSession } from '../auth/context';
import { notificationOptions, unreadOptions } from '../api/orders';
import { ErrorPanel, Loading, PageTitle } from '../components/ui';

const labels: Record<string, string> = {
    'ordering.order-created': 'Order created',
    'ordering.order-confirmed': 'Payment confirmed',
    'ordering.order-cancelled': 'Order cancelled',
    'ordering.order-expired': 'Order expired',
    'ordering.order-processing': 'Order processing',
    'ordering.order-shipped': 'Order shipped',
    'ordering.order-completed': 'Order completed',
};
export function Notifications() {
    const { controller, session } = useSession();
    const userId = session.phase === 'authenticated' ? session.user.id : '';
    const [search, setSearch] = useSearchParams();
    const raw = Number(search.get('page') ?? '1');
    const page = Number.isInteger(raw) && raw > 0 && raw <= 100_000 ? raw : 1;
    const unreadOnly = search.get('unreadOnly') === 'true';
    const list = useQuery(notificationOptions(controller.api, controller, userId, page, 20, unreadOnly));
    const unread = useQuery(unreadOptions(controller.api, controller, userId));
    const [busy, setBusy] = useState<string | null>(null);
    const inFlight = useRef(false);
    const [error, setError] = useState<unknown>(null);
    async function read(id: string) {
        if (inFlight.current) return;
        inFlight.current = true;
        setBusy(id);
        setError(null);
        try {
            await controller.guard(userId, () =>
                controller.api.request(`/api/notifications/${id}/read`, {
                    method: 'POST',
                    body: {},
                    timeoutMs: 15_000,
                }),
            );
            await controller.queries.invalidateQueries({ queryKey: ['private', userId, 'notifications'] });
            await controller.queries.invalidateQueries({ queryKey: ['private', userId, 'unread'] });
        } catch (failure) {
            setError(failure);
        } finally {
            inFlight.current = false;
            setBusy(null);
        }
    }
    const change = (next: number, only = unreadOnly) =>
        setSearch({ page: String(next), unreadOnly: String(only) });
    return (
        <>
            <PageTitle
                eyebrow="DURABLE UPDATES"
                title="Your notifications."
                description="Updates remain available here after reconnecting, even when a live hint is lost."
                action={
                    <button
                        className="button secondary"
                        onClick={() => {
                            void list.refetch();
                            void unread.refetch();
                        }}
                    >
                        Refresh notifications
                    </button>
                }
            />
            <div className="orders-toolbar card">
                <p>{unread.isSuccess ? `${unread.data.count} unread` : 'Unread count unavailable'}</p>
                <label className="check-label">
                    <input
                        type="checkbox"
                        checked={unreadOnly}
                        onChange={(event) => change(1, event.target.checked)}
                    />
                    Unread only
                </label>
            </div>
            {error != null && <ErrorPanel error={error} title="Read status could not be confirmed" />}
            {list.isPending ? (
                <Loading text="Loading notifications…" />
            ) : list.isError ? (
                <ErrorPanel
                    error={list.error}
                    title="Notifications could not be loaded"
                    retry={() => {
                        void list.refetch();
                    }}
                />
            ) : (
                <section className="card notification-list" aria-busy={list.isFetching}>
                    {list.isFetching && <p role="status">Refreshing notifications…</p>}
                    {!list.data.items.length && (
                        <div className="state-box">
                            <h2>No notifications here.</h2>
                            <p>New server updates appear after an order event is processed.</p>
                        </div>
                    )}
                    {list.data.items.map((notification) => (
                        <article key={notification.id} className={notification.readAt ? '' : 'unread'}>
                            <div>
                                <span className="badge">{notification.readAt ? 'Read' : 'Unread'}</span>
                                <h2>{labels[notification.type] ?? 'Order update'}</h2>
                                <p>
                                    {new Date(notification.createdAt).toLocaleString()} · your device time
                                    zone
                                </p>
                                <Link className="text-link" to={`/orders/${notification.orderId}`}>
                                    View order <span className="sr-only">{notification.orderId}</span>
                                </Link>
                            </div>
                            {!notification.readAt && (
                                <button
                                    className="button secondary small"
                                    disabled={busy !== null}
                                    aria-label={`Mark notification ${notification.id} as read`}
                                    onClick={() => {
                                        void read(notification.id);
                                    }}
                                >
                                    {busy === notification.id ? 'Confirming…' : 'Mark as read'}
                                </button>
                            )}
                        </article>
                    ))}
                    <nav className="pagination" aria-label="Notification pages">
                        <span>Page {page}</span>
                        <div>
                            <button
                                className="button secondary small"
                                disabled={page <= 1 || list.isFetching}
                                onClick={() => change(page - 1)}
                            >
                                Previous notifications
                            </button>
                            <button
                                className="button secondary small"
                                disabled={page * 20 >= list.data.totalCount || list.isFetching}
                                onClick={() => change(page + 1)}
                            >
                                Next notifications
                            </button>
                        </div>
                    </nav>
                </section>
            )}
        </>
    );
}
