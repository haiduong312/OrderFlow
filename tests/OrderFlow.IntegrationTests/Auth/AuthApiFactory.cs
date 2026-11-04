using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Http;
using OrderFlow.IntegrationTests.Persistence;
using OrderFlow.IntegrationTests.Authorization;

namespace OrderFlow.IntegrationTests.Auth;

public sealed class AuthApiFactory(PostgresDatabase database, string environment = "Production", bool allowHttp = false,
    RegistrationGate? registrationGate = null, LoginGate? loginGate = null,
    DbCommandInterceptor? catalogInterceptor = null) : WebApplicationFactory<Api.Program>
{
    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:OrderFlow"] = database.ConnectionString,
                ["Authentication:AllowInsecureHttp"] = allowHttp.ToString()
            }));
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
        builder.ConfigureServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddControllers().AddApplicationPart(typeof(AuthPolicyProbeController).Assembly);
            services.AddSingleton<RepresentativeResourceStore>();
            if (registrationGate is not null)
            {
                services.AddSingleton<IUserValidator<ApplicationUser>>(registrationGate);
            }

            if (loginGate is not null)
            {
                services.AddDbContext<OrderFlowDbContext>(options => options.AddInterceptors(loginGate));
            }
            if (catalogInterceptor is not null)
            {
                services.AddDbContext<OrderFlowDbContext>(options => options.AddInterceptors(catalogInterceptor));
            }
        });
    }

    public HttpClient Browser(bool https = true) => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri(https ? "https://localhost" : "http://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true
    });
}

public sealed class RegistrationGate : IUserValidator<ApplicationUser>
{
    private readonly ConcurrentDictionary<Guid, byte> participants = new();
    private readonly TaskCompletionSource both = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Participants => participants.Count;

    public async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        participants.TryAdd(user.Id, 0);
        if (participants.Count >= 2)
        {
            both.TrySetResult();
        }

        // Default Identity validators have already queried uniqueness before this gate.
        await both.Task.WaitAsync(TimeSpan.FromSeconds(15));
        return IdentityResult.Success;
    }
}

public sealed class LoginGate : DbCommandInterceptor
{
    private readonly ConcurrentDictionary<Guid, byte> participants = new();
    private readonly TaskCompletionSource both = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Participants => participants.Count;

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
        {
            participants.TryAdd(eventData.Context!.ContextId.InstanceId, 0);
            if (participants.Count >= 2)
            {
                both.TrySetResult();
            }

            // Before SQL executes, so neither participant holds the contested user row yet.
            await both.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
        }

        return result;
    }
}
