# Architecture decision records

All records below are accepted design decisions dated 2026-10-30. Accepted does not mean implemented; see [implementation status](../implementation-status.md). IDs are permanent. Supersede an ADR with a new numbered record and link both records instead of silently rewriting a material decision.

| ID | Decision |
| --- | --- |
| [ADR-001](0001-modular-monolith.md) | Modular monolith, feature folders, shared context and one database |
| [ADR-002](0002-browser-authentication.md) | Same-origin browser, Identity cookies and explicit antiforgery |
| [ADR-003](0003-inventory-and-order-lifecycle.md) | PostgreSQL inventory coordination, reservation lifecycle and expiry |
| [ADR-004](0004-command-idempotency.md) | Durable command identities and exact CreateOrder response replay |
| [ADR-005](0005-payments-and-durable-delivery.md) | Simulated gateway, captured-funds compensation and at-least-once delivery |
| [ADR-006](0006-money-time-and-client-state.md) | Exact USD amounts, UTC time and client state ownership |
| [ADR-007](0007-solution-bootstrap.md) | Initial solution, host composition, folder boundaries and reproducible dependency baseline |

The [specification](../project-spec.md) defines observable behavior; [architecture](../architecture.md) defines implementation boundaries. If these disagree, report the conflict before implementing the affected behavior, then update the contract and relevant ADR together.
