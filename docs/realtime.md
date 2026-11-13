# Authenticated SignalR notification hints

Durable Notifications and order HTTP APIs remain the source of truth. Realtime carries a small hint to refetch; it is neither an inbox nor an exactly-once/durable notification channel. NotificationCreated still originates from the existing transactional outbox.

## Setup and API configuration

Use the SDK in global.json and existing private [PostgreSQL](persistence.md), [RabbitMQ](messaging.md), [notification consumer](notifications.md) setup. This milestone has no database migration. Official SignalR Redis backplane and test client are pinned to 10.0.15; existing SDK/EF pins remain. The patched package avoids the 10.0.14 backplane advisory documented in [ADR-024](adr/0024-signalr-notification-hints.md).

```bash
dotnet restore OrderFlow.slnx --locked-mode
dotnet build OrderFlow.slnx -c Release --no-restore
docker compose --profile realtime up -d --wait redis
export DOTNET_ENVIRONMENT=Development
export Authentication__AllowInsecureHttp=true
export Realtime__Enabled=true
export Realtime__RedisConnectionString="127.0.0.1:${ORDERFLOW_REDIS_PORT:-6379}"
export Realtime__RedisPrefix=orderflow.realtime.v1
export Realtime__InstanceName=api-a
export Realtime__DispatchEnabled=true
dotnet run --project src/OrderFlow.Api -c Release --no-build --no-restore --no-launch-profile -- --urls http://127.0.0.1:5081
```

ConnectionStrings__OrderFlow and RabbitMq credentials/prefix must be privately exported as in existing guides. Never put Redis credentials in CLI arguments or tracked appsettings. Compose still requires ORDERFLOW_DB_PASSWORD_FILE during interpolation; Redis service does not mount that secret. Redis 8.10.3-alpine is pinned by digest, bound only to loopback port 6379 (ORDERFLOW_REDIS_PORT override), and intentionally has no persistence/auth configured for local development. Production auth/TLS/permissions/HA remain later deployment work. Backplane Redis must be near the APIs and its channel prefix must match across the intended replicas while separating other environments/apps.

Realtime defaults disabled: normal API/checkout starts without Redis/RabbitMQ access. Enabled requires explicit private Redis connection configuration. DispatchEnabled=false hosts authenticated connections/backplane only and needs no RabbitMq configuration; true registers the API-hosted consumer. Prefetch defaults 4, allowed 1–16; one subscription for main and one for retry gives at most two active DB reads/dispatches and 2*Prefetch unacknowledged deliveries per API. Each delivery owns its own DbContext scope and disposes it before SignalR network dispatch.

The Worker still runs Notifications__Enabled=true and OutboxPublishing__Enabled=true independently; it never hosts the hub. No API reference to the Worker project is introduced. Shared RabbitMQ settings/topology/subscription now live in Application/Messaging; existing Worker processing remains intact.

## Browser connection contract

Path: **/api/hubs/notifications**. Existing authenticated Identity cookie only; anonymous negotiate/connect is 401. HTTPS is required unless the existing Development-only HTTP opt-in is set. Same-origin browser Origin is enforced, including WebSocket handshakes; no wildcard CORS/cross-origin credential access. Query access_token is rejected; no homemade JWT/token endpoint. A native authenticated client may omit Origin, but a browser supplies it.

Support WebSockets with normal SignalR negotiation. SSE/LongPolling are not offered. Fetch a fresh request token after login from GET /api/auth/antiforgery and include X-CSRF-TOKEN in negotiation headers, with the antiforgery cookie and auth cookie. SignalR JS frontend integration is deferred; it must preserve normal negotiation/credentials and this CSRF header. It must stop its connection on logout. Cookies/security stamps/role changes are not continuously revalidated on an already-established socket; CloseOnAuthenticationExpiration closes at ticket expiry, and API shutdown aborts active connections.

No client subscribe/join/order-group method exists. User ID is one server-authenticated nonempty GUID NameIdentifier, canonical D format via IUserIdProvider. Query/body IDs do not change identity. Any future order-group feature must implement owner checks first; none is added here.

Messages:

| Method | Payload / meaning |
| --- | --- |
| ConnectionReady | No payload; backplane subscription established. Refetch durable state; do not treat this as a notification history replay |
| NotificationAvailable | `{eventId,notificationId,orderId}` only; stable IDs to deduplicate and refetch own notifications/orders |

