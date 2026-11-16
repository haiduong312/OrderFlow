import {
    DefaultHttpClient,
    HttpClient,
    HttpResponse,
    HubConnectionBuilder,
    HttpTransportType,
    LogLevel,
} from '@microsoft/signalr';
import type { HttpRequest, HubConnection } from '@microsoft/signalr';
import type { SessionController } from '../auth/session';
import { responseProblem } from '../api/client';
import { isProductId } from '../api/catalog';

export class CookieHubHttpClient extends HttpClient {
    private readonly http = new DefaultHttpClient({ log: () => {} });
    constructor(
        private readonly controller: SessionController,
        private readonly userId: string,
        private readonly origin = window.location.origin,
    ) {
        super();
    }
    async send(request: HttpRequest): Promise<HttpResponse> {
        return this.controller.guard(this.userId, async () => {
            const url = new URL(request.url ?? '', this.origin);
            if (
                url.origin !== this.origin ||
                !url.pathname.startsWith('/api/hubs/notifications') ||
                url.searchParams.has('access_token')
            )
                throw new Error('Use the same-origin cookie notification hub.');
            const headers = request.method === 'GET' ? {} : await this.controller.api.antiforgeryHeaders();
            const response = await this.http.send({
                ...request,
                headers: { ...request.headers, ...headers },
                withCredentials: true,
                timeout: 10_000,
            });
            if (response.statusCode >= 400) {
                const problem = await responseProblem(
                    new Response(typeof response.content === 'string' ? response.content : '', {
                        status: response.statusCode,
                    }),
                );
                if (problem.code === 'antiforgery_failed') this.controller.api.invalidateCsrf();
                throw problem;
            }
            return response;
        });
    }
}
export type LiveState = 'off' | 'connecting' | 'connected' | 'reconnecting' | 'offline';
export type HubFactory = (userId: string) => HubConnection;
export class NotificationRealtime {
    private state: LiveState;
    private listeners = new Set<() => void>();
    private unsubscribe?: () => void;
    private stopCurrent?: () => void;
    private currentUser: string | null = null;
    constructor(
        private readonly controller: SessionController,
        private readonly enabled: boolean,
        private readonly factory: HubFactory = (userId) =>
            new HubConnectionBuilder()
                .withUrl('/api/hubs/notifications', {
                    transport: HttpTransportType.WebSockets,
                    skipNegotiation: false,
                    withCredentials: true,
                    httpClient: new CookieHubHttpClient(controller, userId),
                    logMessageContent: false,
                })
                .configureLogging(LogLevel.None)
                .withAutomaticReconnect([0, 2000, 10000, 30000])
                .build(),
    ) {
        this.state = enabled ? 'offline' : 'off';
    }
    getSnapshot = () => this.state;
    subscribe = (listener: () => void) => {
        this.listeners.add(listener);
        return () => {
            this.listeners.delete(listener);
        };
    };
    private publish(state: LiveState) {
        this.state = state;
        this.listeners.forEach((listener) => listener());
    }
    private catchUp(userId: string, orderId?: string) {
        const queries = this.controller.queries;
        const paths = orderId
            ? [['orders'], ['order', orderId], ['payments', orderId], ['notifications'], ['unread']]
            : [[]];
        paths.forEach((path) => {
            void queries.invalidateQueries(
                { queryKey: ['private', userId, ...path] },
                { cancelRefetch: false },
            );
        });
    }
    start() {
        if (this.unsubscribe) return;
        this.unsubscribe = this.controller.subscribe(this.sync);
        this.sync();
    }
    dispose() {
        this.unsubscribe?.();
        this.unsubscribe = undefined;
        this.stopCurrent?.();
        this.stopCurrent = undefined;
        this.currentUser = null;
    }
    restart = () => {
        this.stopCurrent?.();
        this.stopCurrent = undefined;
        this.currentUser = null;
        this.sync();
    };
    private sync = () => {
        const session = this.controller.getSnapshot();
        const userId = session.phase === 'authenticated' ? session.user.id : null;
        if (userId === this.currentUser) return;
        this.stopCurrent?.();
        this.stopCurrent = undefined;
        this.currentUser = userId;
        if (!this.enabled || !userId) {
            this.publish(this.enabled ? 'offline' : 'off');
            return;
        }
        const connection = this.factory(userId);
        let disposed = false;
        const seen = new Set<string>();
        const active = () =>
            !disposed &&
            this.controller.getSnapshot().phase === 'authenticated' &&
            this.currentUser === userId;
        this.stopCurrent = () => {
            disposed = true;
            void connection.stop().catch(() => {
                /* A stopped hint channel has no business side effect. */
            });
        };
        connection.on('NotificationAvailable', (hint: unknown) => {
            const row = hint as { eventId?: string; notificationId?: string; orderId?: string };
            if (
                !active() ||
                !row ||
                !row.eventId ||
                !row.notificationId ||
                !row.orderId ||
                !isProductId(row.eventId) ||
                !isProductId(row.notificationId) ||
                !isProductId(row.orderId) ||
                seen.has(row.eventId)
            )
                return;
            seen.add(row.eventId);
            if (seen.size > 256) seen.delete(seen.values().next().value!);
            this.catchUp(userId, row.orderId); // Never increment unread or accept a hinted status/amount.
        });
        connection.on('ConnectionReady', () => {
            if (active()) {
                this.publish('connected');
                this.catchUp(userId);
            }
        });
        connection.onreconnecting(() => {
            if (active()) this.publish('reconnecting');
        });
        connection.onreconnected(() => {
            if (active()) {
                this.publish('connected');
                this.catchUp(userId);
            }
        });
        connection.onclose(() => {
            if (active()) this.publish('offline');
        });
        this.publish('connecting');
        void connection.start().catch(() => {
            if (active()) this.publish('offline');
        });
    };
}
