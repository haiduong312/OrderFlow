# Shared HTTP and application foundations (milestone 02a)

This implements foundations only. The production API still has no CRUD, authentication, order lookup, or database. The only business-facing route remains GET `/api/health/live`; Development additionally exposes GET `/openapi/v1.json`. Test-only controllers exercise the shared pipeline and are never registered in the shipped host. See [ADR-008](adr/0008-http-application-foundations.md), [specification](project-spec.md), and [implementation status](implementation-status.md).

## Error representation and mapping

Every foundation-generated HTTP error before a response starts uses `Content-Type: application/problem+json` and this shape:

| Field | Meaning |
| --- | --- |
| `type` | Stable problem URI `urn:orderflow:error:{code}` |
| `title` | Fixed safe summary; no exception message, attempted value or resource-owner detail |
| `status` | Actual HTTP status, also set on the response |
| `code` | Stable application code; clients branch on this rather than title text |
| `traceId` | Server request trace shared with `X-Trace-Id` and structured request/failure logs |
| `errors` | Object of field names to arrays of safe validation messages; `{}` for non-validation failures |

`detail`, `instance`, stack traces, SQL, exception/inner-exception messages and attempted values are omitted. Errors set `Cache-Control: no-store`. Do not echo user IDs or request URLs into problems. MVC's invalid-model-state factory uses camelCase validation metadata and generic field messages because framework binding errors can include submitted values. Explicit application validation can use developer-authored messages, never interpolated input/secrets. JSON binding paths may use keys such as `$.amount`; whole-body failures may use `$` or an empty key.

| HTTP | Stable code | Source / rule |
| --- | --- | --- |
| 400 | `validation_failed` | Model validation, malformed JSON/binding, invalid money/scale/range, pagination, or explicit application validation |
| 401 | `unauthenticated` | Explicit unauthenticated response; future Identity challenge must return this status rather than redirect |
| 403 | `forbidden` | Endpoint-role/operation denial; not Customer ownership denial |
| 404 | `not_found` | Absent resource **or not owned**; same generic title/body structure; also unmatched routes |
| 405 | `method_not_allowed` | Unsupported method |
| 409 | `state_conflict` | Invalid state; default bodyless conflict |
| 409 | `idempotency_conflict` | Same idempotency identity, different normalized request |
| 409 | `insufficient_stock` | Guarded reservation/stock conflict |
| 409 | `version_conflict` | Stale optimistic version |
| 409 | `order_expired` | Deadline conflict; future overdue command may first commit expiry as specified |
| 413 | `request_too_large` | Framework/endpoint request-body limit |
| 415 | `unsupported_media_type` | Unsupported body format |
| 429 | `rate_limited` | Explicit throttle response |
| 500 | `internal_error` | Any unexpected exception, including an unclassified database/infrastructure failure or cancellation without RequestAborted |
| 503 | `service_unavailable` | Explicit response from a positively classified transient failure; no automatic exception/SQL retry classification exists yet |
| Other 4xx/5xx | `http_error` | Bodyless status fallback, preserving its actual status |

The Application assembly defines a small `ApplicationError` enum and `ApplicationFailureException`, independent of ASP.NET/HTTP. Expected failures map explicitly in the API. Future commands may throw this exception only for known business failures. Do not wrap every exception in it. A programming error such as ArgumentException is not automatically validation. Unknown database/provider exceptions are not converted to empty data, HTTP 200, a business conflict, or an assumed transient 503.

ASP.NET Core 10 `AddExceptionHandler<ApiExceptionHandler>`/`UseExceptionHandler` handles exceptions; status-code pages fill bodyless errors; `[ApiController]` validation uses the same contract. The handler sets the full response and returns handled, with 404 explicitly permitted. The .NET 10 default suppresses duplicate diagnostics for handled exceptions; our handler emits one structured Error event for each unexpected failure with exception type, code and trace. It intentionally does not pass the exception object/message to the logger. Failures are recorded, not hidden; raw exception text is sacrificed to avoid logging connection strings/payloads. Use trace/type/route and targeted safe instrumentation for diagnosis.

