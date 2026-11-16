import { useSearchParams } from 'react-router';
import { useAdminQuery } from './api';
import type { AuditPage, CountsReport, LowStock, SalesReport, TopReport } from './types';
import { defaultRange, reportMoney, validRange } from './rules';
import { dateTime, Field, FilterForm, Paging, QueryPanel, Table, pageNumber, text } from './shared';
export function RangeFields({ from, to }: { from: string; to: string }) {
    return (
        <>
            <Field id="range-from" label="From UTC date (inclusive)">
                <input id="range-from" name="from" type="date" required defaultValue={from} />
            </Field>
            <Field id="range-to" label="To UTC date (exclusive)">
                <input id="range-to" name="to" type="date" required defaultValue={to} />
            </Field>
        </>
    );
}
export function AdminReports() {
    const [search, setSearch] = useSearchParams();
    const initial = defaultRange();
    const from = search.get('from') ?? initial.from;
    const to = search.get('to') ?? initial.to;
    const limit = Number(search.get('limit') ?? 10);
    const threshold = Number(search.get('threshold') ?? 5);
    const page = pageNumber(search);
    const valid =
        validRange(from, to) &&
        Number.isInteger(limit) &&
        limit >= 1 &&
        limit <= 100 &&
        Number.isInteger(threshold) &&
        threshold >= 0 &&
        threshold <= 2_147_483_647;
    const sales = useAdminQuery<SalesReport>('reports/sales', { from, to }, valid);
    const counts = useAdminQuery<CountsReport>('reports/order-counts', { from, to }, valid);
    const top = useAdminQuery<TopReport>('reports/top-products', { from, to, limit }, valid);
    const low = useAdminQuery<AuditPage<LowStock>>(
        'reports/low-stock',
        { threshold, page, pageSize: 20 },
        valid,
    );
    return (
        <>
            <div className="section-heading">
                <h2>Sales and stock reports</h2>
                <button
                    className="button secondary"
                    disabled={!valid || [sales, counts, top, low].some((query) => query.isFetching)}
                    onClick={() => {
                        for (const query of [sales, counts, top, low]) void query.refetch();
                    }}
                >
                    Refresh reports
                </button>
            </div>
            <p className="quiet-note">
                UTC dates use [from, to), maximum 366 days. Display timestamps use your device time zone. A
                Vietnam display day starts at 07:00 for a UTC midnight boundary; these filters do not mean
                local calendar days.
            </p>
            <FilterForm
                key={search.toString()}
                submit={(data) =>
                    setSearch({
                        from: text(data, 'from'),
                        to: text(data, 'to'),
                        limit: text(data, 'limit'),
                        threshold: text(data, 'threshold'),
                        page: '1',
                    })
                }
            >
                <RangeFields from={from} to={to} />
                <Field id="top-limit" label="Top product limit">
                    <input
                        id="top-limit"
                        name="limit"
                        type="number"
                        min={1}
                        max={100}
                        required
                        defaultValue={limit}
                    />
                </Field>
                <Field id="low-threshold" label="Low-stock threshold">
                    <input
                        id="low-threshold"
                        name="threshold"
                        type="number"
                        min={0}
                        max={2147483647}
                        required
                        defaultValue={threshold}
                    />
                </Field>
            </FilterForm>
            {!valid ? (
                <p className="error-panel" role="alert">
                    Use valid UTC dates from &lt; to, at most 366 days, and bounded whole-number limits.
                </p>
            ) : (
                <>
                    <section className="card admin-panel">
                        <h2>Paid sales and cash movements</h2>
                        <p>
                            Paid sales uses winning captured orders and immutable item totals. Captured funds
                            includes late/surplus captures. Refunds count only Succeeded; net is captured
                            minus succeeded refunds. Created or Completed orders alone do not prove payment.
                        </p>
                        <QueryPanel query={sales}>
                            {(result) => (
                                <>
                                    <p className="quiet-note">
                                        Observed {dateTime(result.observedAt)} · separate reports may observe
                                        different snapshots.
                                    </p>
                                    <dl className="admin-metrics">
                                        <div>
                                            <dt>Paid sales USD</dt>
                                            <dd>{reportMoney(result.totals.paidSales)}</dd>
                                        </div>
                                        <div>
                                            <dt>Captured funds USD</dt>
                                            <dd>{reportMoney(result.totals.capturedRevenue)}</dd>
                                        </div>
                                        <div>
                                            <dt>Succeeded refunds USD</dt>
                                            <dd>{reportMoney(result.totals.refunds)}</dd>
                                        </div>
                                        <div>
                                            <dt>Net captured USD</dt>
                                            <dd>{reportMoney(result.totals.netRevenue)}</dd>
                                        </div>
                                    </dl>
                                    <Table
                                        rows={result.days}
                                        id={(row) => row.date}
                                        caption="Paid sales and captured/refunded funds by UTC day"
                                        columns={[
                                            { label: 'UTC date', cell: (row) => row.date },
                                            {
                                                label: 'Paid orders',
                                                cell: (row) => row.amounts.paidOrderCount,
                                            },
                                            {
                                                label: 'Paid sales',
                                                cell: (row) => reportMoney(row.amounts.paidSales),
                                            },
                                            {
                                                label: 'Captured',
                                                cell: (row) => reportMoney(row.amounts.capturedRevenue),
                                            },
                                            {
                                                label: 'Refunded',
                                                cell: (row) => reportMoney(row.amounts.refunds),
                                            },
                                            {
                                                label: 'Net',
                                                cell: (row) => reportMoney(row.amounts.netRevenue),
                                            },
                                        ]}
                                    />
                                </>
                            )}
                        </QueryPanel>
                    </section>
                    <section className="card">
                        <h2 className="admin-table-title">
                            Current status of orders created in the UTC range
                        </h2>
                        <QueryPanel query={counts}>
                            {(result) => (
                                <>
                                    <p className="admin-table-note">Observed {dateTime(result.observedAt)}</p>
                                    <Table
                                        rows={result.counts}
                                        id={(row) => row.status}
                                        caption="Order counts by current state"
                                        columns={[
                                            { label: 'State', cell: (row) => row.status },
                                            { label: 'Orders', cell: (row) => row.count },
                                        ]}
                                    />
                                </>
                            )}
                        </QueryPanel>
                    </section>
                    <section className="card">
                        <h2 className="admin-table-title">Top-selling products</h2>
                        <QueryPanel query={top}>
                            {(result) => (
                                <>
                                    <p className="admin-table-note">Observed {dateTime(result.observedAt)}</p>
                                    <Table
                                        rows={result.items}
                                        id={(row) => row.productId}
                                        caption="Paid product quantity and historical item value"
                                        columns={[
                                            { label: 'Product', cell: (row) => row.name },
                                            { label: 'SKU', cell: (row) => row.sku },
                                            { label: 'Paid units', cell: (row) => row.units },
                                            {
                                                label: 'Paid sales USD',
                                                cell: (row) => reportMoney(row.paidSales),
                                            },
                                        ]}
                                    />
                                </>
                            )}
                        </QueryPanel>
                    </section>
                    <section className="card">
                        <h2 className="admin-table-title">Current low available stock</h2>
                        <p className="admin-table-note">
                            Active catalog only; Available = OnHand − Reserved, at or below threshold. This
                            view is current, independent of the date range.
                        </p>
                        <QueryPanel query={low}>
                            {(result) => (
                                <>
                                    <p className="admin-table-note">Observed {dateTime(result.observedAt)}</p>
                                    <Table
                                        rows={result.items}
                                        id={(row) => row.productId}
                                        caption="Current low available stock"
                                        columns={[
                                            { label: 'Product', cell: (row) => row.name },
                                            { label: 'On hand', cell: (row) => row.onHand },
                                            { label: 'Reserved', cell: (row) => row.reserved },
                                            { label: 'Available', cell: (row) => row.available },
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
            )}
        </>
    );
}
