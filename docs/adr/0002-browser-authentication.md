# ADR-002: One browser origin and Identity cookie authentication

Date: 2026-10-30. Status: Accepted.

## Context

A React browser client needs user sessions, roles, ownership enforcement, and protection against cross-site requests. There is no v1 mobile client or third-party API consumer requiring bearer tokens.

## Decision

Serve client assets and `/api` through one reverse-proxy origin. ASP.NET Core Identity owns users, password hashing, lockout, roles, and HttpOnly cookies. Require secure host-only cookies in HTTPS, persisted Data Protection keys, API 401/403 rather than login redirects, and server-side ownership filters. Registration assigns only Customer; Admin is operator-provisioned using secrets outside source control.

Validate antiforgery explicitly on every unsafe browser endpoint, including anonymous login/registration and authenticated logout. Provide a no-store request-token endpoint and use `X-CSRF-TOKEN`; refresh tokens after identity changes. The sole business exemption is the provider callback, authenticated with its independent signature protocol and no cookie authority. Keep tokens out of logs and durable frontend stores.

## Alternatives and consequences

Homemade JWT/refresh-token infrastructure adds revocation/storage complexity and is outside v1. Cookie authentication requires antiforgery even with SameSite. One origin simplifies browser deployment, but the proxy, trusted forwarding configuration, shared Data Protection keys, and HTTPS must be verified. A token endpoint alone is insufficient without validation on JSON controllers.

## Verification and reference

HTTP tests must cover missing/invalid antiforgery, login CSRF, role escalation, cross-user access, API status codes, cookie flags, and signed callback isolation. Follow [Microsoft antiforgery guidance](https://learn.microsoft.com/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0), plus the endpoint/ownership contract in [project specification](../project-spec.md).
