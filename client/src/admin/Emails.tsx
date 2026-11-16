import { useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import type { Page } from '../api/types';
import { useAdminQuery } from './api';
import type { EmailFailure } from './types';
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
export function AdminEmails() {
    const [search, setSearch] = useSearchParams();
    const page = pageNumber(search);
    const query = useAdminQuery<Page<EmailFailure>>('email-deliveries', { page, pageSize: 20 });
    const [selected, setSelected] = useState<EmailFailure | null>(null);
    return (
        <>
            <div className="section-heading">
                <h2>Email failures</h2>
                <button
                    className="button secondary"
                    onClick={() => {
                        void query.refetch();
                    }}
                >
                    Refresh email failures
                </button>
            </div>
            <p className="quiet-note">
                Review failed email deliveries and eligible retries. For payment and refund status, open the
                order's read-only financial audit.
            </p>
            <section className="card">
                <QueryPanel query={query}>
                    {(result) => (
                        <>
                            <Table
                                rows={result.items}
                                id={(row) => row.id}
                                caption="Email failures with safe metadata only"
                                columns={[
                                    { label: 'Delivery', cell: (row) => <code>{row.id}</code> },
                                    {
                                        label: 'Order',
                                        cell: (row) => (
                                            <Link className="text-link" to={`/admin/orders/${row.orderId}`}>
                                                {row.orderId}
                                            </Link>
                                        ),
                                    },
                                    {
                                        label: 'Status / code',
                                        cell: (row) => (
                                            <>
                                                {row.status}
                                                <small>{row.lastErrorCode ?? '—'}</small>
                                            </>
                                        ),
                                    },
                                    {
                                        label: 'Attempts',
                                        cell: (row) =>
                                            `${row.attemptCount} total · ${row.reviewRetryCount} audited retries`,
                                    },
                                    {
                                        label: 'Outcome',
                                        cell: (row) =>
                                            row.outcomeUncertain
                                                ? 'Uncertain; duplicate risk'
                                                : 'Known failure',
                                    },
                                    { label: 'Last failure', cell: (row) => dateTime(row.lastFailureAt) },
                                    {
                                        label: 'Action',
                                        cell: (row) =>
                                            row.status === 'NeedsReview' ? (
                                                <button
                                                    className="button secondary small"
                                                    onClick={() => setSelected(row)}
                                                >
                                                    Review email retry
                                                </button>
                                            ) : (
                                                'No retry allowed'
                                            ),
                                    },
                                ]}
                            />
                            <Paging value={result} change={(next) => setSearch({ page: String(next) })} />
                        </>
                    )}
                </QueryPanel>
            </section>
            {selected && <RetryEmail key={selected.id} delivery={selected} close={() => setSelected(null)} />}
        </>
    );
}
function RetryEmail({ delivery, close }: { delivery: EmailFailure; close: () => void }) {
    const write = useCommand<EmailFailure>(close);
    const { command, error } = useAdminRecovery();
    return (
        <section className="card admin-panel">
            <h2>Audit and retry the original email</h2>
            <p>
                Delivery <code>{delivery.id}</code>. The server retains its original provider identity and
                lifetime attempt count. This schedules eligible work; it does not confirm that mail was sent.
            </p>
            {delivery.outcomeUncertain && (
                <p className="notice">
                    The prior provider outcome is uncertain. Retrying can send duplicate mail; explicit
                    acknowledgement is required.
                </p>
            )}
            <form
                className="admin-form"
                onSubmit={(event) => {
                    event.preventDefault();
                    const data = new FormData(event.currentTarget);
                    write.mutate({
                        resource: `email-deliveries/${delivery.id}/retry`,
                        method: 'POST',
                        body: {
                            operationId: crypto.randomUUID(),
                            reason: text(data, 'reason'),
                            acknowledgeDuplicateRisk: data.get('ack') === 'on',
                        },
                    });
                }}
            >
                <fieldset disabled={write.isPending || !!command || !!error}>
                    <Field id="email-retry-reason" label="Email retry reason">
                        <textarea id="email-retry-reason" name="reason" maxLength={300} required />
                    </Field>
                    <label className="check-label">
                        <input type="checkbox" name="ack" required={delivery.outcomeUncertain} />I acknowledge
                        duplicate-mail risk for an uncertain outcome.
                    </label>
                    <button className="button" type="submit">
                        Schedule audited email retry
                    </button>
                </fieldset>
            </form>
            <button className="text-button" onClick={close}>
                Close retry editor
            </button>
        </section>
    );
}
