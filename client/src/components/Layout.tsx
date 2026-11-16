import { useEffect, useRef, useState } from 'react';
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { useSession } from '../auth/context';
import { healthOptions } from '../api/queries';
import { usePreferences } from '../state/preferences';
import { useCart } from '../state/cart';
import { LiveUpdates } from '../realtime/context';
import { BrandMark, ErrorPanel, Icon } from './ui';

export function Layout() {
    const { session, controller } = useSession();
    const { mobileOpen, setMobileOpen, density, setDensity } = usePreferences();
    const cartCount = useCart((state) => state.items.reduce((count, item) => count + item.quantity, 0));
    const location = useLocation();
    const navigate = useNavigate();
    const [logoutError, setLogoutError] = useState<unknown>(null);
    const toggle = useRef<HTMLButtonElement>(null);
    const restoreToggleFocus = useRef(false);
    const drawer = useRef<HTMLElement>(null);
    const [mobile, setMobile] = useState(() => window.matchMedia('(max-width: 700px)').matches);
    const health = useQuery(healthOptions(controller.api));
    useEffect(() => {
        const media = window.matchMedia('(max-width: 700px)');
        const change = () => {
            setMobile(media.matches);
            if (!media.matches) setMobileOpen(false);
        };
        media.addEventListener('change', change);
        return () => media.removeEventListener('change', change);
    }, [setMobileOpen]);
    useEffect(() => {
        setMobileOpen(false);
    }, [location.pathname, setMobileOpen]);
    useEffect(() => {
        if (!mobileOpen) {
            // Wait until React has removed inert from the workspace before returning focus.
            if (restoreToggleFocus.current) {
                restoreToggleFocus.current = false;
                toggle.current?.focus();
            } else {
                const heading = document.querySelector<HTMLElement>('h1');
                if (heading) {
                    heading.tabIndex = -1;
                    heading.focus();
                }
            }
            return;
        }
        drawer.current?.querySelector<HTMLElement>('button')?.focus();
        function escape(event: KeyboardEvent) {
            if (event.key === 'Escape') {
                restoreToggleFocus.current = true;
                setMobileOpen(false);
            }
            if (event.key === 'Tab') {
                const links = drawer.current?.querySelectorAll<HTMLElement>('a,button');
                const first = links?.[0];
                const last = links?.[links.length - 1];
                if (event.shiftKey && document.activeElement === first) {
                    event.preventDefault();
                    last?.focus();
                } else if (!event.shiftKey && document.activeElement === last) {
                    event.preventDefault();
                    first?.focus();
                }
            }
        }
        window.addEventListener('keydown', escape);
        return () => window.removeEventListener('keydown', escape);
    }, [mobileOpen, setMobileOpen, location.pathname]);
    const user = session.phase === 'authenticated' ? session.user : null;
    async function logout() {
        setLogoutError(null);
        try {
            await controller.logout();
            navigate(controller.getSnapshot().phase === 'anonymous' ? '/login' : '/account', {
                replace: true,
            });
        } catch (error) {
            setLogoutError(error);
        }
    }
    return (
        <div className={`app-shell ${density}`}>
            <a className="skip-link" href="#main-content">
                Skip to content
            </a>
            <aside
                ref={drawer}
                className={`sidebar ${mobileOpen ? 'open' : ''}`}
                inert={mobile && !mobileOpen}
                aria-hidden={mobile && !mobileOpen ? true : undefined}
                role={mobileOpen ? 'dialog' : undefined}
                aria-modal={mobileOpen ? true : undefined}
                aria-label={mobileOpen ? 'Workspace navigation' : undefined}
            >
                <button
                    className="icon-button sidebar-close"
                    aria-label="Close navigation"
                    onClick={() => {
                        restoreToggleFocus.current = true;
                        setMobileOpen(false);
                    }}
                >
                    <Icon name="close" />
                </button>
                <Link to="/" className="brand" aria-label="OrderFlow home">
                    <span className="brand-icon">
                        <BrandMark />
                    </span>
                    <span>
                        OrderFlow<span className="brand-caption">ORDER MANAGEMENT</span>
                    </span>
                </Link>
                <p className="nav-label">WORKSPACE</p>
                <nav aria-label="Main navigation">
                    <NavLink to="/" end>
                        <Icon name="home" />
                        Overview
                    </NavLink>
                    <NavLink to="/orders">
                        <Icon name="orders" />
                        My orders
                    </NavLink>
                    <NavLink to="/products">
                        <Icon name="product" /> Products
                    </NavLink>
                    <NavLink to="/cart">
                        <Icon name="cart" /> Cart <span className="nav-cart-count">{cartCount}</span>
                    </NavLink>
                    <NavLink to="/notifications">
                        <Icon name="orders" /> Notifications
                    </NavLink>
                    <NavLink to="/account">
                        <Icon name="account" />
                        My account
                    </NavLink>
                </nav>
                <div className="sidebar-note">
                    <span className="note-icon">
                        <Icon name="shield" />
                    </span>
                    <strong>A little clarity goes a long way.</strong>
                    <p>Your account and orders, together in one place.</p>
                </div>
                <div className="sidebar-bottom">
                    <span
                        className={`connection-dot ${health.isSuccess && health.data.status === 'Healthy' ? 'connected' : ''}`}
                    />
                    {health.isPending
                        ? 'Checking connection'
                        : health.isError
                          ? 'Connection issue'
                          : 'API reachable'}
                    <span className="version">v1</span>
                </div>
            </aside>
            {mobileOpen && (
                <button
                    className="nav-scrim"
                    aria-label="Close navigation"
                    onClick={() => {
                        restoreToggleFocus.current = true;
                        setMobileOpen(false);
                    }}
                />
            )}
            <div className="workspace" inert={mobile && mobileOpen}>
                <header className="topbar">
                    <button
                        className="icon-button menu-toggle"
                        ref={toggle}
                        aria-label={mobileOpen ? 'Close navigation' : 'Open navigation'}
                        aria-expanded={mobileOpen}
                        onClick={() => setMobileOpen(!mobileOpen)}
                    >
                        <Icon name={mobileOpen ? 'close' : 'menu'} />
                    </button>
                    <span className="topbar-title">
                        Your workspace <span className="topbar-divider">/</span>
                        <span className="muted">
                            {location.pathname.startsWith('/products')
                                ? 'Products'
                                : location.pathname === '/cart'
                                  ? 'Cart'
                                  : location.pathname.startsWith('/orders')
                                    ? 'Orders'
                                    : location.pathname === '/account'
                                      ? 'Account'
                                      : location.pathname === '/login'
                                        ? 'Sign in'
                                        : location.pathname === '/register'
                                          ? 'Create account'
                                          : 'Overview'}
                        </span>
                    </span>
                    <div className="topbar-actions">
                        <button
                            className="density-toggle"
                            aria-label="Compact layout"
                            aria-pressed={density === 'compact'}
                            onClick={() => setDensity(density === 'compact' ? 'comfortable' : 'compact')}
                        >
                            Compact view
                        </button>
                        {user ? (
                            <>
                                <Link className="user-link" to="/account">
                                    <span className="avatar">{user.email[0]?.toUpperCase()}</span>
                                    <span className="user-email">{user.email}</span>
                                </Link>
                                <button
                                    className="icon-button"
                                    aria-label="Sign out"
                                    title="Sign out"
                                    onClick={() => {
                                        void logout();
                                    }}
                                >
                                    <Icon name="logout" />
                                </button>
                            </>
                        ) : session.phase === 'changing' || session.phase === 'restoring' ? (
                            <span className="muted session-check" role="status">
                                Checking session…
                            </span>
                        ) : session.phase === 'unavailable' ? (
                            <button
                                className="button secondary small"
                                onClick={() => {
                                    void controller.refresh();
                                }}
                            >
                                Retry session
                            </button>
                        ) : (
                            <Link className="button small" to="/login">
                                Sign in
                                <Icon name="arrow" size={16} />
                            </Link>
                        )}
                    </div>
                </header>
                <main id="main-content" tabIndex={-1}>
                    {logoutError != null && (
                        <ErrorPanel title="We could not confirm sign out" error={logoutError} />
                    )}
                    {user && <LiveUpdates />}
                    <Outlet />
                </main>
                <footer className="footer">
                    <span>
                        OrderFlow <span className="muted">/ A clearer way to keep things moving.</span>
                    </span>
                    <Link to="/account">
                        Your account
                        <Icon name="arrow" size={14} />
                    </Link>
                </footer>
            </div>
        </div>
    );
}
