# ADR-011: Resource ownership policies and application-facing caller identity

Date: 2026-11-03. Status: Accepted. Supplements [ADR-002](0002-browser-authentication.md) and [ADR-010](0010-identity-cookie-authentication.md).

## Context

Orders/admin entities are not implemented. Their authorization foundations must enforce the existing role/ownership contract without adding fake production controllers or coupling application services to HttpContext. The requested work matches milestone 06 (depends on 04); 05 React remains pending and is not a prerequisite.

## Decision

Add small Application contracts ICurrentUser (nullable caller UUID) and IOwnedResource (server-side OwnerId). HTTP supplies a scoped ICurrentUser adapter using the verified Identity principal. No body/query/header override, default identity or worker impersonation. Fail closed for missing/malformed/ambiguous identity IDs. V1 cookies contain one authenticated identity; roles from an unauthenticated identity do not confer permission.

Use standard ASP.NET Core IAuthorizationService, named policies and AuthorizationHandler implementations. CustomerOwnership requires Customer + matching stored owner. AdminOnly uses an explicit AdminAccessHandler, preserving the existing public policy name; CustomerOnly remains the endpoint role gate. Admin privilege never bypasses Customer ownership, including for dual-role users. The policies have no DB/state mutation dependency; stateless handlers are singleton. Do not add operation-level permissions or an owner-or-admin shortcut before a real requirement exists.

A scoped API ResourceAccess adapter invokes these policies and maps known denials into existing ApplicationFailureException/ProblemDetails: anonymous 401, wrong endpoint role 403, missing/not-owned Customer resource 404, privileged Admin lookup missing 404. Add explicit ApplicationError.Unauthenticated for reuse of the 401 mapping. Do not globally hide every 403 or catch infrastructure failures. Only existence-safe summaries are returned.

Caller identity and resource ownership must come from the server. Future Customer queries include authenticated owner predicates before materialization/count/paging; resource authorization is a second permission gate. Attribute role checks occur before the query, imperative ownership checks after the server loads the projection/entity. Future writes must revalidate persisted state under their transaction/lock protocol. Being authorized does not make an invalid transition valid; business state/deadline/version/stock rules remain application responsibilities after authorization.

Register through the existing browser-auth composition, without another project, package, generic repository, dynamic policy provider or global fallback policy. No migration/model change is needed. Framework AuthorizeAsync has no CancellationToken argument; the adapter checks cancellation before/after it and does no I/O. Future DB queries propagate the real request token.

## Consequences and verification

Authorization is explicit and reviewable, but future controllers/services must call it with server-loaded resources; this milestone cannot prove nonexistent Order SQL. Test-only representative resources/paths exercise the policy/HTTP contract; the application assembly never contains a fake Order. HTTP tests use actual PostgreSQL Identity stores and standard cookies/CSRF, without a test auth scheme. Resource samples alone are test dictionary data, not SQL evidence. Admin permission plus an invalid representative transition still returns 409.

Required future integration and allow/deny matrix: [authorization guide](../authorization.md). Exact checks and remaining A02 criteria: [status](../implementation-status.md). Production Order ownership/fulfillment, browser flows and shared-key deployment remain later milestones.

Official references: [resource-based MVC authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resourcebased?view=aspnetcore-10.0), [policy-based authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/policies?view=aspnetcore-10.0), [HttpContext access through DI](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/http-context?view=aspnetcore-10.0).
