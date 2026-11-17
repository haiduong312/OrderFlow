using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application.Observability;
using Xunit;

namespace OrderFlow.UnitTests;

public sealed class ObservabilityTests
{
    [Fact]
    public void Production_metric_views_apply_explicit_checkout_buckets_and_bounded_cardinality()
    {
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Telemetry:Enabled"]="true" }).Build();
        var services=new ServiceCollection(); services.AddSingleton<IConfiguration>(configuration);
        var export=new MetricCapture();
        services.AddLogging(logging=>services.AddOrderFlowTelemetry(configuration,logging,"orderflow-metric-test")!
            .WithMetrics(metrics=>metrics.AddReader(new PeriodicExportingMetricReader(export,exportIntervalMilliseconds:60_000))));
        using var container=services.BuildServiceProvider(); var provider=container.GetRequiredService<MeterProvider>();
        OrderFlowTelemetry.CheckoutCompleted(Stopwatch.GetTimestamp(),201,false);
        using var meter=new Meter(OrderFlowTelemetry.MeterName);
        var counter=meter.CreateCounter<long>("orderflow_test_cardinality");
        for(var i=0;i<250;i++) counter.Add(1,new KeyValuePair<string,object?>("unexpected",i));
        Assert.True(provider.ForceFlush());
        Assert.Equal(new double[]{0.01,0.05,0.1,0.25,0.5,1,2,5,10,double.PositiveInfinity},export.CheckoutBounds);
        Assert.Equal(101,export.PointCount); // 100 regular combinations plus the SDK overflow point.
        Assert.True(export.Overflow);
    }
    [Fact]
    public void Configuration_is_opt_in_and_rejects_unbounded_or_secret_bearing_export_targets()
    {
        Assert.False(TelemetrySettings.Read(new ConfigurationBuilder().Build()).Enabled);
        foreach (var endpoint in new[] { "http://external.invalid/", "https://user:password@example.invalid/", "https://example.invalid/?key=secret", "https://example.invalid/v1/traces" })
            Assert.Throws<InvalidOperationException>(() => Settings(endpoint));
        Assert.True(Settings("http://127.0.0.1:4318/").OtlpEnabled);
        Assert.Throws<InvalidOperationException>(() => TelemetrySettings.Read(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            { ["Telemetry:Enabled"]="true", ["Telemetry:TraceSampleRatio"]="NaN" }).Build()));
    }
    private static TelemetrySettings Settings(string endpoint) => TelemetrySettings.Read(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string,string?> { ["Telemetry:Enabled"]="true", ["Telemetry:OtlpEnabled"]="true", ["Telemetry:Endpoint"]=endpoint }).Build());

    [Fact]
    public void Npgsql_exception_events_SQL_urls_state_and_error_descriptions_are_redacted_before_export()
    {
        var capture = new SpanCapture();
        using var provider = Sdk.CreateTracerProviderBuilder().AddSource("Npgsql").AddProcessor(new TraceRedaction()).AddProcessor(capture).Build();
        using var source = new ActivitySource("Npgsql");
        using (var activity = source.StartActivity("SELECT private-marker"))
        {
            Assert.NotNull(activity); activity.TraceStateString = "vendor=private-marker";
            activity.SetTag("db.query.text", "SELECT 'private-marker'");
            activity.SetTag("url.full", "https://provider.invalid/?secret=private-marker");
            activity.SetTag("db.system.name", "postgresql");
            activity.AddException(new InvalidOperationException("private-marker"));
            activity.SetStatus(ActivityStatusCode.Error, "private-marker");
        }
        var span = Assert.Single(capture.Spans);
        Assert.Equal("PostgreSQL command", span.DisplayName);
        Assert.Equal(ActivityStatusCode.Error, span.Status); Assert.Null(span.StatusDescription); Assert.Null(span.TraceStateString);
        var data = JsonSerializer.Serialize(new { span.DisplayName, Tags = span.TagObjects.ToArray(), Events = span.Events.Select(e => new { e.Name, Tags = e.Tags.ToArray() }) });
        Assert.DoesNotContain("private-marker", data); Assert.Contains("[redacted]", data);
        Assert.Equal("postgresql", span.GetTagItem("db.system.name"));
    }

    [Fact]
    public void Structured_console_and_OTLP_logs_keep_safe_fields_without_messages_exceptions_or_payloads()
    {
        KeyValuePair<string, object?>[] state = [new("Code", "database_unavailable"), new("Password", "private-marker"), new("Body", "private-marker"), new("TraceId", new string('1',32)), new("Count", 2L)];
        var entry = new LogEntry<KeyValuePair<string,object? >[]>(LogLevel.Error, "OrderFlow.Test", new EventId(2801,"DependencyFailed"), state,
            new Exception("private-marker"), (_, _) => "private-marker");
        using var output = new StringWriter(); new SafeJsonConsoleFormatter().Write(entry, null, output);
        Assert.DoesNotContain("private-marker", output.ToString()); Assert.Contains("database_unavailable", output.ToString());
        var export = new LogCapture();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddOpenTelemetry(options =>
        {
            options.AddProcessor(new LogRedaction()); options.AddProcessor(new SimpleLogRecordExportProcessor(export));
        }));
        loggerFactory.CreateLogger("OrderFlow.Test").Log(LogLevel.Error, new EventId(2801,"DependencyFailed"), state,
            new Exception("private-marker"), (_, _) => "private-marker");
        Assert.DoesNotContain("private-marker", Assert.Single(export.Data));
        Assert.Contains("database_unavailable", export.Data.Single());
    }

    [Fact]
    public void Checkout_and_processing_metrics_have_only_finite_outcome_component_and_kind_dimensions()
    {
        var rows = new ConcurrentQueue<(string Name, KeyValuePair<string, object?>[] Tags)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) => { if (instrument.Meter.Name == OrderFlowTelemetry.MeterName) current.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) => rows.Enqueue((instrument.Name, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) => rows.Enqueue((instrument.Name, tags.ToArray())));
        listener.Start();
        foreach (var status in new[] { 201, 400, 401, 409, 500, 503 }) OrderFlowTelemetry.CheckoutCompleted(Stopwatch.GetTimestamp(), status, false);
        OrderFlowTelemetry.CheckoutCompleted(Stopwatch.GetTimestamp(), 499, true);
        OrderFlowTelemetry.ProcessingFailed(ProcessingComponent.Notifications, FailureKind.InvalidMessage);
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.All(row.Tags, tag => Assert.Contains(tag.Key, new[] { "outcome", "component", "kind" })));
        Assert.Equal(new[] { "cancelled", "rejected", "server_error", "success" }, rows.Where(r=>r.Name=="orderflow_checkout_requests").SelectMany(r=>r.Tags).Select(t=>(string)t.Value!).Distinct().Order().ToArray());
    }

    [Fact]
    public void Snapshot_callbacks_do_not_read_dependencies_and_failed_samples_preserve_values_with_increasing_age()
    {
        var clock = new TestClock(); using var metrics = new OperationalMetrics(clock);
        var values = new Dictionary<string,double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (i,l) => { if(i.Meter.Name==OrderFlowTelemetry.MeterName) l.EnableMeasurementEvents(i); };
        listener.SetMeasurementEventCallback<double>((i,v,_,_) => values[i.Name]=v);
        listener.SetMeasurementEventCallback<int>((i,v,_,_) => values[i.Name]=v);
        listener.Start(); listener.RecordObservableInstruments(); Assert.False(values.ContainsKey("orderflow_outbox_pending"));
        metrics.Update(new(clock.GetUtcNow(), 3, 30, 1, 2, 15, 4, 1));
        clock.Advance(); metrics.Failed(); listener.RecordObservableInstruments();
        Assert.Equal(3, values["orderflow_outbox_pending"]); Assert.Equal(0, values["orderflow_telemetry_sample_success"]);
        Assert.Equal(20, values["orderflow_telemetry_snapshot_age"]);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = new(2035,1,1,0,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow()=>now;
        public void Advance()=>now=now.AddSeconds(20);
    }
    private sealed class SpanCapture : BaseProcessor<Activity>
    {
        public ConcurrentQueue<Activity> Spans { get; } = new();
        public override void OnEnd(Activity span)=>Spans.Enqueue(span);
    }
    private sealed class LogCapture : BaseExporter<LogRecord>
    {
        public List<string> Data { get; } = [];
        public override ExportResult Export(in Batch<LogRecord> batch)
        {
            foreach(var log in batch) Data.Add(JsonSerializer.Serialize(new {log.Body,log.FormattedMessage, Exception=log.Exception?.ToString(),log.Attributes}));
            return ExportResult.Success;
        }
    }
    private sealed class MetricCapture : BaseExporter<Metric>
    {
        public double[] CheckoutBounds {get;private set;}=[];
        public int PointCount {get;private set;}
        public bool Overflow {get;private set;}
        public override ExportResult Export(in Batch<Metric> batch)
        {
            foreach(var metric in batch)
            {
                if(metric.Name=="orderflow_checkout_duration")
                    foreach(ref readonly var point in metric.GetMetricPoints())
                    {
                        var bounds=new List<double>();
                        foreach(var bucket in point.GetHistogramBuckets()) bounds.Add(bucket.ExplicitBound);
                        CheckoutBounds=bounds.ToArray();
                    }
                if(metric.Name=="orderflow_test_cardinality")
                {
                    PointCount=0;
                    foreach(ref readonly var point in metric.GetMetricPoints())
                    {
                        PointCount++;
                        foreach(var tag in point.Tags) if(tag.Key=="otel.metric.overflow" && tag.Value is true) Overflow=true;
                    }
                }
            }
            return ExportResult.Success;
        }
    }
}
