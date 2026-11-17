# Replicated local deployment

This is a loopback-only deployment rehearsal, never a command to publish externally. Read [ADR-032](adr/0032-replicated-compose-deployment.md). Requires Docker Engine/Compose2.24.4+ (compatibility override uses !override), Python3.12+, OpenSSL and registry access. Verification additionally uses repository-pinned .NET/Node/npm, `npm ci --prefix client` and the installed Playwright Chromium. No production credentials are included.

## Start in explicit phases

From repository root, create **fresh** ignored private inputs once. The setup refuses an existing directory. Preserve the directory with its volumes; changing passwords does not update an already initialized PostgreSQL/RabbitMQ volume.

```bash
python3 infra/deployment/setup.py
dc=(docker compose -f compose.deploy.yaml --env-file .local/deployment/deployment.env --profile deploy)
"${dc[@]}" config --quiet
"${dc[@]}" build api1 worker1 proxy
"${dc[@]}" up -d --wait --wait-timeout 120 postgres rabbitmq redis
# Explicit one-off schema-owner step; never run by the API/worker on startup.
"${dc[@]}" run --rm migrate
# Optional local demonstration account; generated password is not printed or reset.
"${dc[@]}" run --rm bootstrap-admin
"${dc[@]}" up -d --wait --wait-timeout 150 api1 api2 worker1 worker2 proxy
curl --cacert .local/deployment/tls.crt https://localhost:8443/api/health/ready
"${dc[@]}" ps
```

Visit `https://localhost:8443`. Trust the generated localhost certificate explicitly in a development browser, or use the test's exact certificate/public-key pin. Do not disable certificate validation in application code. The generated Admin credentials are in `.local/deployment/bootstrap.json`; use a private editor. Registration is Customer-only. Base payment initiation can remain pending while delivery workers are disabled; enable the optional simulator flow below to demonstrate settlement. No pretend payment success UI is supplied.

Only edge TLS8443 is published, bound127.0.0.1. API8080, Worker, PG5432, RabbitMQ5672/management and Redis6379 are private. The internal backend network has no external gateway; proxy also joins the separate edge network for loopback publication. PostgreSQL-only base development is still `docker compose --profile dev up -d --wait postgres` with the existing private DB password file; add messaging/email/cache/realtime profiles only when needed.

On **this tested snap Docker/AppArmor environment**, `docker run --security-opt no-new-privileges:true ... /bin/sh` fails with operation not permitted. Standard Docker is the intended hardened configuration. For local rehearsal on that affected daemon only, insert `-f infra/deployment/compose.snap-compat.yaml` into the array before --env-file. It disables no-new-privileges while retaining non-root, dropped capabilities, read-only roots and network boundaries. It is not a production hardening fix. Do not disable AppArmor globally.

## Configuration and storage

`setup.py --directory .local/<name> --project <name> --port 8443 --subnet 172.29.240.0/24 --proxy-ip 172.29.240.10` supports isolated projects/ports/subnets. Choose a nonconflicting subnet; the proxy address must match Deployment__ProxyAddress. `ORDERFLOW_PUBLIC_HOST` is a DNS hostname only; TLS SAN, allowed host, proxy server_name and public URL must agree. The supplied self-signed cert is localhost-only. Values after setup can be reviewed privately in generated deployment.env/JSON. Never run shell tracing, print secrets, or paste expanded `compose config` into logs. `config --quiet` validates without dumping them.

Private JSON is mounted read-only at /run/secrets/settings.json via ORDERFLOW_SETTINGS_FILE. Provider precedence is standard defaults, private file, environment, CLI. Runtime connection/password secrets are absent from Docker build context/image layers and public frontend. `.local/` and credential/certificate patterns are Docker/Git ignored; setup rejects another directory inside the checkout/build context (an external private directory is allowed). Generated host directory is0700; individually bind-mounted files0444 permit container identities to read only selected mounts. Docker host root/daemon and that directory's owner can access them. Compose file secrets do **not** provide encryption at rest; protect the host, secret directory and backups, and use an appropriate deployment secret manager/KMS in production.

Named project-scoped durable volumes: PostgreSQL, RabbitMQ (fixed node identity), Redis AOF, shared Data Protection and optional Mailpit. No developer volume names are reused. Data Protection directory is0700/UID1654; XML master keys are certificate-encrypted, with the wrapping PFX/password mounted only into APIs. Both APIs use the same application discriminator and cookie/AF purposes. Keep key-ring, wrapping certificate/private key/password and database backups recoverable together, with separate restricted access. Copying only PostgreSQL cannot restore encrypted cookies.