The fixed JSON error contract also applies when a caller requests HTML/text. Once response bytes have started or the transport is gone, a new complete ProblemDetails body cannot be guaranteed; never attempt to rewrite a streamed response into successful JSON. Native server failures before the application pipeline and post-start transport diagnostics are outside the pre-response contract.

## Ownership and future endpoint use

Use owner-constrained database queries for Customer order/attempt/refund access. Missing and not-owned cases must both raise `ApplicationError.NotFound` without first returning existence/owner metadata. Use Forbidden for endpoint-role denial only; Admin access stays on explicit admin endpoints. This milestone implements the common safe mapping, not ownership queries or authentication; actual authorization/SQL tests remain mandatory when those features are added. Do not turn every Forbidden into NotFound globally.

## Correlation, logs and cancellation

At request entry, capture the current W3C Activity TraceId (or server TraceIdentifier fallback). Return it in `X-Trace-Id`; ignore client `X-Trace-Id` as authority. A valid standard traceparent can participate in distributed tracing, but a trace is never an authentication/ownership identity. The same value appears in the error body and log fields.

One structured request event contains Method, route **template**, Outcome, StatusCode, safe application Code when available, monotonic elapsed milliseconds and TraceId. Exception middleware clears the current endpoint on failure, so logging reads its saved original endpoint from IExceptionHandlerFeature. Never log the raw path/route values, query string, cookies, Authorization/CSRF/signature headers, body, or configuration. JSON console formatting remains enabled, but API IncludeScopes is false: framework request scopes contain raw RequestPath even when the event itself uses a template. TraceId is already an explicit event field. A regression test feeds sensitive scope markers through the actual configured JSON formatter. Existing Microsoft.AspNetCore log level stays Warning to avoid its informational raw-URL request logs; enabling API scopes, increasing framework verbosity or adding a new sink requires explicit scope/value redaction first.

RequestAborted cancellation/associated I/O is handled by ASP.NET Core's exception middleware, without a business ProblemDetails or Error event. The request log marks `Outcome=Aborted`, with 499 as an internal diagnostic status (not a promised response to a disconnected client). Cancellation without an aborted request is not assumed harmless and becomes a sanitized 500. Unhandled post-start failures have Outcome=Failed with no fabricated successful status. Async operations must continue to receive RequestAborted or the worker shutdown token. The deterministic cancellation test waits for action entry, cancels the real client token, and waits for the request log; no sleep establishes ordering.

## USD money and JSON

Use `UsdMoney` for monetary DTO fields; do not install a converter for every decimal or globally stringify numbers. Both MVC and Minimal API serializer options contain only the typed USD money converter. Ordinary int/decimal fields remain JSON numbers. Unknown JSON properties and quoted ordinary numeric fields are rejected rather than silently accepted.

Input grammar is `^(0|[1-9][0-9]{0,15})(\.[0-9]{1,2})?$`, encoded as a JSON **string**. Examples accepted: `"0"`, `"12"`, `"12.5"`, `"12.50"`, `"9999999999999999.99"`. Output always uses invariant two-place strings: `"0.00"`, `"12.00"`, `"12.50"`. Reject signs, leading zeros except zero itself, whitespace, exponent, grouping, comma decimals, missing digits around a dot, extra scale (including `"1.000"`), JSON numbers/null/objects for a required money field, negatives, and values exceeding `9999999999999999.99`. Parsing never rounds. Decimal construction also checks the decimal's scale and range instead of silently rounding trailing fractional digits.

Zero is allowed in the shared nonnegative money primitive for aggregates/reports, and its default struct value is valid zero. Future price/order/refund domain rules must additionally require positive values; the generic JSON converter is not that domain rule. Use nullable money plus `[Required]` in an input DTO when absence/null must be distinguishable from zero. No money arithmetic, pricing, checkout, database precision mapping or currency conversion feature is introduced here. Currency is fixed USD, not selected by frontend input. SQL numeric(18,2), positive-price constraints, and authoritative backend totals remain later obligations.

