import { createContext, useContext, useEffect, useMemo, useState, useSyncExternalStore } from 'react';
import type { FormEvent, PropsWithChildren, ReactNode } from 'react';
import type { UseQueryResult } from '@tanstack/react-query';
import { Link, NavLink, Outlet } from 'react-router';
import { useSession } from '../auth/context';
import { ErrorPanel, Loading, PageTitle } from '../components/ui';
import { AdminRecovery, adminRecoveryKey, replayable } from './commands';
import type { AdminCommand } from './commands';
import { invalidateAdmin, useAdminMutation } from './api';
import type { Page } from '../api/types';
import { ApiError } from '../api/client';

const Recovery = createContext<AdminRecovery | null>(null);
const Feedback = createContext<
    { start: () => void; failure: (error: unknown) => void; success: () => void } | undefined
>(undefined);
export function useAdmin() {
    const { controller, session } = useSession();
    if (session.phase !== 'authenticated') throw new Error('An authenticated Admin session is required.');
    return { controller, userId: session.user.id };
}
export function useAdminRecovery() {
    const recovery = useContext(Recovery);
    if (!recovery) throw new Error('Admin context required.');
    const state = useSyncExternalStore(recovery.subscribe, recovery.getSnapshot);
    return { recovery, ...state };
}
export function AdminLayout() {
    const { userId } = useAdmin();
    return <AdminWorkspace key={userId} userId={userId} />;
}
function AdminWorkspace({ userId }: { userId: string }) {
    const recovery = useMemo(() => new AdminRecovery(userId), [userId]);
    const [lastError, setLastError] = useState<unknown>(null);
    const [confirmed, setConfirmed] = useState(false);
    const feedback = useMemo(
        () => ({
            start: () => {
                setLastError(null);
                setConfirmed(false);
            },
            failure: (error: unknown) => setLastError(error),
            success: () => setConfirmed(true),
        }),
        [],
    );
    useEffect(() => {
        const sync = (event: StorageEvent) => {
            if (event.key === adminRecoveryKey(userId) || event.key === null) recovery.reload();
        };
        window.addEventListener('storage', sync);
        return () => window.removeEventListener('storage', sync);
    }, [recovery, userId]);
    return (
        <Recovery.Provider value={recovery}>
            <Feedback.Provider value={feedback}>
                <PageTitle
                    eyebrow="ADMIN WORKSPACE"
                    title="Manage with confidence."
                    description="Catalog, stock, fulfillment and financial reads. Every action is authorized and validated by the server."
                />
                <nav className="admin-nav" aria-label="Admin navigation">
                    <NavLink to="/admin/products">Products</NavLink>
                    <NavLink to="/admin/categories">Categories</NavLink>
                    <NavLink to="/admin/orders">Orders</NavLink>
                    <NavLink to="/admin/reports">Reports</NavLink>
                    <NavLink to="/admin/audit">Audit</NavLink>
                    <NavLink to="/admin/emails">Email failures</NavLink>
                </nav>
                <RecoveryBanner />
                <WriteNotice error={lastError} />
                {confirmed && (
                    <p className="notice" role="status">
                        Server confirmed the Admin operation. Affected views are refreshed from the server.
                    </p>
                )}
                <Outlet />
            </Feedback.Provider>
        </Recovery.Provider>
    );
}
function RecoveryBanner() {
    const { controller, userId } = useAdmin();
    const { recovery, command, error, running } = useAdminRecovery();
    const feedback = useContext(Feedback);
    const [ack, setAck] = useState(false);
    const retry = useAdminMutation(
        (value: AdminCommand) =>
            recovery.execute(
                value,
                (saved) =>
                    controller.guard(userId, () =>
                        controller.api.request(`/api/admin/${saved.resource}`, {
                            method: saved.method,
                            body: saved.body,
                            timeoutMs: 15_000,
                        }),
                    ),
                true,
            ),
        (value) => value.resource,
        feedback,
    );
    const close = useAdminMutation(
        () => recovery.close(),
        () => command?.resource ?? 'email-deliveries',
    );
    if (!command && !error) return null;
    return (
        <section className="card admin-recovery" aria-label="Admin operation recovery">
            <h2>{running ? 'Waiting for server confirmation' : 'Resolve the previous Admin operation'}</h2>
            <p>
                {running
                    ? 'This operation is in progress. Keep its saved identity until the server outcome is confirmed.'
                    : (error ??
                      'The outcome is not confirmed. Other Admin writes are blocked until this operation is recovered or explicitly closed.')}
            </p>
            {command && (
                <>
                    <p>
                        {command.method} <code>{command.resource}</code>
                    </p>
                    <p>
                        Operation identity:{' '}
                        <code>
                            {String(
                                command.body.operationId ??
                                    'Versioned/catalog request — inspect server state',
                            )}
                        </code>
                    </p>
                    <p>Reason: {String(command.body.reason ?? 'Catalog management')}</p>
                    {command.body.name && <p>Submitted name: {String(command.body.name)}</p>}
                    {command.body.sku && <p>Submitted SKU: {String(command.body.sku)}</p>}
                    {command.body.expectedVersion && (
                        <p>Submitted version: {String(command.body.expectedVersion)}</p>
                    )}
                    <div className="action-row">
                        {replayable(command) && (
                            <button
                                className="button"
                                disabled={running || retry.isPending || close.isPending}
                                onClick={() => retry.mutate(command)}
                            >
                                Recover same stock/email operation
                            </button>
                        )}
                        <button
                            className="button secondary"
                            onClick={() => {
                                void invalidateAdmin(controller.queries, userId, command.resource);
                            }}
                        >
                            Refresh affected server views
                        </button>
                    </div>
                    {!replayable(command) && (
                        <p>
                            There is no general replay endpoint for this operation. Inspect the current
                            catalog/order and its version or history before closing this record. Category
                            creation must not be blindly submitted again.
                        </p>
                    )}
                </>
            )}
            <label className="check-label">
                <input type="checkbox" checked={ack} onChange={(event) => setAck(event.target.checked)} />I
                have checked server state. Closing local recovery does not undo an effect and a new request
                could repeat a non-idempotent action.
            </label>
            <button
                className="button secondary"
                disabled={!ack || running || retry.isPending || close.isPending}
                onClick={() => close.mutate(undefined, { onSuccess: () => setAck(false) })}
            >
                Close local Admin recovery
            </button>
            {(retry.error || close.error) && (
                <ErrorPanel error={retry.error ?? close.error} title="Recovery could not be confirmed" />
            )}
        </section>
    );
}
export function useCommand<T>(onConfirmed?: (data: T) => void) {
    const { recovery } = useAdminRecovery();
    const { controller, userId } = useAdmin();
    const feedback = useContext(Feedback);
    return useAdminMutation<T, AdminCommand>(
        (command) =>
            recovery.execute(command, (saved) =>
                controller.guard(userId, () =>
                    controller.api.request<T>(`/api/admin/${saved.resource}`, {
                        method: saved.method,
                        body: saved.body,
                        timeoutMs: 15_000,
                    }),
                ),
            ),
        (command) => command.resource,
        feedback
            ? {
                  ...feedback,
                  success: (data) => {
                      feedback.success();
                      onConfirmed?.(data);
                  },
              }
            : undefined,
    );
}
export function WriteNotice({ error }: { error: unknown }) {
    if (!error) return null;
    return (
        <>
            <ErrorPanel error={error} title="The server did not confirm this action" />
            {error instanceof ApiError && error.status === 409 && (
                <p className="notice">
                    Conflict: reload the authoritative record, review its version/state and your input before
                    submitting a new intention. The server may reject stock below Reserved, stale versions or
                    invalid transitions.
                </p>
            )}
        </>
    );
}
export function Field({
    id,
    label,
    hint,
    error,
    children,
}: PropsWithChildren<{ id: string; label: string; hint?: string; error?: string }>) {
    return (
        <div className="field">
            <label htmlFor={id}>{label}</label>
            {children}
            {hint && (
                <p className="field-hint" id={`${id}-hint`}>
                    {hint}
                </p>
            )}
            {error && (
                <p className="field-error" id={`${id}-error`} role="alert">
                    {error}
                </p>
            )}
        </div>
    );
}
export const fieldError = (error: unknown, name: string) =>
    error instanceof ApiError ? error.field(name).join(' ') : '';
