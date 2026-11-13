using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using OrderFlow.Api.Auth;
using OrderFlow.Application.Messaging;
using StackExchange.Redis;

namespace OrderFlow.Api.Realtime;

public static class RealtimeRegistration
{
    public const string HubPath = "/api/hubs/notifications";
    public static IServiceCollection AddNotificationRealtime(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Realtime").Get<RealtimeSettings>() ?? new();
        if (!settings.Enabled) return services;
        var redis = settings.RedisOptions(); services.AddSingleton(settings);
        services.AddLogging(logging => logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning)
            .AddFilter("Microsoft.AspNetCore.Http.Connections", LogLevel.Warning).AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Warning));
        services.AddSingleton<IUserIdProvider, NotificationUserIdProvider>();
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = false; options.MaximumReceiveMessageSize = 4096; options.MaximumParallelInvocationsPerClient = 1;
            options.HandshakeTimeout = TimeSpan.FromSeconds(10); options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
            options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        }).AddStackExchangeRedis(options => options.Configuration = redis);
        services.AddOptions<Microsoft.AspNetCore.SignalR.StackExchangeRedis.RedisOptions>().Configure<ILoggerFactory>((options, logs) =>
        {
            options.ConnectionFactory = async _ =>
            {
                // Do not forward raw Redis connection strings/SDK exception text to our dependency logs.
                var connection = await ConnectionMultiplexer.ConnectAsync(redis);
                var log = logs.CreateLogger("OrderFlow.Api.Realtime.RedisBackplane");
                connection.ConnectionFailed += (_, failure) => log.LogWarning(new EventId(2405, "RealtimeRedisUnavailable"),
                    "API {Instance} Redis backplane unavailable: {FailureType}", settings.InstanceName, failure.FailureType);
                connection.ConnectionRestored += (_, _) => log.LogInformation(new EventId(2406, "RealtimeRedisRestored"),
                    "API {Instance} Redis backplane connection restored", settings.InstanceName);
                return connection;
            };
        });
        services.AddSingleton<NotificationRealtimeDispatcher>();
        if (settings.DispatchEnabled)
        {
            var broker = configuration.GetSection("RabbitMq").Get<RabbitMqSettings>() ?? new(); broker.Validate(); services.AddSingleton(broker);
            services.AddSingleton<RealtimeDeliveryHandler>(); services.AddHostedService<RealtimeConsumerWorker>();
        }
        return services;
    }
    public static WebApplication UseNotificationRealtime(this WebApplication app)
    {
        if (app.Services.GetService<RealtimeSettings>() is null) return app;
        app.UseMiddleware<RealtimeRequestGuard>();
        app.MapHub<NotificationHub>(HubPath, options =>
        {
            options.Transports = HttpTransportType.WebSockets; options.CloseOnAuthenticationExpiration = true;
            options.ApplicationMaxBufferSize = 16 * 1024; options.TransportMaxBufferSize = 16 * 1024;
        }).RequireAuthorization().RequireRateLimiting(BrowserAuth.ReadLimit);
        return app;
    }
}
