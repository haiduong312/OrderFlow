# Architecture decision records

Records below are accepted design decisions; each record supplies its decision date. Accepted does not mean implemented; see [implementation status](../implementation-status.md). IDs are permanent. Supersede an ADR with a new numbered record and link both records instead of silently rewriting a material decision.

| ID | Decision |
| --- | --- |
| [ADR-001](0001-modular-monolith.md) | Modular monolith, feature folders, shared context and one database |
| [ADR-002](0002-browser-authentication.md) | Same-origin browser, Identity cookies and explicit antiforgery |
| [ADR-003](0003-inventory-and-order-lifecycle.md) | PostgreSQL inventory coordination, reservation lifecycle and expiry |
| [ADR-004](0004-command-idempotency.md) | Durable command identities and exact CreateOrder response replay |
| [ADR-005](0005-payments-and-durable-delivery.md) | Simulated gateway, captured-funds compensation and at-least-once delivery |
| [ADR-006](0006-money-time-and-client-state.md) | Exact USD amounts, UTC time and client state ownership |
| [ADR-007](0007-solution-bootstrap.md) | Initial solution, host composition, folder boundaries and reproducible dependency baseline |
| [ADR-008](0008-http-application-foundations.md) | Shared HTTP error/trace contract, typed money/time/pagination and development OpenAPI |
| [ADR-009](0009-postgresql-reliability-foundations.md) | PostgreSQL/Identity storage, shared reliability tables, dedicated migrations and explicit dev seed |
| [ADR-010](0010-identity-cookie-authentication.md) | Identity cookies, global JSON CSRF validation, roles/rate limits and explicit local account bootstrap |
| [ADR-011](0011-resource-authorization.md) | Resource ownership/Admin policies, application-facing caller identity and explicit denial mapping |
| [ADR-012](0012-catalog-products-categories.md) | Product/Category schema, archival visibility, version/lock coordination and literal substring search |
| [ADR-013](0013-inventory-ledger-and-stock-commands.md) | Inventory balances/initialization, append-only audit, ordered row locks and stock operation replay |
| [ADR-014](0014-integration-test-harness.md) | Per-case migrated PostgreSQL/Testcontainers lifecycle, real HTTP security helpers and safe race boundaries |
| [ADR-015](0015-idempotent-checkout.md) | Atomic Customer checkout, duplicate-ID rejection, exact request/response identity and immutable order/reservation snapshots |
| [ADR-016](0016-order-management-transitions.md) | Owner-scoped order views, effective expiry, serialized cancellation/release and Admin fulfillment with preserved status history |
| [ADR-017](0017-order-expiry-worker.md) | Bounded per-order SKIP LOCKED expiry, shared release logic and System audit actors |
| [ADR-018](0018-payment-initiation-and-simulator.md) | Durable payment attempts, fenced gateway observations and restricted separate simulator host |
| [ADR-019](0019-signed-payment-settlement.md) | Signed raw-byte callback receipts, monotonic captures, atomic consumption/confirmation and unique compensation intentions |
| [ADR-020](0020-refund-execution-and-payment-reconciliation.md) | Durable full-compensation execution, lookup settlement, shared lease fencing and provider-dependent recovery guarantees |

| [ADR-021](0021-rabbitmq-outbox-publication.md) | RabbitMQ mandatory/confirmed outbox handoff, fenced leases and finite durable retry topology |

| [ADR-022](0022-durable-user-notifications.md) | Order notification consumer, transactional inbox/effects/outbox, owner reads and commit-before-ACK |

| [ADR-023](0023-order-confirmation-email-delivery.md) | Durable confirmation email snapshots, sending leases, provider capability/SMTP uncertainty and audited Admin retry |

| [ADR-024](0024-signalr-notification-hints.md) | Authenticated API-hosted notification hints, official Redis backplane, bounded RabbitMQ dispatch and proxy-affinity boundary |

| [ADR-025](0025-bounded-catalog-cache.md) | Distributed-only catalog detail cache-aside, source-anchored TTL, postcommit invalidation and DB fallback |

| [ADR-026](0026-direct-admin-reporting.md) | Direct Admin report snapshots, distinct paid/captured/refunded/net semantics and bounded read-only audit |

| [ADR-027](0027-react-session-foundations.md) | React session UX, typed cookie/CSRF fetch, scoped query lifecycle and UI-only persistence |

| [ADR-028](0028-catalog-cart.md) | URL-scoped customer browsing, bounded device cart persistence and advisory exact-money review |

| [ADR-029](0029-checkout-recovery-and-live-ui.md) | Per-user checkout/payment recovery, owner financial status and cookie SignalR durable catch-up UI |

| [ADR-030](0030-admin-workspace.md) | Admin management/report/audit views, targeted invalidation and same-identity stock/email recovery |

| [ADR-031](0031-opentelemetry-and-readiness.md) | Optional OpenTelemetry, durable event trace lineage, safe structured logs/metrics and intentional API/Worker readiness |

| [ADR-032](0032-replicated-compose-deployment.md) | Local replicated TLS Compose, shared encrypted cookie keys, hub affinity, explicit migrations and process recovery |

| [ADR-033](0033-github-actions-and-browser-ci.md) | Pinned GitHub Actions gates, disposable real dependencies, serialized browser acceptance and private failure artifacts |

| [ADR-034](0034-disposable-failure-and-load-lab.md) | Bounded disposable k6/fault scenarios, predefined invariants, explicit cache conditions and honest local measurements |

| [ADR-035](0035-acceptance-audit-and-portfolio-handoff.md) | Honest acceptance audit, explicit protected key bootstrap, executable portfolio demo and claim-to-evidence handoff |

The [specification](../project-spec.md) defines observable behavior; [architecture](../architecture.md) defines implementation boundaries. If these disagree, report the conflict before implementing the affected behavior, then update the contract and relevant ADR together.
