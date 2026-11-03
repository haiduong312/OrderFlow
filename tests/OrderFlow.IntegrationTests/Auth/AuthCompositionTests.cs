using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace OrderFlow.IntegrationTests.Auth;

public sealed class AuthCompositionTests
{
    [Fact]
    public async Task Token_and_anonymous_challenge_require_no_database_configuration()
    {
        await using var factory = new WebApplicationFactory<Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:OrderFlow"] = "" }));
            builder.ConfigureServices(services => services.AddDataProtection().UseEphemeralDataProtectionProvider());
        });
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        Assert.False(string.IsNullOrWhiteSpace(await AuthenticationTests.Token(client)));
        using var response = await client.GetAsync("/api/auth/me");
        await AuthenticationTests.Problem(response, 401, "unauthenticated");
        Assert.Null(response.Headers.Location);
        using var probe = await client.GetAsync("/_tests/auth/admin");
        Assert.Equal(HttpStatusCode.NotFound, probe.StatusCode);
    }
}
