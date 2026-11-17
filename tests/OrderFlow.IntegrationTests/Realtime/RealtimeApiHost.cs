using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderFlow.Api.Realtime;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Persistence;

namespace OrderFlow.IntegrationTests.Realtime;

internal sealed class RealtimeApiHost : IAsyncDisposable
{
    public WebApplication App { get; }
    public Uri Address { get; private set; } = null!;
    public RealtimeLog Logs { get; } = new();
    private RealtimeApiHost(PostgresDatabase db, string redis, RabbitMqSettings broker, string name, string prefix, bool dispatch,
        IDataProtectionProvider keys, TimeSpan? expiry, bool stopHostOnDispatchFailure)
    {
        App = Api.Program.Build(["--environment", "Development"], builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OrderFlow"] = db.ConnectionString, ["Urls"] = "http://127.0.0.1:0",
                ["Authentication:AllowInsecureHttp"] = "true", ["Realtime:Enabled"] = "true", ["Realtime:DispatchEnabled"] = dispatch.ToString(),
                ["Realtime:RedisConnectionString"] = redis, ["Realtime:RedisPrefix"] = prefix, ["Realtime:InstanceName"] = name,
                ["Realtime:StopHostOnDispatchFailure"] = stopHostOnDispatchFailure.ToString(),
                // Referenced hosts share appsettings output names; the simulator's Warning default
                // must not filter this test's deterministic post-ACK Information signal.
                ["Logging:LogLevel:OrderFlow.Api.Realtime.RealtimeDeliveryHandler"] = "Information",
                ["RabbitMq:HostName"] = broker.HostName, ["RabbitMq:Port"] = broker.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["RabbitMq:VirtualHost"] = broker.VirtualHost, ["RabbitMq:UserName"] = broker.UserName, ["RabbitMq:Password"] = broker.Password,
                ["RabbitMq:Prefix"] = broker.Prefix, ["RabbitMq:RetryDelayMilliseconds"] = "200"
            });
            builder.Services.AddSingleton(keys); // Separate API DI/hub/backplane; only cookie-key material is shared.
            builder.Logging.ClearProviders(); builder.Logging.AddProvider(Logs);
            if (expiry is { } lifetime) builder.Services.Configure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options => options.ExpireTimeSpan = lifetime);
        });
    }
    public static async Task<RealtimeApiHost> StartAsync(PostgresDatabase db, string redis, RabbitMqSettings broker, string name, string prefix,
        bool dispatch, IDataProtectionProvider keys, TimeSpan? expiry = null, bool stopHostOnDispatchFailure = true)
    {
        var host = new RealtimeApiHost(db, redis, broker, name, prefix, dispatch, keys, expiry, stopHostOnDispatchFailure);
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await host.App.StartAsync(budget.Token);
            host.Address = new(host.App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()); return host;
        }
        catch { await host.DisposeAsync(); throw; }
    }
    public HttpClient Browser(CookieContainer jar) => new(new HttpClientHandler { CookieContainer = jar, AllowAutoRedirect = false }) { BaseAddress = Address, Timeout = TimeSpan.FromSeconds(15) };
    public async Task<(Guid Id, string Email, string Password)> UserAsync()
    {
        await using var scope = App.Services.CreateAsyncScope();
        await DevelopmentSeeder.SeedRolesAsync(scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>(), default);
        var email = "realtime-" + Guid.NewGuid().ToString("N") + "@example.invalid"; var password = "Aa1!" + Guid.NewGuid().ToString("N");
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, CreatedAt = DateTimeOffset.UtcNow };
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Xunit.Assert.True((await users.CreateAsync(user, password)).Succeeded); Xunit.Assert.True((await users.AddToRoleAsync(user, "Customer")).Succeeded);
        return (user.Id, email, password);
    }
    public async Task<(HttpClient Browser, CookieContainer Cookies)> LoginAsync(string email, string password)
    {
        var jar = new CookieContainer(); var browser = Browser(jar);
        using var login = await BrowserRequests.PostAuthAsync(browser, "login", new { email, password }, await BrowserRequests.TokenAsync(browser));
        Xunit.Assert.Equal(HttpStatusCode.OK, login.StatusCode); return (browser, jar);
    }
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); try { await App.StopAsync(timeout.Token); } finally { await App.DisposeAsync(); }
    }
}

internal sealed class RealtimeLog : ILoggerProvider
{
    public System.Threading.Channels.Channel<int> Events { get; } = System.Threading.Channels.Channel.CreateUnbounded<int>();
    public System.Collections.Concurrent.ConcurrentQueue<string> Messages { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CapturedLogger(this, categoryName);
    public void Dispose() { }
    public async Task WaitAsync(int expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (await Events.Reader.ReadAsync(timeout.Token) != expected) { }
    }
    private sealed class CapturedLogger(RealtimeLog owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            owner.Messages.Enqueue(category + ":" + formatter(state, exception));
            if (id.Id is >= 2401 and <= 2407) owner.Events.Writer.TryWrite(id.Id);
        }
    }
}
