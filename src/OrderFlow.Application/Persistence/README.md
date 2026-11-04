# Persistence boundary

Contains the single scoped Identity-compatible OrderFlowDbContext, explicit Identity/reliability/Catalog mappings, converters, design-time factory and migrations. Configuration is private via ConnectionStrings__OrderFlow; hosts never migrate automatically. See docs/persistence.md for dedicated migrate/seed commands and actual PostgreSQL tests.

Future application use cases share one context for an atomic operation, including idempotency response/outbox/inbox writes. Never share that instance across parallel tasks or commit a claim in an independent store, and do not wrap every EF operation in a generic repository. Runtime hosts compose the application; schema changes are explicit operator actions. Catalog alone owns Categories/Products. No order/inventory/payment entity is generated before its feature milestone.
