import { useSearchParams } from 'react-router';
import type { OrderDetail } from '../api/types';
import { isProductId } from '../api/catalog';
import { useAdminQuery } from './api';
import type { Parameters } from './api';
import type { AuditPage, CaptureAudit, Movement, PaymentAudit, RefundAudit } from './types';
import { defaultRange, reportMoney, validRange } from './rules';
import { RangeFields } from './Reports';
import { MovementTable } from './Stock';
import { dateTime, Field, FilterForm, Paging, QueryPanel, Table, pageNumber, text } from './shared';

export function AdminAudit() {
    const [search, setSearch] = useSearchParams();
    const initial = defaultRange();
    const from = search.get('from') ?? initial.from;
    const to = search.get('to') ?? initial.to;
    const kind = search.get('kind') ?? 'history';
    const orderId = search.get('orderId') ?? '';
    const productId = search.get('productId') ?? '';
    const page = pageNumber(search);
    const stock = kind === 'stock';
    const valid =
        validRange(from, to) &&
        (stock ? !orderId || isProductId(orderId) : isProductId(orderId)) &&
        (!productId || isProductId(productId));
    const parameters = { from, to, page, pageSize: 20, ...(stock ? { orderId, productId } : {}) };
    const change = (next: number) => {
        const value = new URLSearchParams(search);
        value.set('page', String(next));
        setSearch(value);
    };
    return (
        <>
            <h2>Read-only audit history</h2>
            <p className="quiet-note">
                UTC [from, to), at most 366 days. State/stock history cannot be edited here. Attempts/refunds
                filter creation time, captures use recorded time; statuses are current at observation. No raw
                provider messages or secrets are shown.
            </p>
            <FilterForm
                key={search.toString()}
                submit={(data) =>
                    setSearch({
                        from: text(data, 'from'),
                        to: text(data, 'to'),
                        orderId: text(data, 'orderId').toLowerCase(),
                        productId: text(data, 'productId').toLowerCase(),
                        kind: text(data, 'kind'),
                        page: '1',
                    })
                }
            >
                <RangeFields from={from} to={to} />
                <Field id="audit-kind" label="Audit view">
                    <select id="audit-kind" name="kind" defaultValue={kind}>
                        <option value="history">Order history</option>
                        <option value="payment-attempts">Payment attempts</option>
                        <option value="captures">Captures</option>
                        <option value="refunds">Refunds</option>
                        <option value="stock">Stock movements</option>
                    </select>
                </Field>
                <Field id="audit-order" label="Order UUID (required except stock)">
                    <input id="audit-order" name="orderId" defaultValue={orderId} />
                </Field>
                <Field id="audit-product" label="Product UUID (stock filter only)">
                    <input id="audit-product" name="productId" defaultValue={productId} />
                </Field>
            </FilterForm>
            {!valid ? (
                <p className="notice" role="status">
                    Choose a valid UTC date range and an order UUID, or Stock movements with optional UUID
                    filters.
                </p>
            ) : (
                <AuditContent
                    key={`${kind}:${orderId}:${productId}:${from}:${to}:${page}`}
                    kind={kind}
                    orderId={orderId}
                    parameters={parameters}
                    change={change}
                />
            )}
        </>
    );
}
function AuditContent({
    kind,
    orderId,
    parameters,
    change,
}: {
    kind: string;
    orderId: string;
    parameters: Parameters;
    change: (page: number) => void;
}) {
    if (kind === 'stock') return <StockAudit parameters={parameters} change={change} />;
    if (kind === 'history')
        return (
            <HistoryAudit
                resource={`audit/orders/${orderId}/history`}
                parameters={parameters}
                change={change}
            />
        );
    if (kind === 'payment-attempts')
        return (
            <PaymentAuditView
                resource={`audit/orders/${orderId}/payment-attempts`}
                parameters={parameters}
                change={change}
            />
        );
    if (kind === 'captures')
        return (
            <CaptureAuditView
                resource={`audit/orders/${orderId}/captures`}
                parameters={parameters}
                change={change}
            />
        );
    if (kind === 'refunds')
        return (
            <RefundAuditView
                resource={`audit/orders/${orderId}/refunds`}
                parameters={parameters}
                change={change}
            />
        );
    return <p role="alert">Unsupported audit view.</p>;
}
type AuditProps = { resource: string; parameters: Parameters; change: (page: number) => void };
function HistoryAudit({ resource, parameters, change }: AuditProps) {
    const query = useAdminQuery<AuditPage<OrderDetail['history'][number]>>(resource, parameters);
    return (
        <section className="card">
            <QueryPanel query={query}>
                {(result) => (
                    <>
                        <AuditObservation
                            observedAt={result.observedAt}
                            refresh={() => {
                                void query.refetch();
                            }}
                        />
                        <Table
                            rows={result.items}
                            id={(row) => String(row.version)}
                            caption="Order state audit"
                            columns={[
                                { label: 'Version', cell: (row) => row.version },
                                {
                                    label: 'State',
                                    cell: (row) => `${row.fromStatus ?? 'Created'} → ${row.status}`,
                                },
                                { label: 'Actor', cell: (row) => row.actorKind },
                                { label: 'Reason', cell: (row) => row.reason },
                                { label: 'Time', cell: (row) => dateTime(row.occurredAt) },
                            ]}
                        />
                        <Paging value={result} change={change} />
                    </>
                )}
            </QueryPanel>
        </section>
    );
}
function PaymentAuditView({ resource, parameters, change }: AuditProps) {
    const query = useAdminQuery<AuditPage<PaymentAudit>>(resource, parameters);
    return (
        <section className="card">
            <QueryPanel query={query}>
                {(result) => (
                    <>
                        <AuditObservation
                            observedAt={result.observedAt}
                            refresh={() => {
                                void query.refetch();
                            }}
                        />
                        <Table
                            rows={result.items}
                            id={(row) => row.id}
                            caption="Payment attempt audit"
                            columns={[
                                { label: 'Attempt', cell: (row) => <code>{row.id}</code> },
                                { label: 'Status', cell: (row) => row.status },
                                { label: 'Amount USD', cell: (row) => reportMoney(row.amount) },
                                {
                                    label: 'Attention',
                                    cell: (row) => (row.needsAttention ? 'Needs review' : 'No'),
                                },
                                { label: 'Updated', cell: (row) => dateTime(row.updatedAt) },
                            ]}
                        />
                        <Paging value={result} change={change} />
                    </>
                )}
            </QueryPanel>
        </section>
    );
}
function CaptureAuditView({ resource, parameters, change }: AuditProps) {
    const query = useAdminQuery<AuditPage<CaptureAudit>>(resource, parameters);
    return (
        <section className="card">
            <QueryPanel query={query}>
                {(result) => (
                    <>
                        <AuditObservation
                            observedAt={result.observedAt}
                            refresh={() => {
                                void query.refetch();
                            }}
                        />
                        <Table
                            rows={result.items}
                            id={(row) => row.id}
                            caption="Capture audit"
                            columns={[
                                { label: 'Local capture', cell: (row) => <code>{row.id}</code> },
                                { label: 'Amount USD', cell: (row) => reportMoney(row.amount) },
                                {
                                    label: 'Allocation',
                                    cell: (row) =>
                                        row.allocated ? 'Applied to order' : 'Unallocated / compensation',
                                },
                                { label: 'Recorded', cell: (row) => dateTime(row.recordedAt) },
                            ]}
                        />
                        <Paging value={result} change={change} />
                    </>
                )}
            </QueryPanel>
        </section>
    );
}
function RefundAuditView({ resource, parameters, change }: AuditProps) {
    const query = useAdminQuery<AuditPage<RefundAudit>>(resource, parameters);
    return (
        <section className="card">
            <QueryPanel query={query}>
                {(result) => (
                    <>
                        <AuditObservation
                            observedAt={result.observedAt}
                            refresh={() => {
                                void query.refetch();
                            }}
                        />
                        <Table
                            rows={result.items}
                            id={(row) => row.id}
                            caption="Read-only order refund audit"
                            columns={[
                                { label: 'Local refund', cell: (row) => <code>{row.id}</code> },
                                { label: 'Status', cell: (row) => row.status },
                                { label: 'Amount USD', cell: (row) => reportMoney(row.amount) },
                                { label: 'Succeeded', cell: (row) => dateTime(row.succeededAt) },
                                { label: 'Updated', cell: (row) => dateTime(row.updatedAt) },
                            ]}
                        />
                        <Paging value={result} change={change} />
                    </>
                )}
            </QueryPanel>
        </section>
    );
}
function StockAudit({ parameters, change }: Omit<AuditProps, 'resource'>) {
    const query = useAdminQuery<AuditPage<Movement>>('audit/stock-movements', parameters);
    return (
        <section className="card">
            <QueryPanel query={query}>
                {(result) => (
                    <>
                        <AuditObservation
                            observedAt={result.observedAt}
                            refresh={() => {
                                void query.refetch();
                            }}
                        />
                        <MovementTable rows={result.items} />
                        <Paging value={result} change={change} />
                    </>
                )}
            </QueryPanel>
        </section>
    );
}

function AuditObservation({ observedAt, refresh }: { observedAt: string; refresh: () => void }) {
    return (
        <div className="admin-table-note action-row">
            <span>Observed {dateTime(observedAt)} · timestamps display in your device time zone.</span>
            <button className="button secondary small" onClick={refresh}>
                Refresh audit
            </button>
        </div>
    );
}
