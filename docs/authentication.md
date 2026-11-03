# Browser authentication — milestone 04

One browser origin serves React and /api. ASP.NET Core Identity owns credentials/normalization/lockout; authentication uses HttpOnly cookies. There are no bearer/refresh tokens. See [ADR-010](adr/0010-identity-cookie-authentication.md), [HTTP error contract](http-contract.md) and [PostgreSQL setup](persistence.md).

## Endpoints and rules

| Method/path | Access | Result |
| --- | --- | --- |
| GET /api/auth/antiforgery | Anonymous or authenticated | 200 `{requestToken,headerName}` and HttpOnly antiforgery cookie; no-store |
| POST /api/auth/register | Anonymous + CSRF pair | Email/password only; 201 `{id,email,roles:["Customer"]}`; no automatic login |
| POST /api/auth/login | Anonymous + CSRF pair | 200 user summary and HttpOnly auth cookie |
| POST /api/auth/logout | Authenticated + CSRF pair | 204, clear browser cookie |
| GET /api/auth/me | Authenticated | 200 current id/email/roles |

Email is trimmed, at most 256 characters, validated and normalized by Identity; UserName is the same email. Password is 12–128 characters with uppercase/lowercase/digit/symbol and at least four distinct characters. Invalid/duplicate registration is 400 validation_failed. Every undeclared property, including role, roles, isAdmin, id, emailConfirmed and passwordHash, is rejected. Registration transaction creates user + Customer membership atomically. With an existing session, registration leaves that session unchanged; a successful login replaces it and requires a fresh CSRF token. Admin is not implicitly Customer. No public promotion endpoint exists.

Invalid credentials, unknown, locked or unsupported MFA account: 401 invalid_credentials with one generic message. Five failed password checks lock an account for 15 minutes; updates are serialized in PostgreSQL across API processes. Standard Identity hashing/rehashing is used. Authenticated role denial is 403 forbidden; missing session 401 unauthenticated; no HTML redirects. Later Customer order queries must use owner predicates and indistinguishable 404 for absent/not-owned records.

Missing/invalid/mismatched CSRF pair: 400 antiforgery_failed. Auth HTTP access with secure defaults: 400 https_required. Request JSON limit is 128 KiB. Global MVC unsafe-method validation also applies to future controllers. A signed webhook will need its explicit IgnoreAntiforgeryToken policy and independent signature verification; none exists yet. Test-only foundation probes have an explicit exemption and are not shipped endpoints.

Authentication rate limits are **per API process and remote IP**, fixed 1-minute windows, no queue: register/login/logout together 20 requests; antiforgery/me together 60 requests. Invalid CSRF consumes a write permit too. 429 rate_limited includes Retry-After; another replica has a separate quota. No untrusted X-Forwarded-For is used. NAT clients share an IP quota. Framework partition cleanup applies; distributed/edge throttling belongs to deployment planning.

Example successful registration (UUID illustrative):

```http
HTTP/1.1 201 Created
Location: /api/auth/me
Cache-Control: no-store
Content-Type: application/json

{"id":"11111111-1111-4111-8111-111111111111","email":"customer@example.invalid","roles":["Customer"]}
```

Example missing CSRF:

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json
Cache-Control: no-store
X-Trace-Id: 11111111111111111111111111111111

{"type":"urn:orderflow:error:antiforgery_failed","title":"A valid antiforgery cookie and request token are required.","status":400,"code":"antiforgery_failed","traceId":"11111111111111111111111111111111","errors":{}}
```

## HTTPS and local configuration

Default auth cookie: __Host-OrderFlow.Auth, Secure, HttpOnly, SameSite=Lax, host-only, Path=/. Antiforgery cookie: __Host-OrderFlow.Antiforgery, Secure, HttpOnly, SameSite=Strict. The request token is returned separately to JS; SameSite is additional protection, not the CSRF validation design. Responses are no-store. Session tickets expire after 8 hours without sliding renewal, and security stamp validation runs every 5 minutes. Browser closure normally removes the session cookie; browser restore settings may retain it. Logout clears this browser cookie, not every copied ticket on all devices.

Prefer local HTTPS. Run `dotnet dev-certs https --trust` on a supported development machine, then launch the API with `--urls https://localhost:7080` (normal README build/run arguments). Configure the React dev server/reverse proxy to expose /api on that **same origin** in milestone 05. Curl must trust the development certificate (system trust or `--cacert /private/path/public-dev-cert.pem`); do not disable certificate validation for a deployed service.

For explicit loopback HTTP development only:

```bash
export DOTNET_ENVIRONMENT=Development
export Authentication__AllowInsecureHttp=true
# Keep the existing private ConnectionStrings__OrderFlow setting.
dotnet run --project src/OrderFlow.Api -c Release --no-build --no-restore --no-launch-profile -- --urls http://127.0.0.1:5080
```

This opt-in uses OrderFlow.Dev.Auth / OrderFlow.Dev.Antiforgery and SameAsRequest Secure policy. Non-Development with the flag fails startup. Liveness can still be accessed over HTTP without the opt-in. Normal API/worker startup never migrates or seeds.

Current single-host setup uses the framework local Data Protection key ring and application name OrderFlow; protect that directory and keep it across restarts. Shared durable keys, encryption/access controls, consistent replica application name and TLS/trusted forwarded headers must be implemented in milestone **26** before multi-replica deployment. No shared key store is implemented here.

## Explicit development account

First apply migrations and run the existing roles-only `seed-development` command in [persistence setup](persistence.md). Public registration needs the Customer role provisioned. Missing roles are a sanitized server configuration failure, not successful registration.

