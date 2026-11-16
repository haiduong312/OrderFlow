import { useQuery } from '@tanstack/react-query';
import { Link, useSearchParams } from 'react-router';
import { useSession } from '../auth/context';
import { ordersOptions } from '../api/queries';
import type { OrderStatus } from '../api/types';
import { formatUsd } from '../api/money';
import { ErrorPanel, Icon, Loading, PageTitle } from '../components/ui';

const statuses: OrderStatus[] = [
    'PendingPayment',
    'Confirmed',
    'Processing',
    'Shipped',
    'Completed',
    'Cancelled',
    'Expired',
];
const labels: Record<OrderStatus, string> = {
    PendingPayment: 'Awaiting payment',
    Confirmed: 'Confirmed',
    Processing: 'Processing',
    Shipped: 'Shipped',
    Completed: 'Completed',
    Cancelled: 'Cancelled',
    Expired: 'Expired',
};

export function Orders() {
    const { session } = useSession();
    const [search, setSearch] = useSearchParams();
    const pageValue = search.get('page') ?? '1';
    const page = Number(pageValue);
    const statusValue = search.get('status');
    if (
        !/^[1-9][0-9]*$/.test(pageValue) ||
        !Number.isSafeInteger(page) ||
        (page - 1) * 20 > 2147483647 ||
        (statusValue !== null && !statuses.includes(statusValue as OrderStatus))
    ) {
        return (
            <div className="state-box" role="alert">
                <h1>That order view isn’t valid.</h1>
                <p>Use a valid page and order status.</p>
                <Link className="button secondary" to="/orders">
                    Reset filters
                </Link>
            </div>
        );
    }
    if (session.phase !== 'authenticated') return null;
    return (
        <OrderList
            userId={session.user.id}
            page={page}
            status={statusValue as OrderStatus | null}
            change={(next, status) => setSearch({ page: String(next), ...(status ? { status } : {}) })}
        />
    );
}

function OrderList({
    userId,
    page,
    status,
    change,
}: {
    userId: string;
    page: number;
    status: OrderStatus | null;
    change: (page: number, status: OrderStatus | null) => void;
}) {
    const { controller } = useSession();
    const orders = useQuery(
        ordersOptions(controller.api, controller, userId, {
            page,
            pageSize: 20,
            status: status ?? undefined,
        }),
    );
    return (
        <>
            <PageTitle
                eyebrow="YOUR ORDERS"
                title="Orders, at a glance."
                description="Your latest order states and confirmed totals, straight from OrderFlow."
                action={
                    <button
                        className="button secondary"
                        onClick={() => {
                            void orders.refetch();
                        }}
                    >
                        Refresh orders
                    </button>
                }
            />
            <section className="orders-card card">
                {orders.isFetching && !orders.isPending && (
                    <p className="notice" role="status">
                        Refreshing your orders…
                    </p>
                )}
                <div className="orders-toolbar">
                    <div>
                        <h2>My orders</h2>
                        <p>
                            {orders.data
                                ? `${orders.data.totalCount} ${orders.data.totalCount === 1 ? 'order' : 'orders'}`
                                : 'Your account’s order history'}
                        </p>
                    </div>
                    <div className="filter">
                        <label htmlFor="status-filter">Status</label>
                        <select
                            id="status-filter"
                            value={status ?? ''}
                            onChange={(event) => change(1, (event.target.value as OrderStatus) || null)}
                        >
                            <option value="">All statuses</option>
                            {statuses.map((value) => (
                                <option key={value} value={value}>
                                    {labels[value]}
                                </option>
                            ))}
                        </select>
                    </div>
                </div>
                {orders.isPending ? (
                    <Loading text="Loading your orders…" />
                ) : orders.isError ? (
                    <ErrorPanel
                        title="Orders could not be loaded"
                        error={orders.error}
                        retry={() => {
                            void orders.refetch();
                        }}
                    />
                ) : orders.data.items.length === 0 ? (
                    <div className="state-box">
                        <span className="empty-icon">
                            <Icon name="orders" size={32} />
                        </span>
                        <h3>{orders.data.totalCount ? 'No orders on this page.' : 'No orders here yet.'}</h3>
                        <p>
                            {status
                                ? 'There are no orders matching this status.'
                                : 'When you place an order, you can follow its status here.'}
                        </p>
                        <Link className="text-link" to="/">
                            Back to overview
                            <Icon name="arrow" size={16} />
                        </Link>
                    </div>
                ) : (
                    <div className="table-scroll">
                        <table>
                            <caption className="sr-only">Orders for the currently signed-in account</caption>
                            <thead>
                                <tr>
                                    <th scope="col">Order</th>
                                    <th scope="col">Created</th>
                                    <th scope="col">Status</th>
                                    <th scope="col" className="align-right">
                                        Total
                                    </th>
                                </tr>
                            </thead>
                            <tbody>
                                {orders.data.items.map((order) => (
                                    <tr key={order.id}>
                                        <td data-label="Order">
                                            <Link className="text-link" to={`/orders/${order.id}`}>
                                                <code>{order.id}</code>
                                            </Link>
                                        </td>
                                        <td data-label="Created">
                                            {new Date(order.createdAt).toLocaleDateString('en-US', {
                                                month: 'short',
                                                day: 'numeric',
                                                year: 'numeric',
                                            })}
                                        </td>
                                        <td data-label="Status">
                                            <span
                                                className={`badge ${['Confirmed', 'Completed'].includes(order.status) ? 'success' : order.status === 'PendingPayment' ? 'warning' : ''}`}
                                            >
                                                {labels[order.expiry.isExpired ? 'Expired' : order.status]}
                                            </span>
                                            {order.expiry.isExpired && order.status === 'PendingPayment' && (
                                                <small className="muted">Awaiting expiry processing</small>
                                            )}
                                        </td>
                                        <td data-label="Total" className="align-right total">
                                            {formatUsd(order.total)}{' '}
                                            <span className="muted">{order.currency}</span>
                                        </td>
                                    </tr>
                                ))}
                            </tbody>
                        </table>
                    </div>
                )}
                {orders.data && (
                    <div className="pagination">
                        <span>
                            Page {page} of {Math.max(1, Math.ceil(orders.data.totalCount / 20))}
                        </span>
                        <div>
                            <button
                                className="button secondary small"
                                disabled={page <= 1}
                                onClick={() => change(page - 1, status)}
                            >
                                Previous
                            </button>
                            <button
                                className="button secondary small"
                                disabled={page * 20 >= orders.data.totalCount}
                                onClick={() => change(page + 1, status)}
                            >
                                Next
                            </button>
                        </div>
                    </div>
                )}
            </section>
            <p className="quiet-note">
                Dates are shown in your device’s time zone. Totals and order permissions are confirmed by the
                server.
            </p>
        </>
    );
}