There is no recipient ID, body/text/email, order amount/status, full event, trace/token or private group name in the hint. Clients must not increment durable unread counters or apply order transitions solely from hints. On ConnectionReady/reconnect, GET /api/notifications (and unread-count/orders as needed) using the cookie; those APIs enforce ownership. Existing connections can miss hints during Redis outages without reconnecting, so frontend should also refresh on focus/manual refresh or an appropriate polling policy. Offline catch-up is through HTTP, not Redis.

Server limits: detailed errors off, max incoming hub message 4096 bytes, one parallel invocation/client, 16 KiB transport/application buffers, ten-second handshake, 15-second keepalive/30-second client timeout and existing bounded auth-read request-rate policy. There are no hub business writes. Connection-ID/token/query values are not included in our request/dependency logs; noisy framework HTTP/SignalR logs are suppressed to Warning.

## Dispatch and failure semantics

Publisher declares a separate durable realtime destination before publication. Only notifications.notification-created binds its work queue. The source event remains the immutable versioned outbox event; no API sends directly from an uncommitted notification/checkout transaction.

| Exchange (default prefix orderflow) | Queue | Failure path |
| --- | --- | --- |
| orderflow.events.v1 | orderflow.realtime.notifications.v1 | exact NotificationCreated binding; NACK(false) -> retry exchange |
| orderflow.realtime.retry.v1 | orderflow.realtime.notifications.delay.v1 | TTL -> retry-ready exchange |
| orderflow.realtime.retry-ready.v1 | orderflow.realtime.notifications.retry.v1 | NACK(false) -> dead exchange |
| orderflow.realtime.dead.v1 | orderflow.realtime.notifications.dead.v1 | Park, no automatic redrive |

These reuse the existing durable quorum/max-length/reject-publish/at-least-once DLX/delivery-limit five policy. Default delay is five seconds (existing RabbitMq RetryDelayMilliseconds). API subscribes to main and retry-work only. No infinite immediate requeue loop. Broker session failures get at most eight attempts per slot/run with 1/5/15/30/60/120/300-second backoff; permanent auth/topology/unknown errors stop visibly. Shutdown cancels work and closes subscriptions; unacknowledged hints can redeliver on another API.

Validate <=64 KiB strict UTF-8/depth 16/object JSON/no duplicate keys, supported envelope/schema v1, header/body EventId/type and NotificationCreated provenance/identities/timestamp. Read durable Notification and compare source/order/type/time/recipient; stored recipient is the destination. Only then dispose scope and call IHubContext.Clients.User. ACK is after the dispatch attempt completes, not after any client receives/processes it. No realtime ProcessedMessages marker is added because external dispatch cannot be made atomic with such a marker. Duplicate hints carry the same EventId/NotificationId and are acceptable.

Official Redis backplane is used on every enabled API with matching Redis/channel prefix. Short Redis connection/command budgets, auto-reconnect and BacklogPolicy.FailFast prevent relying on offline command buffering. Dispatch budget is five seconds. Redis/dispatch timeout/classified transient SQL failures enter finite retry/DLQ; malformed/unsupported/mismatched messages enter the poison path. Durable Notifications/orders/outbox are never removed/regressed by realtime failure. Redis publish success with no active subscriber is still only an attempt. Redis failure/restart can lose messages; no persistence/replay claim.

Safe logs: 2401 poison code, 2402 dependency failure, 2403 attempted hint (not receipt), 2404 broker reconnect, 2405/2406 Redis connection failure/restoration. IDs/instance/type/code only in our dependency logs; no raw Redis config, client tokens or payload contents. A DLQ hint does not mean its durable notification failed: refetch HTTP first. No general replay tool/backfill is added; previously Published NotificationCreated events predating the binding are not automatically replayed.

## Reproducible two-API demo

Executable acceptance demo with real Redis/RabbitMQ/PostgreSQL and two Kestrel API hosts:

```bash
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~Two_API_instances_Rabbit_on_A_reaches_owner_connections_on_B'
```

