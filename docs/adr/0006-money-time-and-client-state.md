# ADR-006: Exact money, UTC time, and clear client state ownership

Date: 2026-10-30. Status: Accepted.

## Context

Browser floating-point arithmetic, culture-dependent decimal formatting, client-supplied totals, and wall-clock sleeps can produce misleading prices and flaky expiry tests. Duplicating server entities in multiple client stores makes authority unclear.

## Decision

Use USD-only sales, .NET decimal/PostgreSQL numeric(18,2), positive prices, bounded integer quantities, and server-calculated immutable order totals. JSON money is a two-decimal invariant string. Reject extra precision/overflow. Client math parses decimal strings into bigint cents and formats them explicitly; JavaScript Number is not money storage.

Use injected TimeProvider and UTC DateTimeOffset, persisted/compared at PostgreSQL microsecond precision, with ISO 8601 `Z` public values. ExpiresAt equals CreatedAt plus 15 minutes. Fake time tests define immediately before/equal/after boundaries without real sleeps. Host clock synchronization is an explicit operational assumption.

Use TanStack Query for server entities and invalidation/refetch, Zustand for product-ID/quantity cart and UI choices. Do not persist auth cookies/tokens, authoritative prices, inventory, roles, or payment success in Zustand. Cart prices/countdowns are advisory. Persist the pending checkout key and exact normalized item body to recover an uncertain response; a changed body uses a new key after the user resolves the prior checkout outcome. The backend always revalidates permissions/stock and owns totals/state.

## Alternatives and consequences

Floating-point cents or decimal JSON numbers can lose precision, especially near numeric(18,2) limits. bigint requires explicit JSON/format conversion and an appropriate TypeScript target. A client-only countdown cannot authorize payment. One state library for everything is possible but hides server cache lifecycle. The chosen split is small and explainable, with a limited amount of retry/session coordination.

## Verification

Test decimal strings, exact multiplication/sums, overflow, malformed/excess precision, duplicate item normalization, culture independence, stale cart prices, and fake-time expiry equality. Browser tests prove refetch after mutations and same-key recovery without trusting a frontend payment result. See [money/time contract](../project-spec.md#3-money-quantities-identifiers-and-time).
