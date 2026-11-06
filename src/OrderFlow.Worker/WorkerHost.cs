using OrderFlow.Application.Persistence;
using OrderFlow.Application.Modules.Ordering;

namespace OrderFlow.Worker;

public static class WorkerHost
{
    public static IHost Build(string[] args, Action<HostApplicationBuilder>? configure = null, bool runBackgroundJobs = true)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddOrderFlowPersistence(builder.Configuration);
        configure?.Invoke(builder);
        builder.Services.AddOptions<OrderExpiryOptions>().Bind(builder.Configuration.GetSection("OrderExpiry"))
            .Validate(options => options.BatchSize is >= 1 and <= 100, "OrderExpiry:BatchSize must be 1–100.")
            .Validate(options => options.PollIntervalSeconds is >= 1 and <= 300, "OrderExpiry:PollIntervalSeconds must be 1–300.")
            .ValidateOnStart();
        builder.Services.AddScoped<OrderExpiryProcessor>();
        builder.Services.AddSingleton<OrderExpiryIteration>();
        if (runBackgroundJobs && builder.Configuration.GetValue("OrderExpiry:Enabled", true))
            builder.Services.AddHostedService<OrderExpiryWorker>();
        return builder.Build();
    }
}
