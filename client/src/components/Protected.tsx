import type { ReactNode } from 'react';
import { Link, Navigate, useLocation } from 'react-router';
import { useSession } from '../auth/context';
import { ErrorPanel, Icon, Loading } from './ui';

export function Protected({ children, customer = false }: { children: ReactNode; customer?: boolean }) {
    const { session, controller } = useSession();
    const location = useLocation();
    if (session.phase === 'restoring' || session.phase === 'changing')
        return <Loading text="Confirming your session…" />;
    if (session.phase === 'unavailable')
        return (
            <ErrorPanel
                title="Your session could not be confirmed"
                error={session.error}
                retry={() => {
                    void controller.refresh();
                }}
            />
        );
    if (session.phase === 'anonymous')
        return (
            <Navigate
                to="/login"
                replace
                state={{ from: location.pathname + location.search, expired: !!session.expired }}
            />
        );
    // Role checks are navigation UX. The API still authorizes every request independently.
    if (customer && !session.user.roles.includes('Customer'))
        return (
            <section className="state-box">
                <Icon name="shield" size={34} />
                <h1>Customer workspace required</h1>
                <p>
                    This account does not have Customer access. Your server permissions determine which orders
                    you can view.
                </p>
                <Link className="button secondary" to="/account">
                    View my account
                </Link>
            </section>
        );
    return children;
}
