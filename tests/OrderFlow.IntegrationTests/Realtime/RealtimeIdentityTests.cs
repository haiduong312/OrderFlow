using System.Security.Claims;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OrderFlow.Api.Realtime;
using Xunit;

namespace OrderFlow.IntegrationTests.Realtime;

public sealed class RealtimeIdentityTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("uppercase")]
    [InlineData("anonymous")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("empty")]
    public async Task User_identifier_requires_one_authenticated_identity_and_one_nonempty_guid(string scenario)
    {
        var id = Guid.NewGuid(); var claims = new List<Claim>();
        if (scenario != "missing") claims.Add(new(ClaimTypes.NameIdentifier, scenario == "empty" ? Guid.Empty.ToString("D") : scenario == "uppercase" ? id.ToString("D").ToUpperInvariant() : id.ToString("D")));
        if (scenario == "duplicate") claims.Add(new(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")));
        await using var connection = new DefaultConnectionContext();
        connection.Features.Set<IConnectionUserFeature>(new UserFeature { User = new ClaimsPrincipal(new ClaimsIdentity(claims, scenario == "anonymous" ? null : "IdentityCookie")) });
        var context = new HubConnectionContext(connection, new HubConnectionContextOptions(), NullLoggerFactory.Instance);
        Assert.Equal(scenario is "valid" or "uppercase" ? id.ToString("D") : null, new NotificationUserIdProvider().GetUserId(context));
    }
    [Fact]
    public void Realtime_is_opt_in_and_requires_explicit_backplane_and_broker_configuration()
    {
        var disabled = new ServiceCollection(); disabled.AddNotificationRealtime(new ConfigurationBuilder().Build());
        Assert.DoesNotContain(disabled, d => d.ServiceType == typeof(RealtimeSettings));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Realtime:Enabled"]="true" }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddNotificationRealtime(configuration));
        configuration["Realtime:RedisConnectionString"]="127.0.0.1:1";
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddNotificationRealtime(configuration));
        configuration["Realtime:DispatchEnabled"]="false";
        var services = new ServiceCollection(); services.AddLogging(); services.AddNotificationRealtime(configuration);
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
        configuration["Realtime:Prefetch"]="0";
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddNotificationRealtime(configuration));
    }
    private sealed class UserFeature : IConnectionUserFeature { [System.Diagnostics.CodeAnalysis.AllowNull] public ClaimsPrincipal User { get; set; } = new(); }
}
