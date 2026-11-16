import { useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import type { OrderDetail, OrderStatus, OrderSummary, Page } from '../api/types';
import { isProductId } from '../api/catalog';
import { formatUsd } from '../api/money';
import { useAdminQuery } from './api';
import { allowedFulfillment } from './rules';
import {
    dateTime,
    Field,
    FilterForm,
    Paging,
    QueryPanel,
    Table,
    pageNumber,
    text,
    useAdminRecovery,
    useCommand,
} from './shared';
const states: OrderStatus[] = [
    'PendingPayment',
    'Confirmed',
    'Processing',
    'Shipped',
    'Completed',
    'Cancelled',
    'Expired',
];
export function AdminOrders() {
    const [search, setSearch] = useSearchParams();
    const page = pageNumber(search);
    const navigate = useNavigate();
    const [invalid, setInvalid] = useState('');
    const parameters = {
        page,
        pageSize: 20,
        customerId: search.get('customerId') ?? '',
        status: search.get('status') ?? '',
    };
    const orders = useAdminQuery<Page<OrderSummary>>('orders', parameters);
    return (
        <>
            <h2>Find and manage orders</h2>
            <form
                className="admin-filters card"
                onSubmit={(event) => {
                    event.preventDefault();
                    const id = text(new FormData(event.currentTarget), 'id');
                    if (isProductId(id)) {
                        setInvalid('');
                        navigate(`/admin/orders/${id.toLowerCase()}`);
                    } else setInvalid('Enter an exact order UUID.');
                }}
            >
                <Field id="order-lookup" label="Exact order UUID">
                    <input id="order-lookup" name="id" required />
                </Field>
                <button className="button secondary">Open order</button>
                {invalid && <p role="alert">{invalid}</p>}
            </form>
            <FilterForm
                key={search.toString()}
                submit={(data) =>
                    setSearch({
                        customerId: text(data, 'customerId'),
                        status: text(data, 'status'),
                        page: '1',
                    })
                }
            >
                <Field id="order-customer" label="Customer UUID (optional)">
                    <input id="order-customer" name="customerId" defaultValue={parameters.customerId} />
                </Field>
                <Field id="admin-order-status" label="Order status">
                    <select id="admin-order-status" name="status" defaultValue={parameters.status}>
                        <option value="">All states</option>
                        {states.map((state) => (
                            <option key={state}>{state}</option>
                        ))}
                    </select>
                </Field>
            </FilterForm>
            <section className="card">
                <QueryPanel query={orders}>
                    {(result) => (
                        <>
                            <Table
                                rows={result.items}
                                id={(row) => row.id}
                                caption="Admin order search results"
                                columns={[
                                    {
                                        label: 'Order',
                                        cell: (row) => (
                                            <Link className="text-link" to={`/admin/orders/${row.id}`}>
                                                <code>{row.id}</code>
                                            </Link>
                                        ),
                                    },
                                    { label: 'Customer', cell: (row) => <code>{row.customerId}</code> },
                                    { label: 'State', cell: (row) => row.status },
                                    { label: 'Total USD', cell: (row) => formatUsd(row.total) },
                                    { label: 'Created', cell: (row) => dateTime(row.createdAt) },
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
export function AdminOrder() {
    const { id = '' } = useParams();
    return isProductId(id) ? <OrderContent id={id.toLowerCase()} /> : <p role="alert">Invalid order UUID.</p>;
}
function OrderContent({ id }: { id: string }) {
    const query = useAdminQuery<OrderDetail>(`orders/${id}`);
    return (
        <>
            <div className="section-heading">
                <h2>Order details</h2>
                <button
                    className="button secondary"
                    onClick={() => {
                        void query.refetch();
                    }}
                >
                    Reload Admin order
                </button>
            </div>
            <QueryPanel query={query}>
                {(row) => <OrderEditor key={`${row.id}:${row.version}`} order={row} />}
            </QueryPanel>
        </>
    );
}
function OrderEditor({ order }: { order: OrderDetail }) {
    const write = useCommand<OrderDetail>();
    const { command, error } = useAdminRecovery();
    const target = allowedFulfillment(order.status);
    return (
        <>
            <section className="card admin-panel">
                <p>
                    <code>{order.id}</code>
                </p>
                <span className="badge" data-testid="admin-order-state">
                    {order.status}
                </span>
                <h2>Authoritative total: {formatUsd(order.total)} USD</h2>
                <p>
                    Version {order.version} · Customer <code>{order.customerId}</code> · Server observed{' '}
                    {dateTime(order.expiry.observedAt)}.
                </p>
                <p>
                    Only verified settlement confirms payment. Admin fulfillment cannot mark PendingPayment as
                    paid or skip states.
                </p>
                {target ? (
                    <form
                        className="admin-form"
                        onSubmit={(event) => {
                            event.preventDefault();
                            write.mutate({
                                resource: `orders/${order.id}/transitions`,
                                method: 'POST',
                                body: {
                                    targetStatus: target,
                                    expectedVersion: order.version,
                                    reason: text(new FormData(event.currentTarget), 'reason'),
                                },
                            });
                        }}
                    >
                        <fieldset disabled={write.isPending || !!command || !!error}>
                            <Field id="transition-reason" label="Fulfillment reason (optional)">
                                <textarea id="transition-reason" name="reason" maxLength={500} />
                            </Field>
                            <button className="button">Advance to {target}</button>
                        </fieldset>
                    </form>
                ) : (
                    <p className="notice">No Admin fulfillment transition is allowed from {order.status}.</p>
                )}
                <Link className="text-link" to={`/admin/audit?orderId=${order.id}&kind=history`}>
                    Open financial and history audit
                </Link>
            </section>
            <section className="card">
                <h2 className="admin-table-title">Immutable order items</h2>
                <Table
                    rows={order.items}
                    id={(row) => row.productId}
                    caption="Order item snapshots"
                    columns={[
                        { label: 'Item', cell: (row) => row.name },
                        { label: 'SKU', cell: (row) => row.sku },
                        { label: 'Unit USD', cell: (row) => formatUsd(row.unitPrice) },
                        { label: 'Quantity', cell: (row) => row.quantity },
                        { label: 'Line USD', cell: (row) => formatUsd(row.lineTotal) },
                    ]}
                />
            </section>
            <section className="card">
                <h2 className="admin-table-title">Read-only state history</h2>
                <Table
                    rows={order.history}
                    id={(row) => String(row.version)}
                    caption="Immutable order state history"
                    columns={[
                        { label: 'Version', cell: (row) => row.version },
                        {
                            label: 'Transition',
                            cell: (row) => `${row.fromStatus ?? 'Created'} → ${row.status}`,
                        },
                        { label: 'Actor', cell: (row) => row.actorKind },
                        { label: 'Time', cell: (row) => dateTime(row.occurredAt) },
                        { label: 'Reason', cell: (row) => row.reason },
                    ]}
                />
            </section>
        </>
    );
}
