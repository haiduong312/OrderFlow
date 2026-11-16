import { createContext, useContext, useSyncExternalStore } from 'react';
import type { PropsWithChildren } from 'react';
import type { NotificationRealtime } from './notifications';
const Context = createContext<NotificationRealtime | null>(null);
export function RealtimeProvider({ value, children }: PropsWithChildren<{ value: NotificationRealtime }>) {
    return <Context.Provider value={value}>{children}</Context.Provider>;
}
export function LiveUpdates() {
    const live = useContext(Context);
    if (!live) return null;
    return <LiveStatus live={live} />;
}
function LiveStatus({ live }: { live: NotificationRealtime }) {
    const state = useSyncExternalStore(live.subscribe, live.getSnapshot);
    return (
        <span className="live-updates">
            <span role="status" data-testid="live-state">
                {state === 'connected'
                    ? 'Live updates connected'
                    : state === 'connecting'
                      ? 'Connecting live updates…'
                      : state === 'reconnecting'
                        ? 'Reconnecting live updates…'
                        : state === 'off'
                          ? 'Live updates off · periodic refresh'
                          : 'Live updates unavailable · periodic refresh'}
            </span>
            {state !== 'off' && (
                <button className="text-button" onClick={live.restart}>
                    Reconnect live updates
                </button>
            )}
        </span>
    );
}
