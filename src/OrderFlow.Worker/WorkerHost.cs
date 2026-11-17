using OrderFlow.Application.Persistence;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Emails;
using OrderFlow.Worker.Emails;
using OrderFlow.Application.Modules.Notifications;
using OrderFlow.Worker.Messaging;
using OrderFlow.Application.Observability;

namespace OrderFlow.Worker;

public static class WorkerHost
{
    public static IHost Build(string[] args, Action<HostApplicationBuilder>? configure = null, bool runBackgroundJobs = true)
    {
        var builder = Host.CreateApplicationBuilder(args);
        if (Environment.GetEnvironmentVariable("ORDERFLOW_SETTINGS_FILE") is { Length: > 0 } settingsFile)
            builder.Configuration.AddJsonFile(settingsFile, optional: false, reloadOnChange: false).AddEnvironmentVariables().AddCommandLine(args);
        builder.Logging.AddSafeConsole();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(25));
        builder.Services.AddOrderFlowPersistence(builder.Configuration);
        configure?.Invoke(builder);
        builder.Services.AddSingleton<DatabaseReadiness>();
        builder.Services.AddSingleton<IRuntimeReadiness, WorkerReadiness>();
        builder.Services.AddOrderFlowTelemetry(builder.Configuration, builder.Logging, "orderflow-worker");
        builder.Services.AddOptions<OrderExpiryOptions>().Bind(builder.Configuration.GetSection("OrderExpiry"))
            .Validate(options => options.BatchSize is >= 1 and <= 100, "OrderExpiry:BatchSize must be 1–100.")
            .Validate(options => options.PollIntervalSeconds is >= 1 and <= 300, "OrderExpiry:PollIntervalSeconds must be 1–300.")
            .ValidateOnStart();
        builder.Services.AddScoped<OrderExpiryProcessor>();
        builder.Services.AddSingleton<OrderExpiryIteration>();
        if (builder.Configuration.GetValue("EmailSending:Enabled", false))
        {
            if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
                throw new InvalidOperationException("Development SMTP email delivery is restricted to Development/Testing.");
            var smtp = builder.Configuration.GetSection("EmailSending").Get<DevelopmentSmtpOptions>() ?? new(); smtp.Validate();
            builder.Services.AddSingleton(smtp); builder.Services.AddSingleton<IEmailGateway, DevelopmentSmtpEmailGateway>();
            builder.Services.AddSingleton<EmailSendingIteration>();
            if (runBackgroundJobs) builder.Services.AddHostedService<EmailSendingWorker>();
        }
        var publishEvents = builder.Configuration.GetValue("OutboxPublishing:Enabled", false);
        var notifications = builder.Configuration.GetValue("Notifications:Enabled", false);
        if (publishEvents || notifications)
        {
            var settings = builder.Configuration.GetSection("RabbitMq").Get<RabbitMqSettings>() ?? new();
            settings.Validate();
            builder.Services.AddSingleton(settings);
            if (notifications)
            {
                var consumerOptions = builder.Configuration.GetSection("Notifications").Get<NotificationConsumerOptions>() ?? new();
                consumerOptions.Validate();
                builder.Services.AddSingleton(consumerOptions);
                builder.Services.AddScoped<NotificationProcessor>();
                builder.Services.AddSingleton<NotificationDeliveryHandler>();
                if (runBackgroundJobs) builder.Services.AddHostedService<NotificationConsumerWorker>();
            }
        }
        if (publishEvents)
        {
            builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
            builder.Services.AddSingleton<OutboxPublishingIteration>();
            if (runBackgroundJobs) builder.Services.AddHostedService<OutboxPublisherWorker>();
        }
        builder.Services.AddScoped<PaymentSettlementProcessor>();
        if (runBackgroundJobs && builder.Configuration.GetValue("PaymentSettlement:Enabled", false))
            builder.Services.AddHostedService<PaymentSettlementWorker>();
        if (runBackgroundJobs && builder.Configuration.GetValue("OrderExpiry:Enabled", true))
            builder.Services.AddHostedService<OrderExpiryWorker>();
        var submit = builder.Configuration.GetValue("PaymentProcessing:Enabled", false);
        var reconcile = builder.Configuration.GetValue("PaymentReconciliation:Enabled", false);
        var refunds = builder.Configuration.GetValue("RefundProcessing:Enabled", false);
        if (submit || reconcile || refunds)
        {
            if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
                throw new InvalidOperationException("The simulator gateway is restricted to Development/Testing.");
            var address = builder.Configuration["PaymentGateway:BaseUrl"];
            var key = builder.Configuration["PaymentGateway:ApiKey"];
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || (!uri.IsLoopback && !(builder.Configuration.GetValue("PaymentGateway:AllowContainerHost", false) && uri.Host == "simulator" && uri.Port == 8090)) || uri.Scheme is not ("http" or "https")
                || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.UserInfo) || key is null || key.Length < 32)
                throw new InvalidOperationException("Configure a permitted local PaymentGateway:BaseUrl and private ApiKey of at least 32 characters.");
            builder.Services.AddSingleton<IPaymentGateway>(_ => new SimulatorPaymentGateway(new HttpClient(new SocketsHttpHandler
                { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
                { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 16 * 1024 }, key));
            builder.Services.AddSingleton<PaymentProcessingIteration>();
            builder.Services.AddSingleton<PaymentReconciliationIteration>();
            builder.Services.AddSingleton<RefundProcessingIteration>();
            if (runBackgroundJobs && submit) builder.Services.AddHostedService<PaymentWorker>();
            if (runBackgroundJobs && reconcile) builder.Services.AddHostedService<PaymentReconciliationWorker>();
            if (runBackgroundJobs && refunds) builder.Services.AddHostedService<RefundWorker>();
        }
        return builder.Build();
    }
}
