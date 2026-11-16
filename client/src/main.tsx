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

const queries = createQueryClient(); // One stable instance for the browser application.
const session = new SessionController(queries, new ApiClient());
session.start();
createRoot(document.getElementById('root')!).render(
    <StrictMode>
        <AppErrorBoundary>
            <QueryClientProvider client={queries}>
                <SessionProvider controller={session}>
                    <BrowserRouter>
                        <App />
                    </BrowserRouter>
                </SessionProvider>
            </QueryClientProvider>
        </AppErrorBoundary>
    </StrictMode>,
);
if (import.meta.hot)
    import.meta.hot.dispose(() => {
        session.dispose();
        queries.clear();
    });