OpenAPI uses a schema transformer for `UsdMoney` and nullable `UsdMoney?`, with string type/pattern/range description instead of the CLR Value object's shape. Nullable types may allow null at schema level; field validation can further require a non-null value. The document describes real registered endpoints only; there is no Swagger UI package or production OpenAPI endpoint.

## Business time and pagination

API and worker each register singleton TimeProvider.System; tests can replace it. `BusinessTime.GetUtcNow(provider)` reads the injected clock and `ToUtcMicroseconds(value)` normalizes UTC and truncates sub-microsecond ticks. `HasExpired(now,deadline)` compares at that precision and uses inclusive `now >= deadline`. No real sleeps or host-local DateTime.Now. This provides a time primitive; it does not implement reservation creation or an expiry job. Future timestamp DTOs must follow the specification's ISO 8601 `Z` format; no timestamp-bearing production DTO exists yet.

`PageRequest.Create(page=1,pageSize=20)` validates page >= 1, pageSize 1–100, and offset fitting signed 32-bit Skip. It rejects overflow as 400 field validation before any database query; never clamp invalid inputs. `Offset=(Page-1)*PageSize` is safe only after this construction. Future collections return `{items,page,pageSize,totalCount}`.

Default chronological ordering is `CreatedAt DESC, Id DESC`, with the unique ID as mandatory tie-breaker. Stable chronological ascending exports use both keys ASC; report days use bucket ASC. Filtering/ownership predicates precede ordering and paging. Never page a query without ordering or accept an arbitrary user-supplied SQL column name. Product/SKU-specific sorts must be allowlisted by the implementing endpoint and append Id in the same defined direction. No generic sorting framework, collection endpoint or SQL ordering evidence is claimed now. Offset pagination is intentionally simple; concurrent inserts can shift pages and snapshot/keyset pagination is deferred.

## Example HTTP responses and manual checks

These trace IDs are illustrative. Actual property order is not part of the error contract.

```http
GET /api/health/live HTTP/1.1
Host: localhost:5080

HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
X-Trace-Id: 11111111111111111111111111111111

{"status":"Healthy"}
```

```http
GET /api/does-not-exist HTTP/1.1
Host: localhost:5080

HTTP/1.1 404 Not Found
Content-Type: application/problem+json
Cache-Control: no-store
X-Trace-Id: 11111111111111111111111111111111

{"type":"urn:orderflow:error:not_found","title":"The requested resource was not found.","status":404,"code":"not_found","traceId":"11111111111111111111111111111111","errors":{}}
```

Validation example exercised through the **test-only** echo controller (not a production endpoint):

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json
X-Trace-Id: 11111111111111111111111111111111

{"type":"urn:orderflow:error:validation_failed","title":"One or more fields are invalid.","status":400,"code":"validation_failed","traceId":"11111111111111111111111111111111","errors":{"quantity":["The field is invalid or missing."]}}
```

That test controller also verifies a success DTO round-trip: `{"name":"x","quantity":2,"amount":"12.5","ratio":1.25}` becomes `{"name":"x","quantity":2,"amount":"12.50","ratio":1.25}`. Amount is a string; quantity and ratio remain numeric. An unexpected failure instead returns 500 with code internal_error, a fixed title, empty errors and the same trace correlation, never the exception message.

After [README restore/build](../README.md), start the API as documented and run:

```bash
curl -i http://127.0.0.1:5080/api/health/live
curl -i -H 'Accept: text/html' http://127.0.0.1:5080/api/does-not-exist
```

Set `DOTNET_ENVIRONMENT=Development` before starting to read `/openapi/v1.json`; Production returns the generic 404 for that route. Run solution tests to exercise validation/money/failure/cancellation probes; there is intentionally no production endpoint for forcing an exception or marking payment successful.
