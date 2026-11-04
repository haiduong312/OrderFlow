# Unit tests

This project references Application, not either host. Milestone 02a adds meaningful primitive tests for invariant USD money/range/scale, injected-clock UTC precision/expiry boundaries, and pagination bounds/overflow. Domain feature tests are still added with their own features. HTTP/composition/serialization/error/logging probes are in OrderFlow.IntegrationTests; there is no always-passing placeholder test or EF InMemory database substitute.

Milestone 03 adds six snapshot-completion guard/buffer-isolation tests (43 unit cases total). SQL uniqueness, FK, immutable-trigger, shared-transaction and provider-conversion behavior are verified separately against actual PostgreSQL in IntegrationTests.

Milestone 04 adds development-account configuration guards; milestone 07 adds SKU/name/price/category/version/archive domain tests. Current unit suite: 57 cases. Catalog SQL/constraints, concurrency, HTTP/permissions and OpenAPI are covered against PostgreSQL separately.
