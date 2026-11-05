using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Http;
using OrderFlow.IntegrationTests.Persistence;
using OrderFlow.IntegrationTests.Authorization;

namespace OrderFlow.IntegrationTests.Auth;

public sealed class AuthApiFactory(PostgresDatabase database, string environment = "Production", bool allowHttp = false,
    RegistrationGate? registrationGate = null, LoginGate? loginGate = null,
    DbCommandInterceptor? catalogInterceptor = null, TimeProvider? clock = null) : WebApplicationFactory<Api.Program>
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
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(clock ?? TimeProvider.System);
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

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = builder.Build();
        // Inspect the FINAL provider, including WithWebHostBuilder additions. Checking service
        // descriptors earlier can miss a hosted service registered after ConfigureTestServices.
        var unexpected = host.Services.GetServices<IHostedService>().Where(service => service.GetType().FullName is not (
            "Microsoft.AspNetCore.Hosting.GenericWebHostService" or
            "Microsoft.AspNetCore.DataProtection.Internal.DataProtectionHostedService")).ToArray();
        if (unexpected.Length != 0)
        {
            host.Dispose();
            throw new InvalidOperationException("Test host found uncontrolled background services: " +
                string.Join(", ", unexpected.Select(service => service.GetType().FullName)) +
                ". Remove them explicitly or drive them manually in the test.");
        }
        host.Start();
        return host;
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
    private readonly RaceRendezvous rendezvous = new();
    public int Participants => rendezvous.Participants;

    public async Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        // Default validators have queried uniqueness; neither registration has written a user.
        await rendezvous.ArriveAsync(user.Id, CancellationToken.None);
        return IdentityResult.Success;
    }
}

public sealed class LoginGate : CommandRaceGate
{
    public LoginGate() : base(command => command.Contains("FOR UPDATE", StringComparison.Ordinal)) => Enabled = true;
}