The test uses two independent API DI/hub/backplane managers, shares only test cookie-key material/PostgreSQL, opens real authenticated WebSockets on B, leaves B's broker consumer disabled, and publishes committed outbox events. It asserts A's consumer log, B's owner's minimal hint, isolation from another customer's connection/query/group attempts, identical duplicate IDs, unchanged durable counts and owned HTTP catch-up. This runs within one test process with actual network dependencies; it does not claim separate OS-process crash testing.

Separate-process manual setup (same private DB/RabbitMq exports and RedisPrefix in both terminals):

```bash
# Terminal A: consumes hints and hosts connections.
export DOTNET_ENVIRONMENT=Development Authentication__AllowInsecureHttp=true
export Realtime__Enabled=true Realtime__DispatchEnabled=true Realtime__InstanceName=api-a
export Realtime__RedisConnectionString="127.0.0.1:${ORDERFLOW_REDIS_PORT:-6379}"
export Realtime__RedisPrefix=orderflow.realtime.v1
dotnet run --project src/OrderFlow.Api -c Release --no-build --no-restore --no-launch-profile -- --urls http://127.0.0.1:5081

# Terminal B: own connections; no broker consumer, proving backplane routing.
export DOTNET_ENVIRONMENT=Development Authentication__AllowInsecureHttp=true
export Realtime__Enabled=true Realtime__DispatchEnabled=false Realtime__InstanceName=api-b
export Realtime__RedisConnectionString="127.0.0.1:${ORDERFLOW_REDIS_PORT:-6379}"
export Realtime__RedisPrefix=orderflow.realtime.v1
dotnet run --project src/OrderFlow.Api -c Release --no-build --no-restore --no-launch-profile -- --urls http://127.0.0.1:5082
```

Run the normal Worker with Notifications/OutboxPublishing. Use an authenticated official SignalR client (the test helper is a complete native example) to negotiate/connect directly to B with its cookie/CSRF token, then create/cancel an order and wait for durable notification processing. A logs 2403; the intended B connection receives only the three-ID hint and refetches. Duplicate the same committed event through the controlled tests; no new durable notification is created. Stop Redis to observe bounded hint failure/DLQ while HTTP notifications remain available, then reconnect/refetch after recovery. Tests refresh randomly mapped Redis fixture ports after restart; Compose uses a fixed host port.

Normal same-user local .NET processes typically share their default Data Protection key directory/application name; explicit protected shared key storage is still required for deployment. The automated demo shares an ephemeral test key provider. Do not infer production cookie continuity or proxy readiness from that fixture. Direct localhost ports intentionally avoid the not-yet-implemented proxy/affinity configuration. Frontend integration remains a later milestone-23 slice.

## Proxy obligations — implement in milestone 26

The supported client uses negotiation, so **sticky sessions remain required with Redis**. Negotiate and WebSocket connect for one connection must reach the same API. Redis moves hub messages, not HTTP connection state. A WS-only client with SkipNegotiation=true is an upstream exception, but not this milestone's supported negotiation contract.

Milestone 26 must implement/test affinity, WebSocket Upgrade/Connection forwarding, Origin preservation, idle/read timeouts compatible with keepalive, no inappropriate response buffering, trusted forwarded scheme/host only from known proxies, and shared protected Data Protection keys/application name across APIs. HTTPS/cookie/CSRF and allowed hosts must reflect the public single origin. No proxy configuration is implemented now, per the request.

See [official scale/affinity guide](https://learn.microsoft.com/en-us/aspnet/core/signalr/scale?view=aspnetcore-10.0) and [Redis failure semantics](https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0). Production Redis auth/TLS/roles/HA, continuous connection revocation and deployment tests remain future work.

## Verification

```bash
dotnet test OrderFlow.slnx -c Release --no-build --no-restore --filter 'FullyQualifiedName~Realtime'
```

Tests cover strict/canonical user identifiers, opt-in/private config, anonymous/CSRF/origin/token/transport rejection, cross-user isolation/unknown groups, two-API backplane, payload minimization, duplicate tolerance, invalid schema/data correlation, real Redis failure/DLQ/HTTP fallback, auth expiration/reconnect catch-up and API shutdown. Synchronization uses real ConnectionReady, client/broker callbacks and safe dependency log signals, not arbitrary sleeps. Exact results/limitations are in [status](implementation-status.md).
