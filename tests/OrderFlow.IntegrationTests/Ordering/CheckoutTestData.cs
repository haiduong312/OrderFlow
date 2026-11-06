using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Money;
using OrderFlow.Application.Modules.Catalog;
using OrderFlow.Application.Modules.Inventory;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Persistence;
using Xunit;

namespace OrderFlow.IntegrationTests.Ordering;

internal static class CheckoutTestData
{
    internal static async Task<Product[]> StockAsync(PostgresDatabase database, AuthApiFactory factory,
        int count = 2, decimal price = 12.50m, int stock = 10)
    {
        var (client, actor) = await AuthenticatedBrowser.CreateAsync(factory, "Admin");
        client.Dispose();
        await using var context = database.CreateContext();
        var category = Category.Create("Checkout category", DateTimeOffset.UnixEpoch);
        var products = Enumerable.Range(0, count).Select(index => Product.Create("CHECKOUT-" + Guid.NewGuid().ToString("N"),
            "Original product " + index, "", UsdMoney.FromDecimal(price), category.Id, DateTimeOffset.UnixEpoch))
            .OrderBy(product => product.Id.ToString("N"), StringComparer.Ordinal).ToArray();
        context.Add(category);
        context.AddRange(products);
        await context.SaveChangesAsync();
        if (stock != 0)
        {
            foreach (var product in products)
            {
                var balance = await context.InventoryBalances.SingleAsync(balance => balance.ProductId == product.Id);
                context.StockMovements.Add(balance.Change(Guid.NewGuid(), StockMovementKind.Receipt, stock, "Opening receipt", actor, DateTimeOffset.UnixEpoch));
            }
            await context.SaveChangesAsync();
        }
        return products;
    }

    internal static object Body(IEnumerable<Product> products, int quantity = 1) => new
    {
        items = products.Select(product => new { productId = product.Id, quantity }).ToArray()
    };

    internal static async Task<HttpResponseMessage> PostAsync(HttpClient client, object body, string? key,
        string? token = null, bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders") { Content = JsonContent.Create(body) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        if (csrf) request.Headers.Add("X-CSRF-TOKEN", token ?? await BrowserRequests.TokenAsync(client));
        return await client.SendAsync(request);
    }

    internal static async Task<OrderResponse> CreatedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }

    internal static async Task AssertCountsAsync(PostgresDatabase database, int orders, int reservations)
    {
        await using var read = database.CreateContext();
        Assert.Equal(orders, await read.Orders.CountAsync());
        Assert.Equal(orders, await read.OrderStatusHistory.CountAsync());
        Assert.Equal(reservations, await read.OrderItems.CountAsync());
        Assert.Equal(reservations, await read.InventoryReservations.CountAsync());
        Assert.Equal(reservations, await read.StockMovements.CountAsync(movement => movement.Kind == StockMovementKind.Reservation));
        Assert.Equal(orders, await read.OutboxMessages.CountAsync(message => message.EventType == "ordering.order-created"));
        Assert.Equal(orders, await read.IdempotencyRecords.CountAsync(record => record.Operation == CheckoutRequest.Operation));
        var balances = await read.InventoryBalances.AsNoTracking().ToListAsync();
        var active = await read.InventoryReservations.AsNoTracking().Where(row => row.Status == ReservationStatus.Active).ToListAsync();
        var ledger = await read.StockMovements.AsNoTracking().ToListAsync();
        foreach (var balance in balances)
        {
            Assert.InRange(balance.Reserved, 0, balance.OnHand);
            Assert.Equal(active.Where(row => row.ProductId == balance.ProductId).Sum(row => row.Quantity), balance.Reserved);
            Assert.Equal(ledger.Where(row => row.ProductId == balance.ProductId).Sum(row => row.OnHandDelta), balance.OnHand);
            Assert.Equal(ledger.Where(row => row.ProductId == balance.ProductId).Sum(row => row.ReservedDelta), balance.Reserved);
        }
    }
}