export function FilterForm({ children, submit }: PropsWithChildren<{ submit: (data: FormData) => void }>) {
    return (
        <form
            className="admin-filters card"
            onSubmit={(event: FormEvent<HTMLFormElement>) => {
                event.preventDefault();
                submit(new FormData(event.currentTarget));
            }}
        >
            {children}
            <button className="button secondary" type="submit">
                Apply filters
            </button>
        </form>
    );
}
export interface Column<T> {
    label: string;
    cell: (row: T) => ReactNode;
}
export function Table<T>({
    rows,
    columns,
    caption,
    id,
}: {
    rows: T[];
    columns: Column<T>[];
    caption: string;
    id: (row: T) => string;
}) {
    if (!rows.length)
        return (
            <div className="state-box">
                <h3>No records here.</h3>
                <p>Try another filter or refresh the current view.</p>
            </div>
        );
    return (
        <div className="table-wrap" role="region" aria-label={caption} tabIndex={0}>
            <table className="admin-table">
                <caption className="sr-only">{caption}</caption>
                <thead>
                    <tr>
                        {columns.map((column) => (
                            <th scope="col" key={column.label}>
                                {column.label}
                            </th>
                        ))}
                    </tr>
                </thead>
                <tbody>
                    {rows.map((row) => (
                        <tr key={id(row)}>
                            {columns.map((column) => (
                                <td data-label={column.label} key={column.label}>
                                    {column.cell(row)}
                                </td>
                            ))}
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
}
export function QueryPanel<T>({
    query,
    children,
    blockWhileFetching = false,
}: {
    query: UseQueryResult<T>;
    children: (value: T) => ReactNode;
    blockWhileFetching?: boolean;
}) {
    if (query.isPending || (blockWhileFetching && query.isFetching))
        return <Loading text="Loading Admin data…" />;
    if (query.isError)
        return (
            <ErrorPanel
                title="Admin data could not be loaded"
                error={query.error}
                retry={() => {
                    void query.refetch();
                }}
            />
        );
    return (
        <>
            {query.isFetching && (
                <p className="notice" role="status">
                    Refreshing server data…
                </p>
            )}
            {children(query.data)}
        </>
    );
}
export function Paging({ value, change }: { value: Page<unknown>; change: (page: number) => void }) {
    return (
        <nav className="pagination" aria-label="Admin pages">
            <span>
                Page {value.page} of {Math.max(1, Math.ceil(value.totalCount / value.pageSize))} ·{' '}
                {value.totalCount} records
            </span>
            <div>
                <button
                    type="button"
                    className="button secondary small"
                    disabled={value.page <= 1}
                    onClick={() => change(value.page - 1)}
                >
                    Previous page
                </button>
                <button
                    type="button"
                    className="button secondary small"
                    disabled={value.page * value.pageSize >= value.totalCount}
                    onClick={() => change(value.page + 1)}
                >
                    Next page
                </button>
            </div>
        </nav>
    );
}
export const pageNumber = (search: URLSearchParams) => {
    const page = Number(search.get('page') ?? '1');
    return Number.isInteger(page) && page > 0 && page <= 100_000 ? page : 1;
};
export const text = (data: FormData, key: string) => String(data.get(key) ?? '').trim();
export const dateTime = (value: string | null) => (value ? new Date(value).toLocaleString() : '—');
export function AdminHome() {
    return (
        <section className="card admin-panel">
            <h2>Choose an Admin view</h2>
            <p>
                Manage catalog/stock, inspect orders, reports and immutable audit, or review failed email
                work.
            </p>
            <Link className="text-link" to="/admin/products">
                Open products
            </Link>
        </section>
    );
}