Do not overwrite the wrapping certificate during an upgrade: the current implementation decrypts with the configured certificate. Rotation needs an explicitly configured old/new decryption certificate set and tested key lifecycle first. Back up/protect certificates at rest; production may require HSM/KMS-backed wrapping rather than a local exportable PFX. Do not commit private keys or production connection strings.

PostgreSQL new-volume init creates separate bootstrap, migrator, API, Worker and simulator logins. Migrator owns the app DB/schema; runtime roles are NOSUPERUSER/NOCREATEDB/NOCREATEROLE, do not own tables and cannot modify migration history. Fixed grants preserve existing append-only audit triggers. Grant step runs after every explicit migration so new objects receive intended access. The bootstrap superuser password is mounted into PG only, never runtime hosts. Existing initialized volumes do not rerun init scripts: review/provision missing roles as an operator without resetting the volume. Upgrade/rollback requires reviewed migration SQL, backups and compatible binaries; this profile does not guarantee rolling schema compatibility.

## Same origin, TLS and SignalR

nginx serves built assets, SPA history routes, /api and /api/hubs/notifications under one HTTPS host/port. Only nginx's exact backend IP is trusted for one hop of forwarded proto/host/client IP; it overwrites spoofed incoming forwarding headers. No arbitrary network/0.0.0.0 trust or client-supplied role. HTTPS is observed before auth/origin/rate-limit checks. Production guidance: use a valid renewed certificate, restricted secrets, configured public host and edge ports/firewall; add HSTS after testing the real domain. If adding another proxy, explicitly redesign trusted hops/addresses and overwrite rules. Internal PostgreSQL/RabbitMQ/Redis/API HTTP traffic here uses a private Docker bridge; production networks may require dependency TLS/mTLS, certificate validation, broker permissions, firewalling and managed storage.

Hub supports WebSockets **with negotiation**, not SSE/long polling. The path-scoped __Secure-OrderFlow.Route cookie pins negotiation/upgrade to api1 or api2; it is Secure/HttpOnly/Lax and grants no authorization. WebSocket Upgrade/Connection, buffering and75s proxy idle timeouts are explicit; server pings every15s. Ordinary API traffic round robins and shared keys make identity/AF portable. Redis backplane delivers hints to connections on either node but does not share negotiation tokens. Replacing a node disconnects its sockets; client renegotiates/refetches durable notifications/orders. Failover during negotiation may fail and require a fresh negotiation. Duplicate/missing hints are harmless with durable catch-up. Fallback transports stay disabled; enabling them requires extending/testing affinity and client/server support.

Docker DNS refreshes upstream IPs every5s; passive failure windows are5s. Existing sockets do not migrate. Proxy does not retry upstream POSTs (or silently retry other calls); a transient502/503 requires client recovery. Checkout retains its exact original intention/key after uncertainty, and server SQL arbitrates across replicas. X-OrderFlow-Instance is enabled for local diagnosis; turn Deployment__ExposeInstanceHeader off if internal node names should not be public.

## Readiness, retries and shutdown

API /api/health/live describes HTTP responsiveness. /api/health/ready checks PostgreSQL/schema only,200/503/no-store; it does not require broker/email/Redis/exporter. Startup waits for database readiness only. Outbox can buffer during delivery outages subject to finite storage/capacity. Worker check-readiness additionally checks configured broker/SMTP; it proves reachability, not forward progress. Core deployment phases check broker/Redis readiness intentionally before the complete demo, without making API Compose startup depend on either.

StartupReadiness tries8 probes with2s gaps, using existing dependency deadlines (DB2s, broker2s, SMTP1s), then throws. This retries probes, never financial/stock mutations. Services use bounded on-failure:3 container policy; restart-policy counters are Docker semantics, not an overall deployment deadline or successful recovery guarantee. Health status alone does not cause restart. Fatal background tasks stop the host and now produce exit1; SIGTERM normally exits0. Operator must inspect repeated/fatal failures, repair configuration and explicitly start stopped hosts. API optional dispatch instead suspends after classified finite reconnect attempts and logs event2407/processing failure; HTTP stays available. Repair/restart API to resume dispatch; do not infer live delivery from API readiness.

Host graceful shutdown budget25s, Docker30s; nginx SIGQUIT drains up to20s. PostgreSQL outbox/inbox/leases remain the recovery authority if killed. A process that dies after publish/send may redeliver; SMTP is not exactly-once, stable payment/refund identities remain mandatory. API rate limiting is per-process, per-IP fixed windows:20 auth writes and60 auth reads/minute; two replicas may admit more, restart resets local counters. No strict cluster-wide quota is claimed.