To create a local Admin or Customer, set private environment variables through a secret store or a private sourced file (never pass passwords on command lines):

```bash
export DOTNET_ENVIRONMENT=Development
export DevelopmentSeed__Account__Enabled=true
export DevelopmentSeed__Account__Role=Admin
# Privately supply DevelopmentSeed__Account__Email and DevelopmentSeed__Account__Password.
# Both are mandatory; there is no default credential.
dotnet run --project src/OrderFlow.Worker -c Release --no-build --no-restore --no-launch-profile -- seed-development-account
unset DevelopmentSeed__Account__Enabled DevelopmentSeed__Account__Email DevelopmentSeed__Account__Password DevelopmentSeed__Account__Role
```

The command validates Development/opt-in/configuration, provisions roles, then creates the account and membership transactionally with standard Identity password validation. Rerun with same email/role leaves existing credentials untouched. A different role fails; it cannot promote an existing Customer or silently alter existing membership. A bad password fails with no partial user. No account is created during normal host startup. Production account provisioning is not this development command.

## Curl verification with cookies and CSRF

After setup, create a private JSON file containing **only** email and password for a new test customer. Do not add real credentials to the repository, examples or terminal history. Run in Bash with Python 3 available. The example URL assumes the explicitly enabled loopback HTTP mode above; use your trusted HTTPS origin otherwise.

```bash
umask 077
ORDERFLOW_AUTH_URL=http://127.0.0.1:5080
ORDERFLOW_AUTH_JAR=$(mktemp)
ORDERFLOW_AUTH_BODY=/private/path/customer.json
orderflow_csrf() {
  curl --fail --silent --show-error -b "$ORDERFLOW_AUTH_JAR" -c "$ORDERFLOW_AUTH_JAR" \
    "$ORDERFLOW_AUTH_URL/api/auth/antiforgery" |
    python3 -c 'import json,sys; print(json.load(sys.stdin)["requestToken"])'
}
ORDERFLOW_CSRF=$(orderflow_csrf)
curl --fail-with-body -i -b "$ORDERFLOW_AUTH_JAR" -c "$ORDERFLOW_AUTH_JAR" \
  -H 'Content-Type: application/json' -H "X-CSRF-TOKEN: $ORDERFLOW_CSRF" \
  --data-binary "@$ORDERFLOW_AUTH_BODY" "$ORDERFLOW_AUTH_URL/api/auth/register"
# Expected 201; registration alone leaves /me unauthenticated.
curl -i -b "$ORDERFLOW_AUTH_JAR" "$ORDERFLOW_AUTH_URL/api/auth/me"
curl --fail-with-body -i -b "$ORDERFLOW_AUTH_JAR" -c "$ORDERFLOW_AUTH_JAR" \
  -H 'Content-Type: application/json' -H "X-CSRF-TOKEN: $ORDERFLOW_CSRF" \
  --data-binary "@$ORDERFLOW_AUTH_BODY" "$ORDERFLOW_AUTH_URL/api/auth/login"
# Expected 200; obtain a NEW request token for the authenticated identity.
ORDERFLOW_CSRF=$(orderflow_csrf)
curl --fail-with-body -i -b "$ORDERFLOW_AUTH_JAR" "$ORDERFLOW_AUTH_URL/api/auth/me"
curl -i -b "$ORDERFLOW_AUTH_JAR" -X POST "$ORDERFLOW_AUTH_URL/api/auth/logout"
# Expected 400; missing CSRF must leave the session intact.
curl --fail-with-body -i -b "$ORDERFLOW_AUTH_JAR" -c "$ORDERFLOW_AUTH_JAR" \
  -H "X-CSRF-TOKEN: $ORDERFLOW_CSRF" -X POST "$ORDERFLOW_AUTH_URL/api/auth/logout"
# Expected 204, then /me 401. Refresh CSRF again before another anonymous login.
curl -i -b "$ORDERFLOW_AUTH_JAR" "$ORDERFLOW_AUTH_URL/api/auth/me"
ORDERFLOW_CSRF=$(orderflow_csrf)
rm -- "$ORDERFLOW_AUTH_JAR"
unset ORDERFLOW_CSRF ORDERFLOW_AUTH_JAR ORDERFLOW_AUTH_BODY ORDERFLOW_AUTH_URL
```

Do not share verbose curl output containing Set-Cookie/request tokens; these values are authentication material. Cookies live in the temporary private jar, not source control. The above cleanup removes only the temporary jar you just created.

## Browser verification / client handoff

Use DevTools on the same origin: GET antiforgery with `fetch('/api/auth/antiforgery', {credentials:'same-origin', cache:'no-store'})`, retain requestToken in memory, then POST JSON to register/login with credentials same-origin and X-CSRF-TOKEN. Refresh the token after login, logout or a session identity change; other tabs may need a refresh after a 400. Read /me for authoritative roles. Logout is POST with the current pair. Do not read auth/antiforgery cookies in JS or store auth secrets in localStorage. No React UI is included in this milestone.

Verify dev-cookie names only for explicit HTTP mode; HTTPS defaults must show Secure/HttpOnly/SameSite flags. In Production the HTTP opt-in must fail startup, development account seed must refuse, and /openapi/v1.json must stay 404. Test-only role probes are never available in the normal host. Integration tests additionally prove database role membership and concurrency; browser DevTools alone cannot establish those invariants.

Milestone 06 adds reusable CustomerOwnership/AdminOnly policies, ICurrentUser and explicit resource denial handling; see [resource authorization](authorization.md). Existing role policy names, cookie/CSRF endpoints and rate limits are retained. Actual Order routes remain future work.
