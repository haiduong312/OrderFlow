import type { ReactNode } from 'react';
import { ApiError } from '../api/client';

export function Icon({
    name,
    size = 20,
}: {
    name:
        | 'home'
        | 'orders'
        | 'account'
        | 'arrow'
        | 'check'
        | 'menu'
        | 'close'
        | 'logout'
        | 'shield'
        | 'product'
        | 'cart';
    size?: number;
}) {
    const paths = {
        home: 'm3 10 9-7 9 7v10a1 1 0 0 1-1 1h-5v-7H9v7H4a1 1 0 0 1-1-1Z',
        orders: 'M6 3h12v18H6ZM9 7h6M9 11h6M9 15h4',
        account: 'M16 7a4 4 0 1 1-8 0 4 4 0 0 1 8 0ZM4 21v-2a8 8 0 0 1 16 0v2',
        arrow: 'M5 12h14m-5-5 5 5-5 5',
        check: 'm5 12 4 4L19 6',
        menu: 'M4 6h16M4 12h16M4 18h16',
        close: 'm6 6 12 12M6 18 18 6',
        logout: 'M9 4H4v16h5m5-13 5 5-5 5M8 12h11',
        shield: 'm12 3 8 3v6c0 5-8 9-8 9s-8-4-8-9V6Zm-4 9 3 3 5-6',
        product: 'm12 3 9 5-9 5-9-5Zm-9 5v10l9 5 9-5V8M12 13v10M7.5 5.5l9 5',
        cart: 'M3 3h2l3 13h11l3-9H6m3 14h.01M19 21h.01',
    };
    return (
        <svg
            width={size}
            height={size}
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            strokeWidth="1.7"
            strokeLinecap="round"
            strokeLinejoin="round"
            aria-hidden="true"
        >
            <path d={paths[name]} />
        </svg>
    );
}
export function BrandMark() {
    return (
        <svg viewBox="0 0 36 36" fill="none" aria-hidden="true">
            <path d="m18 3 14 8-14 8L4 11Z" fill="currentColor" />
            <path
                d="m4 17 14 8 14-8M4 24l14 8 14-8"
                stroke="currentColor"
                strokeWidth="3"
                strokeLinejoin="round"
            />
        </svg>
    );
}
export function Loading({ text = 'Loading your workspace…' }: { text?: string }) {
    return (
        <div className="state-box" role="status">
            <span className="spinner" aria-hidden="true" />
            <p>{text}</p>
        </div>
    );
}
export function ErrorPanel({
    error,
    title = 'Something needs your attention',
    retry,
}: {
    error: unknown;
    title?: string;
    retry?: () => void;
}) {
    const api = error instanceof ApiError ? error : null;
    return (
        <div className="error-panel" role="alert">
            <strong>{title}</strong>
            <p>
                {api?.code === 'antiforgery_failed'
                    ? 'Your security check has expired. Try again to get a fresh check.'
                    : (api?.message ??
                      (error instanceof Error && error.name === 'RecoveryError'
                          ? error.message
                          : 'We could not reach or confirm the service. Please try again.'))}
            </p>
            {api && (
                <p className="error-reference">
                    <span>{api.code}</span>
                    {api.traceId && (
                        <>
                            {' '}
                            · Trace <code>{api.traceId}</code>
                        </>
                    )}
                </p>
            )}
            {api?.retryAfter && <p>Try again after {api.retryAfter} seconds.</p>}
            {retry && (
                <button className="button secondary" onClick={retry}>
                    Try again
                </button>
            )}
        </div>
    );
}
export function PageTitle({
    eyebrow,
    title,
    description,
    action,
}: {
    eyebrow: string;
    title: string;
    description: string;
    action?: ReactNode;
}) {
    return (
        <header className="page-heading">
            <div>
                <p className="eyebrow">{eyebrow}</p>
                <h1 tabIndex={-1}>{title}</h1>
                <p>{description}</p>
            </div>
            {action}
        </header>
    );
}
