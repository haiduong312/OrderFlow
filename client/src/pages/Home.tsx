import { Link } from 'react-router';
import { useSession } from '../auth/context';
import { BrandMark, ErrorPanel, Icon, PageTitle } from '../components/ui';

export function Home() {
    const { session, controller } = useSession();
    const user = session.phase === 'authenticated' ? session.user : null;
    return (
        <>
            <PageTitle
                eyebrow="A CLEARER VIEW"
                title="Everything in order."
                description="Your account, your orders, and a little more peace of mind."
                action={
                    <Link className="button secondary" to="/products">
                        Browse products <Icon name="arrow" size={17} />
                    </Link>
                }
            />
            <div className="overview-grid">
                <section className="welcome-card">
                    <div className="welcome-copy">
                        <span className="light-eyebrow">
                            <span className="tiny-square" />
                            WELCOME TO ORDERFLOW
                        </span>
                        <h2>
                            A place to keep
                            <br />
                            things moving.
                        </h2>
                        <p>
                            {user
                                ? 'Your workspace is ready. Follow your orders or take a moment to check your account.'
                                : 'Sign in to follow your orders and find the details that matter, all in one place.'}
                        </p>
                        <Link className="button light" to={user ? '/orders' : '/login'}>
                            {user ? 'View my orders' : 'Sign in to your workspace'}
                            <Icon name="arrow" size={18} />
                        </Link>
                    </div>
                    <div className="flow-art" aria-hidden="true">
                        <div className="orbit orbit-one" />
                        <div className="orbit orbit-two" />
                        <div className="art-tile art-back" />
                        <div className="art-tile art-front">
                            <BrandMark />
                            <span />
                            <span />
                            <span />
                        </div>
                        <span className="art-check">
                            <Icon name="check" size={22} />
                        </span>
                        <span className="art-spark">✳</span>
                    </div>
                </section>
                <section className="session-card card">
                    <div className="card-label">
                        <span className="icon-tile">
                            <Icon name="account" />
                        </span>
                        <span className={`badge ${user ? 'success' : ''}`}>
                            {user
                                ? 'Signed in'
                                : session.phase === 'unavailable'
                                  ? 'Needs attention'
                                  : session.phase === 'anonymous'
                                    ? 'Signed out'
                                    : 'Checking'}
                        </span>
                    </div>
                    <h2>Your session</h2>
                    <p className="muted">A quick look at where you stand.</p>
                    <dl>
                        <div>
                            <dt>Account</dt>
                            <dd>
                                {user?.email ??
                                    (session.phase === 'unavailable'
                                        ? 'Unable to verify'
                                        : session.phase === 'anonymous'
                                          ? 'Sign in to continue'
                                          : 'Checking session…')}
                            </dd>
                        </div>
                        <div>
                            <dt>Access</dt>
                            <dd>
                                {user?.roles.join(', ') ||
                                    (session.phase === 'anonymous'
                                        ? 'Not signed in'
                                        : session.phase === 'unavailable'
                                          ? 'Unable to verify'
                                          : 'Checking…')}
                            </dd>
                        </div>
                    </dl>
                    <Link className="text-link" to={user ? '/account' : '/login'}>
                        {user ? 'Go to my account' : 'Sign in'}
                        <Icon name="arrow" size={17} />
                    </Link>
                </section>
            </div>
            {session.phase === 'unavailable' && (
                <ErrorPanel
                    title="We could not check your session"
                    error={session.error}
                    retry={() => {
                        void controller.refresh();
                    }}
                />
            )}
            <section className="essentials">
                <div className="section-heading">
                    <div>
                        <h2>Your workspace essentials</h2>
                        <p>Simple, useful, and right where you need them.</p>
                    </div>
                    <span className="section-rule" />
                </div>
                <div className="essentials-grid">
                    <Link className="essential-card card" to="/orders">
                        <span className="icon-tile blue">
                            <Icon name="orders" size={23} />
                        </span>
                        <h3>Follow your orders</h3>
                        <p>See your order status and the totals confirmed by OrderFlow.</p>
                        <span className="text-link">
                            Open my orders
                            <Icon name="arrow" size={17} />
                        </span>
                    </Link>
                    <Link className="essential-card card" to="/account">
                        <span className="icon-tile peach">
                            <Icon name="account" size={23} />
                        </span>
                        <h3>Know your account</h3>
                        <p>Check your current account, access, and sign-in session.</p>
                        <span className="text-link">
                            View my account
                            <Icon name="arrow" size={17} />
                        </span>
                    </Link>
                    <Link className="essential-card card" to={user ? '/login' : '/register'}>
                        <span className="icon-tile sage">
                            <Icon name="shield" size={23} />
                        </span>
                        <h3>{user ? 'Another account?' : 'New around here?'}</h3>
                        <p>
                            {user
                                ? 'Switch accounts when you need to use a different workspace.'
                                : 'Create a Customer account and make this workspace yours.'}
                        </p>
                        <span className="text-link">
                            {user ? 'Switch account' : 'Create an account'}
                            <Icon name="arrow" size={17} />
                        </span>
                    </Link>
                </div>
            </section>
        </>
    );
}
