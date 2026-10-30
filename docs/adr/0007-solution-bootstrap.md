# ADR-007: Minimal solution bootstrap and reproducible dependencies

Date: 2026-10-30. Status: Accepted. Implements ADR-001 without changing its three-production-project decision.

## Context

Milestone 02 needs separately runnable API/worker hosts and test projects. The request mentions domain/application/infrastructure boundaries; ADR-001 already chose feature folders inside one Application assembly, not separate projects for those layers. No existing solution, source, test project, package manifest, or migration was present. Git is now valid and was clean at inspection; the earlier invalid-Git record remains historical milestone-01 evidence.

## Decision

Create OrderFlow.slnx with API, Worker, Application, UnitTests and IntegrationTests. API/Worker each reference Application. UnitTests references Application; IntegrationTests references the two hosts. Application references no host and currently contains only boundary documentation. Domain/application code will be grouped by module/feature; shared persistence/messaging infrastructure stays in the ADR's existing folders. No empty entities/interfaces, generic repository, EF/Npgsql, or invented domain tests are needed for composition.

Pin installed stable SDK 10.0.114 in global.json with `rollForward: disable` and `allowPrerelease: false`; target net10.0. This SDK is listed in [Microsoft's servicing announcement](https://github.com/dotnet/announcements/issues/496). Centralize exact direct NuGet versions, retain generated lockfiles, and use locked restores. Use stable Microsoft hosting/HTTP testing packages 10.0.14, plus the small VSTest/xUnit baseline recorded in README. Central build settings enable nullable references, implicit usings, warnings as errors, full NuGet auditing, and the SDK's per-project artifacts layout. No extra CLI tool is necessary, so no speculative tool manifest is added.

API only maps process liveness. Worker runs Generic Host with no background jobs; it waits for normal shutdown signals. Test the real API entry point through WebApplicationFactory and the worker's shared construction method through StartAsync/StopAsync. UnitTests is empty until domain behavior exists; no always-passing test is added. Composition tests need no external service and make no SQL correctness claim.

## Alternatives and consequences

Three additional layer projects would contradict ADR-001 and add empty abstractions. Explicit public API entry-point and worker construction method give tests a small real composition seam. An idle Generic Host is honest about absent jobs; a timed log loop would imply work that does not exist. Exact SDK selection requires installing that release and consciously updating it for servicing. Folder boundaries rely on review. Test infrastructure is deliberately small and can evolve when meaningful feature tests arrive.

## Verification and references

Require locked restore/build, two composition tests, separate API HTTP launch and worker start/SIGTERM/exit-0 evidence, no circular project references, and no generated/secret-bearing files in repository deliverables. Actual results and any blockers are recorded in [implementation status](../implementation-status.md).

Official references: [global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json), [Central Package Management](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management), [ASP.NET Core integration testing](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0), and [.NET Worker Services](https://learn.microsoft.com/en-us/dotnet/core/extensions/workers).
