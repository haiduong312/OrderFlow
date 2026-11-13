using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using OrderFlow.Api.Auth.Authorization;

namespace OrderFlow.Api.Realtime;

// No subscribe/join/order-group or client-to-server business methods. Server chooses every destination.
[Authorize]
public sealed class NotificationHub(IHostApplicationLifetime lifetime) : Hub
{
    private CancellationTokenRegistration shutdown;
    public override async Task OnConnectedAsync()
    {
        if (Context.UserIdentifier is null) { Context.Abort(); return; }
        shutdown = lifetime.ApplicationStopping.Register(Context.Abort);
        await base.OnConnectedAsync();
        // Runs after the backplane's subscriptions are established. Clients refetch durable state here/reconnect.
        await Clients.Caller.SendCoreAsync("ConnectionReady", [], Context.ConnectionAborted);
    }
    public override Task OnDisconnectedAsync(Exception? exception) { shutdown.Dispose(); return base.OnDisconnectedAsync(exception); }
}
public sealed class NotificationUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => UserIdentity.Read(connection.User)?.Id.ToString("D");
}
public sealed record NotificationHint(Guid EventId, Guid NotificationId, Guid OrderId);
