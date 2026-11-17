using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Context.Propagation;
using OrderFlow.Application.Modules.Catalog;

namespace OrderFlow.Application.Observability;

public static class TelemetryRegistration
{
    public static void AddSafeConsole(this ILoggingBuilder logging)
    {
        logging.AddConsole(o => o.FormatterName = "orderflow-json")
            .AddConsoleFormatter<SafeJsonConsoleFormatter, ConsoleFormatterOptions>();
        logging.Configure(o => o.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
    }

    public static OpenTelemetryBuilder? AddOrderFlowTelemetry(this IServiceCollection services, IConfiguration configuration,
        ILoggingBuilder logging, string serviceName)
    {
        var settings = TelemetrySettings.Read(configuration);
        services.AddSingleton(settings);
        if (!settings.Enabled) return null;
        // Correlate by W3C context only; never forward arbitrary inbound baggage to dependencies.
        Sdk.SetDefaultTextMapPropagator(new TraceContextPropagator());
        var resource = ResourceBuilder.CreateEmpty().AddService(serviceName, autoGenerateServiceInstanceId: false, serviceInstanceId: settings.InstanceName);
        var builder = services.AddOpenTelemetry().ConfigureResource(r => r.AddService(serviceName, autoGenerateServiceInstanceId: false, serviceInstanceId: settings.InstanceName));
        builder.WithTracing(traces =>
        {
            traces.SetResourceBuilder(resource).SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(settings.TraceSampleRatio)))
                .AddSource(OrderFlowTelemetry.SourceName, "Npgsql").AddHttpClientInstrumentation(o => o.RecordException = false)
                .AddProcessor(new TraceRedaction());
            if (settings.OtlpEnabled)
                traces.AddOtlpExporter(o => ConfigureExporter(o, settings, "traces"));
        });
        builder.WithMetrics(metrics =>
        {
            metrics.SetResourceBuilder(resource).AddMeter(OrderFlowTelemetry.MeterName, CatalogCache.MeterName)
                // One matching view per instrument avoids two streams with the same metric name.
                .AddView(instrument => instrument.Name == "orderflow_checkout_duration"
                    ? new ExplicitBucketHistogramConfiguration
                    {
                        Boundaries = [0.01, 0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10], CardinalityLimit = 8, TagKeys = ["outcome"]
                    }
                    : new MetricStreamConfiguration { CardinalityLimit = 100 });
            if (settings.OtlpEnabled)
                metrics.AddOtlpExporter((o, reader) =>
                {
                    ConfigureExporter(o, settings, "metrics");
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10_000;
                    reader.PeriodicExportingMetricReaderOptions.ExportTimeoutMilliseconds = 2_000;
                });
        });
        if (settings.OtlpEnabled)
            logging.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(resource); options.IncludeScopes = false; options.IncludeFormattedMessage = false;
                options.AddProcessor(new LogRedaction());
                options.AddOtlpExporter((o, processor) =>
                {
                    ConfigureExporter(o, settings, "logs");
                    processor.BatchExportProcessorOptions = new BatchExportLogRecordProcessorOptions
                    {
                        MaxQueueSize = 1024, MaxExportBatchSize = 128, ScheduledDelayMilliseconds = 1000, ExporterTimeoutMilliseconds = 2000
                    };
                });
            });
        services.AddSingleton<OperationalMetrics>();
        services.AddHostedService<OperationalMetricsWorker>();
        return builder;
    }
    private static void ConfigureExporter(OtlpExporterOptions options, TelemetrySettings settings, string signal)
    {
        options.Protocol = OtlpExportProtocol.HttpProtobuf;
        options.Endpoint = new Uri(new Uri(settings.Endpoint), "v1/" + signal);
        options.TimeoutMilliseconds = 2000;
        options.BatchExportProcessorOptions = new BatchExportActivityProcessorOptions
        {
            MaxQueueSize = 1024, MaxExportBatchSize = 128, ScheduledDelayMilliseconds = 1000, ExporterTimeoutMilliseconds = 2000
        };
    }
}
