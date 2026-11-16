import { Link } from 'react-router';
import { useSession } from '../auth/context';
import { Icon, PageTitle } from '../components/ui';

export function Account() {
    const { session, controller } = useSession();
    if (session.phase !== 'authenticated') return null;
    const { user } = session;
    return (
        <>
            <PageTitle
                eyebrow="YOUR ACCOUNT"
                title="A little about your workspace."
                description="Your current account details, confirmed by OrderFlow."
                action={
                    <button
                        className="button secondary"
                        onClick={() => {
                            void controller.refresh();
                        }}
                    >
                        Refresh session
                    </button>
                }
            />
            <section className="account-card card">
                <div className="account-person">
                    <span className="avatar large">{user.email[0]?.toUpperCase()}</span>
                    <div>
                        <h2>{user.email}</h2>
                        <span className="badge success">
                            <Icon name="check" size={13} />
                            Session active
                        </span>
                    </div>
                </div>
                <dl className="account-details">
                    <div>
                        <dt>Email address</dt>
                        <dd>{user.email}</dd>
                    </div>
                    <div>
                        <dt>Account ID</dt>
                        <dd>
                            <code>{user.id}</code>
                        </dd>
                    </div>
                    <div>
                        <dt>Workspace access</dt>
                        <dd className="role-list">
                            {user.roles.map((role) => (
                                <span className="badge" key={role}>
                                    {role}
                                </span>
                            ))}
                        </dd>
                    </div>
                </dl>
                <div className="account-bottom">
                    <div>
                        <strong>Need a different workspace?</strong>
                        <p>Switch accounts to continue with another identity.</p>
                    </div>
                    <Link className="button secondary" to="/login">
                        Switch account
                        <Icon name="arrow" size={17} />
                    </Link>
                </div>
            </section>
            <p className="quiet-note">
                <Icon name="shield" size={17} />
                Access is checked by OrderFlow for every request. Account details here are read-only.
            </p>
        </>
    );
}
