using OrderFlow.Application.Persistence;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;

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
        if (builder.Configuration.GetValue("PaymentProcessing:Enabled", false))
        {
            if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
                throw new InvalidOperationException("The simulator gateway is restricted to Development/Testing.");
            var address = builder.Configuration["PaymentGateway:BaseUrl"];
            var key = builder.Configuration["PaymentGateway:ApiKey"];
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme is not ("http" or "https")
                || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.UserInfo) || key is null || key.Length < 32)
                throw new InvalidOperationException("Configure a loopback PaymentGateway:BaseUrl and private ApiKey of at least 32 characters.");
            builder.Services.AddSingleton<IPaymentGateway>(_ => new SimulatorPaymentGateway(new HttpClient(new SocketsHttpHandler
                { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
                { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 16 * 1024 }, key));
            builder.Services.AddSingleton<PaymentProcessingIteration>();
            if (runBackgroundJobs) builder.Services.AddHostedService<PaymentWorker>();
        }
        return builder.Build();
    }
}
