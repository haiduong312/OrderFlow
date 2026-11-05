# Resource authorization — milestone 06

This milestone provides reusable policies/handlers, an application-facing identity abstraction and explicit API denial mapping. Milestone 06 originally created no Order entity/endpoint; milestone 12 now integrates actual owner-constrained checkout detail reads as documented below. Representative resources/controllers live only in the integration test assembly. [ADR-011](adr/0011-resource-authorization.md) records the decisions; [authentication](authentication.md) describes the existing Identity cookies/CSRF.

## Components and boundaries

| Component | Responsibility |
| --- | --- |
| Application/Common/Authorization/ICurrentUser | Nullable authenticated user UUID; no HttpContext/ClaimsPrincipal/HTTP dependency |
| HttpCurrentUser | Scoped HTTP adapter; reads current verified principal, never query/body/header userId |
| IOwnedResource | Minimal server-loaded resource/projection contract exposing OwnerId |
| AuthorizationPolicies | Registers CustomerOnly, CustomerOwnership and AdminOnly through ASP.NET Core authorization |
| CustomerOwnershipHandler | Requires a valid authenticated ID, Customer role and exact stored OwnerId match |
| AdminAccessHandler | Requires valid authenticated ID and Admin role; checks permission, not resource existence or business state |
| ResourceAccess | Scoped API adapter that invokes named policies and applies the 401/403/404 concealment contract |

The v1 Identity cookie has one authenticated ClaimsIdentity and one nonempty UUID NameIdentifier. Missing, malformed, empty or ambiguous IDs/identities do not grant ownership/Admin access. Roles must belong to that authenticated identity; unrelated unauthenticated identity claims cannot grant privileges. HttpCurrentUser returns null without a valid application identity or HTTP request, never Guid.Empty/a configured system user. It reads the current context rather than capturing a principal at construction. Background workers must use their explicit application workflow; they do not impersonate a hard-coded browser user.

ICurrentUser identifies the caller; obtaining an ID alone is not a permission check. Policies/handlers use the principal supplied by ASP.NET Core's authorization service. Stateless handlers are singleton; HttpCurrentUser and ResourceAccess are scoped. None query the database or share a DbContext. Application services later use ICurrentUser for server-side owner predicates without depending on ASP.NET APIs. Never implement IOwnedResource on a request DTO or authorize against client-supplied OwnerId.

## Policies and HTTP behavior

CustomerOnly remains the endpoint role gate. CustomerOwnership is an imperative resource policy: load an owner-constrained server projection, then call RequireCustomerOwnershipAsync. Applying only an Authorize attribute cannot inspect an entity that has not been loaded. Do not apply the ownership policy as an attribute: middleware initially receives endpoint context, not your IOwnedResource.

AdminOnly is the privilege policy for admin modules. It can authorize create/list operations without an existing resource. RequireAdminResourceAsync adds the existing-resource check; RequireAdminAsync is available for resource-free operations. Missing resource is never a successful entity lookup simply because Admin permission succeeded.

| Caller | Customer path: own / other / missing | Admin path: existing / missing |
| --- | --- | --- |
| Anonymous | 401 / 401 / 401 | 401 / 401 |
| Customer | Allow / 404 / 404 | 403 / 403 |
| Admin only | 403 / 403 / 403 | Allow / 404 |
| Admin + Customer | Allow / 404 / 404 | Allow / 404 |
| Authenticated without required role | 403 / 403 / 403 | 403 / 403 |

Admin is not implicitly Customer. Dual-role users still must own a resource on the Customer path. Privileged access belongs to explicit admin paths. Missing and not-owned resources share the same generic not_found response, with no owner/resource metadata. Only per-request trace correlation differs. Do not globally translate every forbidden response into 404.

ResourceAccess first checks authentication, then endpoint role/privilege, then resource ownership/existence. Anonymous throws ApplicationError.Unauthenticated (401); wrong role throws Forbidden (403); missing/not-owned throws NotFound (404). Framework endpoint challenges/forbids use the same ProblemDetails mapping, no HTML redirect. A malformed authenticated principal is denied by the resource policy (404 on Customer ownership, 403 on Admin privilege); it never acquires a synthetic ID. Unexpected infrastructure exceptions keep their existing sanitized 500 behavior.

Example non-owner **or missing** response (trace illustrative):

