using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OrderFlow.Application.Modules.Identity;

namespace OrderFlow.Application.Persistence;

public static class PersistenceRegistration
{
    public static IdentityBuilder AddOrderFlowPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        // Options are resolved lazily: liveness/startup do not connect to or migrate PostgreSQL.
        services.AddDbContext<OrderFlowDbContext>(options => Configure(options,
            configuration.GetConnectionString("OrderFlow")));
        return services.AddIdentityCore<ApplicationUser>(IdentityPolicy.Configure)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<OrderFlowDbContext>();
    }

    public static void Configure(DbContextOptionsBuilder options, string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Set ConnectionStrings__OrderFlow before resolving persistence or running database tools.");
        }

        var safeOptions = new NpgsqlConnectionStringBuilder(connectionString)
        {
            IncludeErrorDetail = false,
            PersistSecurityInfo = false
        };
        options.UseNpgsql(safeOptions.ConnectionString, postgres =>
        {
            postgres.MigrationsHistoryTable("__EFMigrationsHistory", "messaging");
            postgres.CommandTimeout(30);
        });
        options.EnableSensitiveDataLogging(false);
        options.EnableDetailedErrors(false);
        // No implicit retries: future operations must classify failures and preserve identities.
    }
}
