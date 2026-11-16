import type { QueryClient } from '@tanstack/react-query';
import { ApiClient, ApiError } from '../api/client';
import { isPrivate, sessionOptions } from '../api/queries';
import type { Credentials, User } from '../api/types';

export type Session =
    | { phase: 'restoring' }
    | { phase: 'changing' }
    | { phase: 'anonymous'; expired?: ApiError }
    | { phase: 'authenticated'; user: User }
    | { phase: 'unavailable'; error: unknown };

// Coordinates cookie identity and cache lifetime; the server alone authenticates/authorizes.
// No password, role, cookie or CSRF state is persisted or placed in Zustand.
export class SessionController {
    private snapshot: Session = { phase: 'restoring' };
    private listeners = new Set<() => void>();
    private epoch = 0;
    private busy = false;
    private started = false;
    private channel: BroadcastChannel | null = null;
    private readonly source = crypto.randomUUID();

    constructor(
        public readonly queries: QueryClient,
        public readonly api: ApiClient,
    ) {}
    getSnapshot = () => this.snapshot;
    subscribe = (listener: () => void) => {
        this.listeners.add(listener);
        return () => {
            this.listeners.delete(listener);
        };
    };
    private publish(value: Session) {
        this.snapshot = value;
        this.listeners.forEach((listener) => listener());
    }

    start() {
        if (this.started) return;
        this.started = true;
        if (typeof BroadcastChannel !== 'undefined') {
            this.channel = new BroadcastChannel('orderflow-session');
            this.channel.onmessage = (event) => {
                if (event.data?.source === this.source) return;
                if (event.data?.type === 'changing') void this.clear('restoring');
                if (event.data?.type === 'settled') void this.refresh();
            };
        }
        window.addEventListener('focus', this.onFocus);
        window.addEventListener('pagehide', this.onPageHide);
        window.addEventListener('pageshow', this.onPageShow);
        document.addEventListener('visibilitychange', this.onVisibility);
        void this.refresh();
    }
    dispose() {
        this.channel?.close();
        this.channel = null;
        window.removeEventListener('focus', this.onFocus);
        window.removeEventListener('pagehide', this.onPageHide);
        window.removeEventListener('pageshow', this.onPageShow);
        document.removeEventListener('visibilitychange', this.onVisibility);
        this.started = false;
    }
    private onFocus = () => {
        if (!this.busy) void this.refresh();
    };
    private onPageHide = () => {
        void this.clear('restoring');
    };
    private onPageShow = (event: PageTransitionEvent) => {
        if (event.persisted && !this.busy) void this.refresh();
    };
    private onVisibility = () => {
        if (this.busy) return;
        if (document.visibilityState === 'hidden') void this.clear('restoring');
        else void this.refresh();
    };
    private broadcast(type: 'changing' | 'settled') {
        this.channel?.postMessage({ type, source: this.source });
    }

    private clear(phase: 'restoring' | 'changing') {
        this.epoch++;
        this.publish({ phase }); // Hide protected content before cancelling/removing cached values.
        this.api.invalidateCsrf();
        const cancelled = this.queries.cancelQueries({ predicate: isPrivate });
        this.queries.removeQueries({ predicate: isPrivate });
        this.queries.getMutationCache().clear();
        return cancelled;
    }

    private async verify(epoch: number, refreshToken: boolean) {
        try {
            const user = await this.queries.fetchQuery(sessionOptions(this.api));
            if (epoch !== this.epoch) return;
            if (refreshToken) await this.api.refreshCsrf();
            if (epoch === this.epoch)
                this.publish(user ? { phase: 'authenticated', user } : { phase: 'anonymous' });
        } catch (error) {
            if (epoch === this.epoch) this.publish({ phase: 'unavailable', error });
        }
    }

    async refresh() {
        if (this.busy) return;
        const cancelled = this.clear('restoring');
        const epoch = this.epoch;
        await cancelled;
        if (epoch === this.epoch) await this.verify(epoch, false);
    }
    changed = () => {
        void this.refresh();
    };

    async guard<T>(userId: string, action: () => Promise<T>): Promise<T> {
        const epoch = this.epoch;
        const ownsScope = () =>
            epoch === this.epoch &&
            this.snapshot.phase === 'authenticated' &&
            this.snapshot.user.id === userId;
        if (!ownsScope()) throw new DOMException('Session scope is no longer active.', 'AbortError');
        try {
            const result = await action();
            if (!ownsScope()) throw new DOMException('Identity changed during the request.', 'AbortError');
            return result;
        } catch (error) {
            // Error side effects need the same fence as data: an old account's late 401
            // must not expire the new account's successfully verified session.
            if (!ownsScope()) throw new DOMException('Session scope is no longer active.', 'AbortError');
            if (error instanceof ApiError && error.status === 401) {
                void this.clear('restoring');
                this.publish({ phase: 'anonymous', expired: error });
                this.broadcast('settled');
            }
            throw error;
        }
    }

    private async transition(action: () => Promise<unknown>) {
        if (this.busy) throw new Error('Another session change is in progress.');
        this.busy = true;
        await this.clear('changing');
        const epoch = this.epoch;
        this.broadcast('changing');
        try {
            await action();
            this.api.invalidateCsrf();
            await this.verify(epoch, true); // Verify the cookie via /me; never infer session from register/login DTOs.
            if (this.snapshot.phase === 'unavailable') throw this.snapshot.error;
        } catch (error) {
            this.api.invalidateCsrf();
            await this.verify(epoch, false); // Resolve an uncertain response or the still-valid old cookie on failed login/logout.
            throw error;
        } finally {
            this.busy = false;
            if (this.snapshot.phase === 'restoring' || this.snapshot.phase === 'changing')
                await this.refresh();
            this.broadcast('settled');
        }
    }
    login(credentials: Credentials) {
        return this.transition(() =>
            this.api.request<User>('/api/auth/login', { method: 'POST', body: credentials }),
        );
    }
    logout() {
        return this.transition(() => this.api.request<void>('/api/auth/logout', { method: 'POST' }));
    }
    async register(credentials: Credentials) {
        await this.api.request<User>('/api/auth/register', { method: 'POST', body: credentials });
        // Registration creates a Customer but does not sign in or replace the current cookie.
        this.api.invalidateCsrf(); // The next unsafe request retrieves a fresh token; no false registration failure after a confirmed 201.
    }
}
