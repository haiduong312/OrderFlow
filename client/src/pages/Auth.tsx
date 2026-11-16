import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useLocation, useNavigate } from 'react-router';
import { ApiError } from '../api/client';
import { useSession } from '../auth/context';
import { BrandMark, ErrorPanel, Icon } from '../components/ui';
import { safeReturnTo } from '../auth/returnTo';

export function AuthPage({ register = false }: { register?: boolean }) {
    const { session, controller } = useSession();
    const location = useLocation();
    const navigate = useNavigate();
    const [error, setError] = useState<unknown>(null);
    const [busy, setBusy] = useState(false);
    const [created, setCreated] = useState(false);
    const fields = error instanceof ApiError ? error : null;
    const current = session.phase === 'authenticated' ? session.user : null;
    const title = register ? 'Make yourself at home.' : current ? 'Switch your account.' : 'Welcome back.';
    async function submit(event: FormEvent<HTMLFormElement>) {
        event.preventDefault();
        if (busy || session.phase === 'changing') return;
        const form = event.currentTarget;
        const data = new FormData(form);
        setError(null);
        setBusy(true);
        try {
            const credentials = {
                email: String(data.get('email') ?? '').trim(),
                password: String(data.get('password') ?? ''),
            };
            if (register) {
                await controller.register(credentials);
                setCreated(true);
                form.reset();
            } else {
                await controller.login(credentials);
                if (controller.getSnapshot().phase !== 'authenticated')
                    throw new Error('The cookie session could not be confirmed.');
                navigate(safeReturnTo(location.state?.from), { replace: true });
            }
        } catch (failure) {
            setError(failure);
            const password = form.elements.namedItem('password') as HTMLInputElement | null;
            if (password) password.value = '';
        } finally {
            setBusy(false);
        }
    }
    return (
        <div className="auth-layout">
            <section className="auth-intro">
                <p className="eyebrow">YOUR ORDERFLOW WORKSPACE</p>
                <h1>{title}</h1>
                <p>
                    Less searching. More clarity.
                    <br />
                    Keep your account and orders close.
                </p>
                <div className="auth-illustration" aria-hidden="true">
                    <div className="auth-illustration-logo">
                        <BrandMark />
                    </div>
                    <div className="auth-illustration-line" />
                    <span>
                        <Icon name="check" size={22} />
                    </span>
                </div>
                <blockquote>
                    Good things happen
                    <br />
                    when everything is in order.
                </blockquote>
                <span className="intro-footnote">A simpler view of what matters.</span>
            </section>
            <section className="auth-card card" aria-labelledby="auth-title">
                <span className="icon-tile">
                    <Icon name={register ? 'account' : 'shield'} size={24} />
                </span>
                <h2 id="auth-title">
                    {created
                        ? 'Your account is ready.'
                        : register
                          ? 'Create your account'
                          : 'Sign in to OrderFlow'}
                </h2>
                {created ? (
                    <div className="registered">
                        <p role="status">Your Customer account was created. Sign in to start your session.</p>
                        <Link className="button" to="/login">
                            Continue to sign in
                            <Icon name="arrow" size={18} />
                        </Link>
                    </div>
                ) : (
                    <>
                        <p className="auth-description">
                            {register
                                ? 'A few details, and you’re on your way.'
                                : 'Enter your details to continue to your workspace.'}
                        </p>
                        {current && (
                            <div className="notice">
                                Currently signed in as <strong>{current.email}</strong>.
                                {register
                                    ? ' Creating an account does not switch your session.'
                                    : ' Signing in below replaces this session.'}
                            </div>
                        )}
                        {location.state?.expired && (
                            <div className="notice" role="status">
                                Your session has ended. Please sign in again.
                            </div>
                        )}
                        {session.phase === 'anonymous' && session.expired && (
                            <p className="error-reference">
                                {session.expired.code} · Trace <code>{session.expired.traceId}</code>
                            </p>
                        )}
                        {error != null && (
                            <ErrorPanel
                                title={
                                    register ? 'Please check your details' : 'Sign in needs your attention'
                                }
                                error={error}
                            />
                        )}
                        <form
                            onSubmit={(event) => {
                                void submit(event);
                            }}
                        >
                            <fieldset disabled={busy || session.phase === 'changing'}>
                                <div className="field">
                                    <label htmlFor="email">Email address</label>
                                    <input
                                        id="email"
                                        name="email"
                                        type="email"
                                        autoComplete="username"
                                        required
                                        maxLength={256}
                                        placeholder="you@example.com"
                                        aria-invalid={!!fields?.field('email').length}
                                        aria-describedby={
                                            fields?.field('email').length ? 'email-error' : undefined
                                        }
                                    />
                                    {!!fields?.field('email').length && (
                                        <p className="field-error" id="email-error">
                                            {fields.field('email').join(' ')}
                                        </p>
                                    )}
                                </div>
                                <div className="field">
                                    <label htmlFor="password">Password</label>
                                    <input
                                        id="password"
                                        name="password"
                                        type="password"
                                        autoComplete={register ? 'new-password' : 'current-password'}
                                        required
                                        minLength={register ? 12 : undefined}
                                        maxLength={128}
                                        placeholder={
                                            register ? 'Create a strong password' : 'Enter your password'
                                        }
                                        aria-invalid={!!fields?.field('password').length}
                                        aria-describedby={[
                                            'password-hint',
                                            fields?.field('password').length ? 'password-error' : '',
                                        ]
                                            .filter(Boolean)
                                            .join(' ')}
                                    />
                                    <p className="field-hint" id="password-hint">
                                        {register
                                            ? '12–128 characters with uppercase, lowercase, a number, a symbol, and at least four distinct characters.'
                                            : 'Use the password for your OrderFlow account.'}
                                    </p>
                                    {!!fields?.field('password').length && (
                                        <p className="field-error" id="password-error">
                                            {fields.field('password').join(' ')}
                                        </p>
                                    )}
                                </div>
                                <button className="button full" type="submit">
                                    {busy
                                        ? 'Confirming with OrderFlow…'
                                        : register
                                          ? 'Create account'
                                          : current
                                            ? 'Switch account'
                                            : 'Sign in'}
                                    {!busy && <Icon name="arrow" size={18} />}
                                </button>
                            </fieldset>
                        </form>
                        <p className="auth-switch">
                            {register ? 'Already have an account?' : 'New to OrderFlow?'}{' '}
                            <Link to={register ? '/login' : '/register'}>
                                {register ? 'Sign in' : 'Create an account'}
                            </Link>
                        </p>
                        <div className="auth-assurance">
                            <Icon name="shield" size={16} />
                            <span>Your session is protected by OrderFlow.</span>
                        </div>
                    </>
                )}
            </section>
        </div>
    );
}
