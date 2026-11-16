import { createContext, useContext, useEffect, useMemo, useSyncExternalStore } from 'react';
import type { PropsWithChildren } from 'react';
import { useSession } from '../auth/context';
import { PendingOperations } from './pending';

const Context = createContext<PendingOperations | null>(null);
export function OperationsProvider({ children }: PropsWithChildren) {
    const { session } = useSession();
    const userId = session.phase === 'authenticated' ? session.user.id : null;
    const operations = useMemo(() => (userId ? new PendingOperations(userId) : null), [userId]);
    useEffect(() => {
        if (!operations) return;
        const listener = (event: StorageEvent) => {
            if (event.key === operations.storageKey || event.key === null) operations.reload();
        };
        window.addEventListener('storage', listener);
        return () => window.removeEventListener('storage', listener);
    }, [operations]);
    return <Context.Provider value={operations}>{children}</Context.Provider>;
}
export function useOperations() {
    const operations = useContext(Context);
    if (!operations) throw new Error('A verified session is required.');
    const pending = useSyncExternalStore(operations.subscribe, operations.getSnapshot);
    return { operations, pending };
}
