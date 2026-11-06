using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Persistence;
using OrderFlow.Simulator;
using Xunit;

namespace OrderFlow.IntegrationTests.Payments;

internal sealed class SimulatorTestHost : IAsyncDisposable
{
    private readonly WebApplication app;
    public string ApiKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string ControlKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public string SigningKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public Uri Address { get; private set; } = null!;
    private SimulatorTestHost(PostgresDatabase database, TimeProvider clock, DbCommandInterceptor? probe)
    {
        app = SimulatorHost.Build(["--environment", "Testing"], builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:OrderFlow"] = database.ConnectionString,
                ["Simulator:Enabled"] = "true", ["Simulator:ApiKey"] = ApiKey, ["Simulator:ControlKey"] = ControlKey,
                ["Simulator:CallbackSigningKey"] = SigningKey, ["Urls"] = "http://127.0.0.1:0"
            });
            builder.Services.RemoveAll<TimeProvider>(); builder.Services.AddSingleton(clock);
            if (probe is not null) builder.Services.AddDbContext<OrderFlowDbContext>(options => options.AddInterceptors(probe));
        });
    }

    public static async Task<SimulatorTestHost> StartAsync(PostgresDatabase database, TimeProvider clock, DbCommandInterceptor? probe = null)
    {
        var host = new SimulatorTestHost(database, clock, probe);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await host.app.StartAsync(timeout.Token);
            host.Address = new(host.app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
            return host;
        }
        catch { await host.DisposeAsync(); throw; }
    }

    public SimulatorPaymentGateway Gateway(TimeSpan? timeout = null) => new(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        { BaseAddress = Address, Timeout = timeout ?? TimeSpan.FromSeconds(5), MaxResponseContentBufferSize = 16 * 1024 }, ApiKey);
    public HttpClient Client(bool control = false, bool authenticated = true)
    {
        var client = new HttpClient { BaseAddress = Address, Timeout = TimeSpan.FromSeconds(10) };
        if (authenticated) client.DefaultRequestHeaders.Add(control ? "X-Simulator-Control-Key" : "X-Simulator-Key", control ? ControlKey : ApiKey);
        return client;
    }

    public async Task ScenarioAsync(string key, string scenario)
    {
        using var client = Client(control: true);
        using var response = await client.PutAsJsonAsync("/control/payments/" + key + "/scenario", new { scenario });
        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await app.StopAsync(timeout.Token); }
        finally { await app.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)); }
    }
}
