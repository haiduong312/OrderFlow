import { createContext, useContext, useSyncExternalStore } from 'react';
import type { PropsWithChildren } from 'react';
import type { SessionController } from './session';

const Context = createContext<SessionController | null>(null);
export function SessionProvider({
    controller,
    children,
}: PropsWithChildren<{ controller: SessionController }>) {
    return <Context.Provider value={controller}>{children}</Context.Provider>;
}
export function useSession() {
    const controller = useContext(Context);
    if (!controller) throw new Error('SessionProvider is required.');
    const session = useSyncExternalStore(controller.subscribe, controller.getSnapshot);
    return { session, controller };
}
