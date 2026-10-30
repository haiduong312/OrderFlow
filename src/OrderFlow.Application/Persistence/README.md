# Persistence boundary

Reserved for the single scoped OrderFlowDbContext, owner-schema mappings, and ordered migration chain required by ADR-001. No EF Core/Npgsql package, DbContext, connection string, or migration is added in milestone 02; these begin in milestone 03.

Future application use cases share one context for an atomic operation. Never share that instance across parallel tasks, and do not wrap every EF operation in a generic repository. Runtime hosts compose the application; schema changes are explicit operator actions.
