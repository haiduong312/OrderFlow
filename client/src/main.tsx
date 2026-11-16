import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router';
import { QueryClientProvider } from '@tanstack/react-query';
import { ApiClient } from './api/client';
import { createQueryClient } from './api/queries';
import { SessionController } from './auth/session';
import { SessionProvider } from './auth/context';
import { App, AppErrorBoundary } from './App';
import './styles.css';
import { CART_KEY, useCart } from './state/cart';
import { NotificationRealtime } from './realtime/notifications';
import { RealtimeProvider } from './realtime/context';

const queries = createQueryClient(); // One stable instance for the browser application.
const session = new SessionController(queries, new ApiClient());
session.start();
const realtime = new NotificationRealtime(session, import.meta.env.VITE_ORDERFLOW_REALTIME === 'true');
realtime.start();
// Same-origin tabs share this device cart. Last persisted write wins; purchases are never inferred.
const syncCart = (event: StorageEvent) => {
    if (event.key === CART_KEY || event.key === null) void useCart.persist.rehydrate();
};
window.addEventListener('storage', syncCart);
createRoot(document.getElementById('root')!).render(
    <StrictMode>
        <AppErrorBoundary>
            <QueryClientProvider client={queries}>
                <SessionProvider controller={session}>
                    <BrowserRouter>
                        <RealtimeProvider value={realtime}>
                            <App />
                        </RealtimeProvider>
                    </BrowserRouter>
                </SessionProvider>
            </QueryClientProvider>
        </AppErrorBoundary>
    </StrictMode>,
);
if (import.meta.hot)
    import.meta.hot.dispose(() => {
        session.dispose();
        realtime.dispose();
        queries.clear();
        window.removeEventListener('storage', syncCart);
    });
