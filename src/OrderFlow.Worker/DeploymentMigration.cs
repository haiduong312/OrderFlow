using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Worker;

public static class DeploymentMigration
{
    // Explicit schema-owner CLI only. Normal hosts neither migrate nor grant privileges.
    public static async Task RunAsync(IServiceProvider services, CancellationToken token)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
        await db.Database.MigrateAsync(token);
        await DevelopmentSeeder.SeedRolesAsync(db, token); // Definitions only, no credentials.
        if (services.GetRequiredService<IConfiguration>().GetValue("Deployment:GrantRuntimeRoles", false))
            await db.Database.ExecuteSqlRawAsync(RuntimeGrants, token);
    }
    // Identifiers are a fixed allowlist, quoted by PostgreSQL. No input or execution endpoint.
    private const string RuntimeGrants = """
        DO $grants$ DECLARE s text; BEGIN
          FOREACH s IN ARRAY ARRAY['identity','catalog','inventory','ordering','payments','messaging','notifications'] LOOP
            EXECUTE format('GRANT USAGE ON SCHEMA %I TO orderflow_api, orderflow_worker', s);
            EXECUTE format('GRANT SELECT ON ALL TABLES IN SCHEMA %I TO orderflow_api, orderflow_worker', s);
            EXECUTE format('GRANT INSERT, UPDATE ON ALL TABLES IN SCHEMA %I TO orderflow_api', s);
            IF s NOT IN ('identity','catalog') THEN
              EXECUTE format('GRANT INSERT, UPDATE ON ALL TABLES IN SCHEMA %I TO orderflow_worker', s);
            END IF;
            EXECUTE format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO orderflow_api, orderflow_worker', s);
          END LOOP;
        END $grants$;
        GRANT DELETE ON ALL TABLES IN SCHEMA identity TO orderflow_api;
        REVOKE INSERT, UPDATE, DELETE ON messaging."__EFMigrationsHistory" FROM orderflow_api, orderflow_worker;
        """;
}
