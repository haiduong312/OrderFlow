import { useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';
import { isProductId } from '../api/catalog';
import type { Page } from '../api/types';
import { useAdminQuery } from './api';
import type { Inventory, Movement } from './types';
import { integerInput } from './rules';
import {
    dateTime,
    Field,
    Paging,
    QueryPanel,
    Table,
    pageNumber,
    text,
    useAdminRecovery,
    useCommand,
} from './shared';

export function AdminStock() {
    const { id = '' } = useParams();
    return isProductId(id) ? (
        <StockContent id={id.toLowerCase()} />
    ) : (
        <p role="alert">Invalid product UUID.</p>
    );
}
function StockContent({ id }: { id: string }) {
    const [search, setSearch] = useSearchParams();
    const page = pageNumber(search);
    const inventory = useAdminQuery<Inventory>(`products/${id}/inventory`);
    const history = useAdminQuery<Page<Movement>>(`products/${id}/stock-history`, { page, pageSize: 20 });
    const write = useCommand<Movement>();
    const { command, error } = useAdminRecovery();
    const [kind, setKind] = useState('receipt');
    const [validation, setValidation] = useState('');
    const blocked = write.isPending || !!command || !!error;
    return (
        <>
            <div className="section-heading">
                <h2>Stock management</h2>
                <button
                    className="button secondary"
                    onClick={() => {
                        void inventory.refetch();
                        void history.refetch();
                    }}
                >
                    Refresh stock
                </button>
            </div>
            <Link className="text-link" to={`/admin/products/${id}`}>
                ← Product details
            </Link>
            <section className="card admin-panel">
                <QueryPanel query={inventory}>
                    {(row) => (
                        <>
                            <dl className="admin-metrics">
                                <div>
                                    <dt>On hand</dt>
                                    <dd data-testid="stock-on-hand">{row.onHand}</dd>
                                </div>
                                <div>
                                    <dt>Reserved</dt>
                                    <dd data-testid="stock-reserved">{row.reserved}</dd>
                                </div>
                                <div>
                                    <dt>Available</dt>
                                    <dd data-testid="stock-available">{row.available}</dd>
                                </div>
                                <div>
                                    <dt>Revision</dt>
                                    <dd>{row.revision}</dd>
                                </div>
                            </dl>
                            <p>
                                Server locks and validates stock. An adjustment cannot reduce OnHand below
                                Reserved.
                            </p>
                        </>
                    )}
                </QueryPanel>
            </section>
            <section className="card admin-panel">
                <h2>Receipt or adjustment</h2>
                {validation && (
                    <p className="field-error" role="alert">
                        {validation}
                    </p>
                )}
                <form
                    className="admin-form"
                    onSubmit={(event) => {
                        event.preventDefault();
                        const data = new FormData(event.currentTarget);
                        try {
                            const amount = integerInput(
                                text(data, 'amount'),
                                kind === 'receipt' ? 1 : -2_147_483_648,
                            );
                            if (!amount) throw new Error('Adjustment delta cannot be zero.');
                            const reason = text(data, 'reason');
                            if (!reason || reason.length > 500)
                                throw new Error('A reason of 1–500 characters is required.');
                            setValidation('');
                            write.mutate({
                                resource: `products/${id}/${kind === 'receipt' ? 'stock-receipts' : 'stock-adjustments'}`,
                                method: 'POST',
                                body: {
                                    operationId: crypto.randomUUID(),
                                    ...(kind === 'receipt' ? { quantity: amount } : { delta: amount }),
                                    reason,
                                },
                            });
                        } catch (failure) {
                            setValidation(
                                failure instanceof Error ? failure.message : 'Invalid stock input.',
                            );
                        }
                    }}
                >
                    <fieldset disabled={blocked}>
                        <Field id="stock-kind" label="Stock operation">
                            <select
                                id="stock-kind"
                                value={kind}
                                onChange={(event) => setKind(event.target.value)}
                            >
                                <option value="receipt">Receipt</option>
                                <option value="adjustment">Adjustment</option>
                            </select>
                        </Field>
                        <Field
                            id="stock-amount"
                            label={kind === 'receipt' ? 'Receipt quantity' : 'Adjustment delta'}
                            hint={
                                kind === 'receipt'
                                    ? 'Positive whole units.'
                                    : 'Signed whole units; zero is not an adjustment.'
                            }
                        >
                            <input id="stock-amount" name="amount" inputMode="numeric" required />
                        </Field>
                        <Field id="stock-reason" label="Stock reason">
                            <textarea id="stock-reason" name="reason" maxLength={500} required />
                        </Field>
                        <button className="button" type="submit">
                            {write.isPending ? 'Confirming…' : 'Submit stock operation'}
                        </button>
                    </fieldset>
                </form>
                {write.data && (
                    <p className="notice" role="status">
                        Server confirmed movement <code>{write.data.id}</code>, operation{' '}
                        <code>{write.data.operationId}</code>, revision {write.data.revision}. This is its
                        immutable result; current balance is shown above.
                    </p>
                )}
            </section>
            <section className="card">
                <h2 className="admin-table-title">Read-only stock movements</h2>
                <QueryPanel query={history}>
                    {(result) => (
                        <>
                            <MovementTable rows={result.items} />
                            <Paging value={result} change={(next) => setSearch({ page: String(next) })} />
                        </>
                    )}
                </QueryPanel>
            </section>
        </>
    );
}
export function MovementTable({ rows }: { rows: Movement[] }) {
    return (
        <Table
            rows={rows}
            id={(row) => row.id}
            caption="Immutable stock movement history"
            columns={[
                { label: 'Time', cell: (row) => dateTime(row.occurredAt) },
                { label: 'Kind / actor', cell: (row) => `${row.kind} · ${row.actorKind}` },
                { label: 'Reason', cell: (row) => row.reason },
                {
                    label: 'On hand',
                    cell: (row) => `${row.onHandBefore} → ${row.onHandAfter} (${row.onHandDelta})`,
                },
                {
                    label: 'Reserved',
                    cell: (row) => `${row.reservedBefore} → ${row.reservedAfter} (${row.reservedDelta})`,
                },
                { label: 'Operation', cell: (row) => <code>{row.operationId}</code> },
                { label: 'Revision', cell: (row) => row.revision },
            ]}
        />
    );
}
