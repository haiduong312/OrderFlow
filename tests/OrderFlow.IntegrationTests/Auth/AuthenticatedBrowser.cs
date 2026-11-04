using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.Application.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Auth;

public static class AuthenticatedBrowser
{
    public static async Task<(HttpClient Client, Guid Id)> CreateAsync(AuthApiFactory factory, params string[] roles)
    {
        // Test setup persists users/roles through real Identity stores. HTTP authentication
        // still uses the production login endpoint and cookie handler, never a test auth scheme.
        var email = AuthenticationTests.Email();
        var password = AuthenticationTests.Password();
        var id = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await DevelopmentSeeder.SeedRolesAsync(scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>(), default);
            var user = new ApplicationUser
            {
                Id = id, Email = email, UserName = email,
                CreatedAt = BusinessTime.GetUtcNow(scope.ServiceProvider.GetRequiredService<TimeProvider>())
            };
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRolesAsync(user, roles)).Succeeded);
        }

        var client = factory.Browser();
        using var response = await AuthenticationTests.Post(client, "login", new { email, password }, await AuthenticationTests.Token(client));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (client, id);
    }
}
