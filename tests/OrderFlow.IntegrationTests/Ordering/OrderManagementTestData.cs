using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Persistence;

namespace OrderFlow.IntegrationTests.Ordering;

internal static class OrderManagementTestData
{
    internal static string Customer(Guid id) => "/api/orders/" + id;
    internal static string Admin(Guid id) => "/api/admin/orders/" + id;
    internal static async Task<OrderResponse> CreateAsync(HttpClient customer, object body, string? key = null)
    {
        using var response = await CheckoutTestData.PostAsync(customer, body, key ?? Guid.NewGuid().ToString("N"));
        return await CheckoutTestData.CreatedAsync(response);
    }
    internal static async Task<HttpResponseMessage> CancelAsync(HttpClient customer, Guid id, string? token = null, bool csrf = true) =>
        await BrowserRequests.SendAsync(customer, HttpMethod.Post, Customer(id) + "/cancel", new { },
            csrf ? token ?? await BrowserRequests.TokenAsync(customer) : null);
    internal static async Task<HttpResponseMessage> AdvanceAsync(HttpClient admin, Guid id, string target, int version,
        string? token = null, string? reason = null) => await BrowserRequests.SendAsync(admin, HttpMethod.Post,
            Admin(id) + "/transitions", new { targetStatus = target, expectedVersion = version, reason },
            token ?? await BrowserRequests.TokenAsync(admin));

    // Payment/capture is not implemented. Seed its later status and history only in the owned
    // test database; this is fulfillment test setup, never a production confirmation route.
    internal static async Task SeedStatusAsync(PostgresDatabase database, Guid id, string status)
    {
        await using var context = database.CreateContext();
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE ordering."Orders" SET "Status" = {status}, "Version" = 2 WHERE "Id" = {id};
            INSERT INTO ordering."OrderStatusHistory"
                ("OrderId","Version","FromStatus","Status","ActorKind","ActorUserId","Reason","OccurredAt")
            SELECT "Id",2,'PendingPayment',"Status",'Customer',"UserId",'Simulated later writer for test setup',"CreatedAt"
            FROM ordering."Orders" WHERE "Id" = {id}
            """);
    }
}