## Optional local adapters and telemetry

```bash
demo=(docker compose -f compose.deploy.yaml -f compose.deploy.demo.yaml --env-file .local/deployment/deployment.env --profile deploy --profile demo)
"${demo[@]}" build simulator
# Separate provider database, explicit schema step before starting simulator.
"${demo[@]}" run --rm simulator migrate
"${demo[@]}" up -d --wait --wait-timeout 150
```

Optional simulator operator endpoint is loopback5091, Mailpit UI loopback8026; no simulator/admin secret goes into the browser. Workers switch to Development for the existing simulator/SMTP adapters; APIs remain Production secure cookie hosts. Explicit validators permit exactly simulator:8090, mailpit and .invalid recipients; this is not an unrestricted relay/HTTP target or production gateway. Use existing [payment simulator](payments.md), [callback](payment-callbacks.md), [refund](payment-recovery.md) and [email](email-delivery.md) contracts. Base profile does not enable financial settlement/delivery jobs without a configured provider.

For telemetry, add `-f compose.deploy.telemetry.yaml --profile telemetry` to the relevant command. It enables redacted OTLP to exactly telemetry:4318 on the private bridge and optional pinned LGTM/Grafana on loopback3001. Static Telemetry__InstanceName values api1/api2/worker1/worker2 become service.instance.id so replica counters do not overwrite the same series; never put request/order/user IDs in this setting. Dashboard shared-DB gauges use MAX, while per-process counter rates can be summed. No app depends_on collector; unavailable exporters must not prevent startup. Dashboard `/d/orderflow-operations` and investigation thresholds are described in [observability](observability.md). This optional private HTTP target is explicit; outside this local bridge use HTTPS and appropriate collector auth/retention. Telemetry data is disposable in this profile; business volumes are durable.

After starting the demo overlay, `ORDERFLOW_DEPLOYMENT_VERIFY_CONFIG="$PWD/.local/<verification-directory>" node infra/deployment/demo-smoke.mjs` exercises a real Customer checkout, simulator submission, signed capture, Confirmed allocation and Mailpit acceptance. This helper accepts only an orderflow-verify-* project, reads machine credentials privately and never returns them to the browser. With telemetry enabled, optionally set ORDERFLOW_VERIFY_TRACE_FILE to an artifact path outside Git; search its checkout traceId in Tempo to see API/Worker checkout.create, outbox.publish and notifications.handle. Query `count by (instance,job) (orderflow_outbox_pending)` in Prometheus to see the four fixed resource identities. No displayed redirect proves payment.

## Reproducible acceptance

```bash
# Prerequisites: dotnet restore --locked-mode; npm ci --prefix client;
# cd client && npx playwright install chromium (if not installed).
bash infra/deployment/verify.sh
# Affected snap daemon only:
ORDERFLOW_VERIFY_SNAP_COMPAT=1 bash infra/deployment/verify.sh
```

Script creates a fresh orderflow-verify-* project and private .local inputs, exposes PostgreSQL on loopback15432 only with compose.verify.yaml, builds images, checks dependencies, explicitly migrates/bootstrap-seeds, then runs real multi-process tests and Chromium smoke. Override ORDERFLOW_VERIFY_PORT/PG_PORT/SUBNET/PROXY_IP if occupied. Tests pin only the generated TLS certificate/public key, never globally trust all certificates. Tests require both APIs to appear in pg_stat_activity waiting on actual checkout locks before release, and both workers to reach a test-only advisory barrier on distinct SKIP LOCKED rows. The temporary trigger exists only in the owned verification DB and is removed in finally. No test endpoint/auth bypass is added.

Checks include auth and AF across APIs, Customer admin denial, forwarded-header overwrite, same-key replay, last-unit stock contention, cross-node hub hints, two-worker claims, pending outbox recovery after PG/broker/Redis restart and API replacement, original cookies/AF/replay, restricted DB roles, built SPA login/restore/mobile layout. Existing suites cover detailed lease fencing/ACK loss/payment/refund recovery. The deployment test is deliberately skipped in ordinary solution tests; skipping is not acceptance evidence. Exact executed results/limits are in [status](implementation-status.md).

On exit script stops/removes **only its own** containers/network with Compose down, preserving volumes and private inputs. Set ORDERFLOW_VERIFY_KEEP_RUNNING=1 to inspect the rehearsal. Never use down -v or remove existing volumes. For the manual profile stop with `"${dc[@]}" down --timeout 30` (no volume removal). No high availability, production restore drill, external publication, global limiter or transport fallback guarantee is implied.
