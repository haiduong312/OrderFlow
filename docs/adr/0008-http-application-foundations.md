# ADR-008: Explicit HTTP failures and typed application primitives

Date: 2026-10-30. Status: Accepted. Clarifies ADR-006 money input and extends the bootstrap; does not change the modular monolith or ownership decisions.

## Context

The user requested shared HTTP/application foundations before CRUD/database work. The original 01–29 roadmap had no separate slot for this work and assigned 03 to PostgreSQL. Record this requested addition as milestone **02a**, depending on 02, without renumbering or claiming 03 is complete. Subsequent HTTP features build on this foundation. Existing wording required two-place public money strings while also rejecting input above two places; make the accepted input/output distinction explicit.

## Decision

Use ASP.NET Core 10 IExceptionHandler with explicit application error mappings, the same ProblemDetails DTO for validation/bodyless statuses, stable codes and trace correlation. Unknown failures remain sanitized 500 plus a structured Error event, never assumed database/transient/business errors. Future Customer ownership queries use generic NotFound; Forbidden is endpoint-role denial. Preserve native RequestAborted handling; a cancellation without request abort is an unexpected failure.

Do not log exception objects/messages, submitted values, raw URLs or sensitive headers/bodies. Keep a safe exception type, code, route template, trace and request duration/outcome. Disable API JSON console scopes because native request scopes carry raw RequestPath; the trace is already an explicit event field. Test the configured formatter as well as captured event fields. Rely on .NET 10's suppression of duplicate handled-exception diagnostics; do not turn off all error logging. No new retry policy, authentication or CRUD endpoint is part of this addition.

Introduce small BCL-only primitives in Application/Common: nonnegative UsdMoney, injected-clock UTC/microsecond helpers and validated PageRequest. USD input strings accept zero/one/two fractional places; output is always exactly two places, without rounding or locale dependence. The converter targets UsdMoney only; counts and other decimals remain numeric. Positive prices/refunds remain domain constraints when those features exist. Register TimeProvider in both hosts. Pagination defaults/limits and deterministic timestamp+ID conventions are documented without a generic query abstraction.

Use first-party Microsoft.AspNetCore.OpenApi **10.0.14**, compatible with the existing framework and centrally pinned/locked. Generate OpenAPI 3.1 in Development only, describe the liveness/error contract and transform typed money schemas. No UI package or future endpoint inventory is exposed as if implemented. Controller probes live only in the test assembly, explicitly registered in a test factory; production has no mutating probe endpoints.

## Consequences and verification

Application remains free of ASP.NET/EF/Npgsql packages. Expected-failure exceptions are a simple middle-level choice; do not wrap every failure or add a mediator/result framework. Omitting exception messages reduces accidental disclosure but requires safe trace-based instrumentation for diagnosis. Offset paging is simple but can shift under concurrent inserts. Folder boundaries still rely on review. Input DTO validation and money parsing do not replace future database/business constraints.

Tests must cover binding/field validation, every expected mapping, sanitized unknown/database/cancellation failures, matched body/header/log trace, safe structured logging, true request abort with deterministic synchronization, exact money/invalid tokens and numeric isolation, culture/range/scale, UTC precision/expiry equality, pagination overflow, and OpenAPI environment/schema behavior. No PostgreSQL authorization/order/inventory evidence is claimed by these HTTP-only probes. See [HTTP contract](../http-contract.md) for examples and exact mappings; [status](../implementation-status.md) records actual execution.

Official API references: [ASP.NET Core exception handling](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0), [model validation](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/validation?view=aspnetcore-10.0), [OpenAPI](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview?view=aspnetcore-10.0), and [schema transformers](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/customize-openapi?view=aspnetcore-10.0). Package API signatures were also checked against the installed .NET reference XML and the resolved OpenAPI assembly.