```http
HTTP/1.1 404 Not Found
Content-Type: application/problem+json
Cache-Control: no-store
X-Trace-Id: 11111111111111111111111111111111

{"type":"urn:orderflow:error:not_found","title":"The requested resource was not found.","status":404,"code":"not_found","traceId":"11111111111111111111111111111111","errors":{}}
```

Endpoint role denial is 403 forbidden; absent authentication is 401 unauthenticated. A permitted caller attempting an invalid domain transition receives the operation's 409 state_conflict/order_expired/version_conflict after the application enforces its transaction rules. No authorization handler checks or changes order state, inventory, deadlines or payments.

## Required integration in later features

1. Mark Customer/admin endpoints with CustomerOnly/AdminOnly before loading data. Do not accept caller userId/role/OwnerId as authority. Public auth and health behavior is unchanged; no new global fallback policy is added.
2. Obtain caller ID through ICurrentUser in application services. Load Customer order/payment/refund data using resource ID **and owner ID** predicates; preserve generic missing behavior. Lists must apply ownership before pagination/counting.
3. Supply only a server-loaded IOwnedResource to RequireCustomerOwnershipAsync. For explicit admin detail/mutation use RequireAdminResourceAsync; for admin create/list use RequireAdminAsync. Do not convert a successful role check into an ownership bypass.
4. After permission succeeds, enforce all business rules server-side in the actual application unit of work. For writes, load/lock/revalidate against persisted state as required, then apply guarded updates/history/outbox atomically. Authorization is not a transaction, state machine or concurrency guard.
5. Preserve global CSRF validation for unsafe browser methods, including privileged requests. Resource authorization does not replace antiforgery.
6. Milestones 13/19 must add real PostgreSQL Order query/HTTP tests for own/other/missing IDs, detail/list/payment/refund ownership, actual cancellation/fulfillment states and rollback/concurrency. Milestones 26/27 still implement replica/browser acceptance. Existing representative-resource tests do not prove those SQL paths.

There is no owner-or-admin shortcut, generic CRUD permission matrix, operation-name reflection or database permission table in v1. All current privileged operations require Admin; distinct permissions can be designed when a real use case requires them.

## Verification

After README restore/build and private PostgreSQL test setup:

```bash
# Pure policy/identity/denial mapping composition tests (14 cases; no external service).
dotnet test tests/OrderFlow.IntegrationTests -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~AuthorizationPolicyTests'
# HTTP resource authorization with real Identity cookies/PostgreSQL (6 cases).
dotnet test tests/OrderFlow.IntegrationTests -c Release --no-build --no-restore \
  --filter 'FullyQualifiedName~ResourceAuthorizationHttpTests'
# Full suite, including existing cookie/CSRF/SQL regressions.
dotnet test OrderFlow.slnx -c Release --no-build --no-restore
```

Use the same --artifacts-path across restore/build/test if you build outside the repository. Full commands and observed results are recorded in [implementation status](implementation-status.md).

Focused policy cases exercise roles/ownership, null/wrong-type resources, invalid/duplicate IDs, ambiguous identities, unauthenticated role claims, no request context and cancellation. HTTP cases persist generated users with real Identity stores and authenticate through the production login endpoint, cookie handler and CSRF flow; no test authentication scheme/bypass is installed. Only representative resources use an isolated test dictionary, not EF InMemory. Missing/not-owned HTTP problems compare equal except traceId; spoofed query/header userId/role cannot override the cookie. Admin still needs CSRF and gets 409 for the test resource's closed-state rule.

Run the normal API and request /_tests/resource-authorization/customer/{a-generated-uuid}: it must be 404 because no test application part is registered. No production URL can be used to exercise a fake Order or change representative data. Actual production ownership walkthroughs are deferred until Order endpoints exist; the auth curl/browser session walkthrough remains in authentication.md.

## Actual Order detail integration (12)

POST /api/orders and GET /api/orders/{id} now require the existing CustomerOnly policy. The detail query constrains both order ID and authenticated UserId in PostgreSQL; other-owner/missing returns the same 404, and Admin-only users remain 403 on this Customer path. Checkout and successful replay require the normal antiforgery cookie/token pair. Owner/user/role fields are never accepted in the body. Real PostgreSQL HTTP tests cover these paths; representative-resource probes remain useful policy contract tests. Order listing/cancellation/Admin inspection remain later features. See [checkout guide](checkout.md).
