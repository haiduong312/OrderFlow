import { Component } from 'react';
import type { PropsWithChildren } from 'react';
import { Link, Route, Routes } from 'react-router';
import { Layout } from './components/Layout';
import { Protected } from './components/Protected';
import { Home } from './pages/Home';
import { AuthPage } from './pages/Auth';
import { Account } from './pages/Account';
import { Orders } from './pages/Orders';

export class AppErrorBoundary extends Component<PropsWithChildren, { failed: boolean }> {
    state = { failed: false };
    static getDerivedStateFromError() {
        return { failed: true };
    }
    render() {
        return this.state.failed ? (
            <main className="state-box" role="alert">
                <h1>This screen could not be displayed.</h1>
                <p>Reload to reconnect to OrderFlow.</p>
                <button className="button" onClick={() => window.location.reload()}>
                    Reload workspace
                </button>
            </main>
        ) : (
            this.props.children
        );
    }
}
export function App() {
    return (
        <Routes>
            <Route element={<Layout />}>
                <Route index element={<Home />} />
                <Route path="login" element={<AuthPage key="login" />} />
                <Route path="register" element={<AuthPage key="register" register />} />
                <Route
                    path="account"
                    element={
                        <Protected>
                            <Account />
                        </Protected>
                    }
                />
                <Route
                    path="orders"
                    element={
                        <Protected customer>
                            <Orders />
                        </Protected>
                    }
                />
                <Route
                    path="*"
                    element={
                        <section className="state-box">
                            <p className="eyebrow">404</p>
                            <h1>This page took a wrong turn.</h1>
                            <p>Let’s get you back to your workspace.</p>
                            <Link className="button" to="/">
                                Back to overview
                            </Link>
                        </section>
                    }
                />
            </Route>
        </Routes>
    );
}
