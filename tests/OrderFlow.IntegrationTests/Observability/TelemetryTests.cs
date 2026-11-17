using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;
using OrderFlow.Application.Messaging;
using OrderFlow.Application.Modules.Notifications;
using OrderFlow.Application.Modules.Ordering;
using OrderFlow.Application.Modules.Payments;
using OrderFlow.Application.Observability;
using OrderFlow.Application.Persistence;
using OrderFlow.IntegrationTests.Auth;
using OrderFlow.IntegrationTests.Messaging;
using OrderFlow.IntegrationTests.Notifications;
using OrderFlow.IntegrationTests.Ordering;
using OrderFlow.IntegrationTests.Payments;
using OrderFlow.IntegrationTests.Persistence;
using OrderFlow.Worker;
using OrderFlow.Worker.Messaging;
using Xunit;
using Xunit.Abstractions;

namespace OrderFlow.IntegrationTests.Observability;

[Collection("Messaging"), Trait("Category", "PostgreSQL"), Trait("Category", "RabbitMQ")]
public sealed class TelemetryTests(PostgresServer server, RabbitMqServer broker, ITestOutputHelper output) : PostgresTest(server)
{
    private readonly ManualTimeProvider clock = new(new(2035,1,1,0,0,0,TimeSpan.Zero));
    private ServiceProvider Services()
    {
        var services = new ServiceCollection(); services.AddLogging(logging =>
        {
            logging.AddSafeConsole();
            if (OtlpBase() is { } endpoint) logging.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService("orderflow-verification", autoGenerateServiceInstanceId:false));
                options.IncludeScopes=false; options.IncludeFormattedMessage=false; options.AddProcessor(new LogRedaction());
                options.AddOtlpExporter(o=>{o.Protocol=OtlpExportProtocol.HttpProtobuf; o.Endpoint=new Uri(endpoint,"v1/logs");o.TimeoutMilliseconds=2000;});
            });
        }); services.AddSingleton<TimeProvider>(clock);
        services.AddDbContext<OrderFlowDbContext>(options => PersistenceRegistration.Configure(options, Database.ConnectionString));
        services.AddScoped<NotificationProcessor>(); services.AddSingleton<DatabaseReadiness>();
        return services.BuildServiceProvider(validateScopes:true);
    }
    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback,0); listener.Start(); var port=((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }

    [Fact]
    public async Task Real_HTTP_outbox_and_RabbitMQ_consumer_keep_one_trace_and_observe_backlog_growth_failure_and_recovery()
    {
        await using var api = new AuthApiFactory(Database, clock:clock);
        var products = await CheckoutTestData.StockAsync(Database, api, count:1, stock:100);
        var (owner,_) = await AuthenticatedBrowser.CreateAsync(api,"Customer"); using var browser = owner;
        using var services = Services(); var scopes=services.GetRequiredService<IServiceScopeFactory>();
        using var metrics = new OperationalMetrics(clock); var spans = new SpanCapture();
        using var tracing = BuildTraces(spans); using var metering=BuildMetrics();
        var traceId=ActivityTraceId.CreateRandom();
        using var request=new HttpRequestMessage(HttpMethod.Post,"/api/orders") {Content=JsonContent.Create(CheckoutTestData.Body(products))};
        request.Headers.Add("Idempotency-Key",Guid.NewGuid().ToString("D"));
        request.Headers.Add("X-CSRF-TOKEN",await BrowserRequests.TokenAsync(browser));
        request.Headers.Add("traceparent",$"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        request.Headers.Add("tracestate","vendor=private-marker");
        using var response=await browser.SendAsync(request); var order=await CheckoutTestData.CreatedAsync(response);
        Assert.Null(Activity.Current); // Delivery work cannot inherit the original HTTP execution context.
        await metrics.SampleAsync(scopes,default); Assert.Equal(1,metrics.Snapshot!.OutboxPending);
        Assert.True(metering.ForceFlush()); await CheckBackendAsync("orderflow_outbox_pending",1);
        clock.Advance(TimeSpan.FromSeconds(3));
        await OrderManagementTestData.CreateAsync(browser,CheckoutTestData.Body(products));
        await metrics.SampleAsync(scopes,default); Assert.Equal(2,metrics.Snapshot!.OutboxPending); Assert.Equal(3,metrics.Snapshot.OutboxOldestAge);
        Assert.True(metering.ForceFlush()); await CheckBackendAsync("orderflow_outbox_pending",2);
        await CheckBackendAsync("orderflow_checkout_requests_total",2,"success");
        using(var rejected=await CheckoutTestData.PostAsync(browser,CheckoutTestData.Body(products,quantity:0),Guid.NewGuid().ToString("D")))
            Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
        var configuration=api.Services.GetRequiredService<IConfiguration>();
        var connection=configuration.GetConnectionString("OrderFlow");
        try
        {
            configuration["ConnectionStrings:OrderFlow"]=new NpgsqlConnectionStringBuilder(Database.ConnectionString){Port=ClosedPort(),Pooling=false,Timeout=1}.ConnectionString;
            using var failed=await CheckoutTestData.PostAsync(browser,CheckoutTestData.Body(products),Guid.NewGuid().ToString("D"));
            Assert.Equal(HttpStatusCode.InternalServerError,failed.StatusCode);
        }
        finally { configuration["ConnectionStrings:OrderFlow"]=connection; }
        Assert.True(metering.ForceFlush());
        await CheckBackendAsync("orderflow_checkout_requests_total",1,"rejected");
        await CheckBackendAsync("orderflow_checkout_requests_total",1,"server_error");

        var settings=broker.Settings("telemetry-"+Guid.NewGuid().ToString("N"));
        await using (var broken=new RabbitMqEventPublisher(broker.Settings("telemetry-broken-"+Guid.NewGuid().ToString("N"),port:ClosedPort(),timeout:1)))
            Assert.True(await new OutboxPublishingIteration(scopes,broken,clock,services.GetRequiredService<ILogger<OutboxPublishingIteration>>()).ProcessNextAsync(default));
        using(var ready=await browser.GetAsync("/api/health/ready")) Assert.Equal(HttpStatusCode.OK,ready.StatusCode);
        clock.Advance(TimeSpan.FromSeconds(5));
        var acknowledged=new DeliveryLog<NotificationDeliveryHandler>(2);
        var handler=new NotificationDeliveryHandler(scopes,new CombinedLog<NotificationDeliveryHandler>(services.GetRequiredService<ILogger<NotificationDeliveryHandler>>(),acknowledged));
        await using var subscription=await NotificationConsumerSubscription.StartAsync(settings,new(){Prefetch=1},settings.NotificationQueue,handler,default);
        await using var publisher=new RabbitMqEventPublisher(settings);
        var iteration=new OutboxPublishingIteration(scopes,publisher,clock,services.GetRequiredService<ILogger<OutboxPublishingIteration>>());
        Assert.True(await iteration.ProcessNextAsync(default)); Assert.True(await iteration.ProcessNextAsync(default));
        await acknowledged.Acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(await iteration.ProcessNextAsync(default)); Assert.True(await iteration.ProcessNextAsync(default));
        await metrics.SampleAsync(scopes,default); Assert.Equal(0,metrics.Snapshot!.OutboxPending); Assert.Equal(0,metrics.Snapshot.OutboxOldestAge);
        Assert.True(metering.ForceFlush()); await CheckBackendAsync("orderflow_outbox_pending",0);
        await using(var db=Database.CreateContext())
        {
            Assert.Equal(2,await db.Notifications.CountAsync());
            var original=await db.OutboxMessages.SingleAsync(m=>m.AggregateId==order.Id && m.EventType=="ordering.order-created");
            Assert.True(ActivityContext.TryParse(original.TraceParent,original.TraceState,out var context)); Assert.Equal(traceId,context.TraceId);
            var consumed=Assert.Single(spans.Spans,s=>s.TraceId==traceId && s.OperationName=="notifications.handle");
            var child=await db.OutboxMessages.SingleAsync(m=>m.CausationId==original.EventId && m.EventType=="notifications.notification-created");
            Assert.Equal(consumed.Id,child.TraceParent);
        }
        Assert.Contains(spans.Spans,s=>s.TraceId==traceId && s.Kind==ActivityKind.Server);
        Assert.Contains(spans.Spans,s=>s.TraceId==traceId && s.OperationName=="checkout.create");
        Assert.Contains(spans.Spans,s=>s.TraceId==traceId && s.OperationName=="outbox.publish");
        Assert.Contains(spans.Spans,s=>s.OperationName=="outbox.publish" && s.Status==ActivityStatusCode.Error);
        Assert.Contains(spans.Spans,s=>s.TraceId==traceId && s.Source.Name=="Npgsql");
        await using(var db=Database.CreateContext())
            await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("DO $$ BEGIN RAISE EXCEPTION 'private-marker'; END $$;"));
        var dump=JsonSerializer.Serialize(spans.Spans.Select(s=>new {s.DisplayName,TraceId=s.TraceId.ToString(),SpanId=s.SpanId.ToString(),Parent=s.ParentSpanId.ToString(),Status=s.Status.ToString(),s.StatusDescription,s.TraceStateString,Tags=s.TagObjects.ToArray(),Events=s.Events.Select(e=>new{e.Name,Tags=e.Tags.ToArray()})}));
        Assert.DoesNotContain("private-marker",dump); Assert.DoesNotContain("db.query.text",dump);
        Directory.CreateDirectory("/tmp/orderflow-telemetry/evidence"); await File.WriteAllTextAsync("/tmp/orderflow-telemetry/evidence/order-trace.json",dump);
        Assert.True(tracing.ForceFlush()); await CheckTraceBackendAsync(traceId.ToString());
        await CheckLogsBackendAsync(traceId.ToString());
        output.WriteLine($"Durable trace {traceId}; real outbox pending samples 1 -> 2 -> 0; two notifications committed. Optional collector verification: {VerificationBase() is not null}.");
    }

    [Fact]
    public async Task Snapshot_SQL_counts_overdue_work_and_pending_refunds_without_turning_dependency_failure_into_zero()
    {
        await using var api=new AuthApiFactory(Database,clock:clock);
        var products=await CheckoutTestData.StockAsync(Database,api,count:1,stock:100);
        var (owner,_)=await AuthenticatedBrowser.CreateAsync(api,"Customer"); using var browser=owner;
        var created=await OrderManagementTestData.CreateAsync(browser,CheckoutTestData.Body(products));
        using(var payment=await PaymentInitiationTests.InitiateAsync(browser,created.Id)) Assert.Equal(HttpStatusCode.Accepted,payment.StatusCode);
        using(var cancelled=await OrderManagementTestData.CancelAsync(browser,created.Id)) Assert.Equal(HttpStatusCode.OK,cancelled.StatusCode);
        // Verified late capture produces the real durable compensation intention.
        await using(var db=Database.CreateContext())
        {
            await using var transaction=await db.Database.BeginTransactionAsync();
            var order=await db.Orders.SingleAsync(); var attempt=await db.PaymentAttempts.SingleAsync();
            var result=new GatewayPaymentResult(attempt.ProviderRequestKey,attempt.Id,order.Id,"telemetry-transaction",attempt.Amount.ToString(),"USD",GatewayPaymentState.Succeeded,"telemetry-capture");
            await PaymentSettlement.LockIdentitiesAsync(db,result,default);
            Assert.Null(await new PaymentSettlement(db).SettleAsync(order,attempt,result,new PaymentEvidence(Guid.NewGuid(),null,null),clock.GetUtcNow(),default));
            await db.SaveChangesAsync(); await transaction.CommitAsync();
        }
        await OrderManagementTestData.CreateAsync(browser,CheckoutTestData.Body(products));
        using var services=Services(); var scopes=services.GetRequiredService<IServiceScopeFactory>(); using var metrics=new OperationalMetrics(clock);
        clock.Advance(TimeSpan.FromMinutes(15)); await metrics.SampleAsync(scopes,default);
        Assert.Equal(1,metrics.Snapshot!.ExpiryDue); Assert.Equal(0,metrics.Snapshot.ExpiryOldestLag); Assert.Equal(1,metrics.Snapshot.RefundsPending);
        clock.Advance(TimeSpan.FromSeconds(9)); await metrics.SampleAsync(scopes,default); Assert.Equal(9,metrics.Snapshot!.ExpiryOldestLag);
        await using(var scope=services.CreateAsyncScope()) Assert.True(await new OrderExpiryProcessor(scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>(),clock).ProcessNextAsync(default));
        await metrics.SampleAsync(scopes,default); Assert.Equal(0,metrics.Snapshot!.ExpiryDue);
        await using(var db=Database.CreateContext())
        {
            var refund=await db.PaymentRefunds.SingleAsync(); refund.Claim(clock.GetUtcNow());
            refund.Observe(null,"controlled_review",false,clock.GetUtcNow()); await db.SaveChangesAsync();
        }
        await metrics.SampleAsync(scopes,default); Assert.Equal(0,metrics.Snapshot!.RefundsPending); Assert.Equal(1,metrics.Snapshot.RefundsReview);
        var previous=metrics.Snapshot;
        var broken=new ServiceCollection().AddLogging().AddDbContext<OrderFlowDbContext>(o=>PersistenceRegistration.Configure(o,new NpgsqlConnectionStringBuilder(Database.ConnectionString){Port=ClosedPort(),Pooling=false,Timeout=1}.ConnectionString));
        using(var provider=broken.BuildServiceProvider())
        {
            var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>metrics.SampleAsync(provider.GetRequiredService<IServiceScopeFactory>(),default));
            Assert.IsType<NpgsqlException>(error.InnerException);
        }
        Assert.Same(previous,metrics.Snapshot);
    }

    [Fact]
    public async Task API_and_worker_readiness_have_intentional_dependency_policies_and_schema_check()
    {
        using var services=Services(); var database=services.GetRequiredService<DatabaseReadiness>();
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            {["OutboxPublishing:Enabled"]="true",["EmailSending:Enabled"]="true",["EmailSending:Host"]="127.0.0.1",["EmailSending:Port"]=ClosedPort().ToString()}).Build();
        using var dependencies=new ServiceCollection().AddSingleton(broker.Settings("readiness",port:ClosedPort())).BuildServiceProvider();
        var worker=new WorkerReadiness(database,config,dependencies,NullLogger<WorkerReadiness>.Instance);
        var report=await worker.CheckAsync(default); Assert.False(report.Ready); Assert.True(report.Dependencies["postgresql"]);
        Assert.False(report.Dependencies["rabbitmq"]); Assert.False(report.Dependencies["smtp"]);
        Assert.True((await new ApiReadiness(database).CheckAsync(default)).Ready);
        await using var old=await Server.CreateDatabaseAsync("20261112082134_AddEmailDeliveries");
        await using var api=new AuthApiFactory(old);
        using var browser=api.Browser(); using var response=await browser.GetAsync("/api/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal("Unhealthy",(await response.Content.ReadFromJsonAsync<Api.LivenessResponse>())!.Status);
        using var live=await browser.GetAsync("/api/health/live"); Assert.Equal(HttpStatusCode.OK,live.StatusCode);
    }

    [Fact]
    public async Task API_and_worker_start_with_unreachable_OTLP_and_API_database_failure_affects_readiness_only()
    {
        var settings=new Dictionary<string,string?>
        {
            ["Telemetry:Enabled"]="true",["Telemetry:OtlpEnabled"]="true",["Telemetry:Endpoint"]=$"http://127.0.0.1:{ClosedPort()}/",
            ["ConnectionStrings:OrderFlow"]=new NpgsqlConnectionStringBuilder(Database.ConnectionString){Port=ClosedPort(),Pooling=false,Timeout=1}.ConnectionString,
            ["OrderExpiry:Enabled"]="false"
        };
        await using(var app=Api.Program.Build([],builder=>{builder.WebHost.UseTestServer();builder.Configuration.AddInMemoryCollection(settings);}))
        {
            await app.StartAsync().WaitAsync(TimeSpan.FromSeconds(5)); using var client=app.GetTestClient();
            using var live=await client.GetAsync("/api/health/live"); Assert.Equal(HttpStatusCode.OK,live.StatusCode);
            using var ready=await client.GetAsync("/api/health/ready"); Assert.Equal(HttpStatusCode.ServiceUnavailable,ready.StatusCode);
            var body=await ready.Content.ReadAsStringAsync(); Assert.DoesNotContain("Password",body); Assert.DoesNotContain("Host",body);
            await app.StopAsync();
        }
        settings["ConnectionStrings:OrderFlow"]=Database.ConnectionString;
        using var host=WorkerHost.Build([],builder=>builder.Configuration.AddInMemoryCollection(settings));
        await host.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True((await host.Services.GetRequiredService<IRuntimeReadiness>().CheckAsync(default)).Ready);
        await host.StopAsync();
    }

    private static Uri? VerificationBase()
    {
        var raw=Environment.GetEnvironmentVariable("ORDERFLOW_TELEMETRY_VERIFY_BASE");
        if(raw is null)return null;
        var uri=new Uri(raw); if(!uri.IsLoopback || uri.Scheme!="http") throw new InvalidOperationException("Telemetry verification must target loopback HTTP."); return uri;
    }
    private static Uri? OtlpBase()
    {
        if(VerificationBase() is null)return null;
        var uri=new Uri(Environment.GetEnvironmentVariable("ORDERFLOW_TELEMETRY_VERIFY_OTLP")??"http://127.0.0.1:4318/");
        if(!uri.IsLoopback || uri.Scheme!="http")throw new InvalidOperationException("Telemetry verification OTLP must be loopback.");return uri;
    }
    private static TracerProvider BuildTraces(SpanCapture capture)
    {
        var builder=Sdk.CreateTracerProviderBuilder().SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService("orderflow-verification",autoGenerateServiceInstanceId:false))
            .AddSource(OrderFlowTelemetry.SourceName,"Npgsql").AddAspNetCoreInstrumentation(o=>o.RecordException=false)
            .AddProcessor(new TraceRedaction()).AddProcessor(capture);
        if(OtlpBase() is {} endpoint)builder.AddOtlpExporter(o=>{o.Protocol=OtlpExportProtocol.HttpProtobuf;o.Endpoint=new Uri(endpoint,"v1/traces");o.TimeoutMilliseconds=2000;});
        return builder.Build();
    }
    private static MeterProvider BuildMetrics()
    {
        var builder=Sdk.CreateMeterProviderBuilder().SetResourceBuilder(ResourceBuilder.CreateEmpty().AddService("orderflow-verification",autoGenerateServiceInstanceId:false))
            .AddMeter(OrderFlowTelemetry.MeterName).AddView(instrument=>instrument.Name=="orderflow_checkout_duration"
                ? new ExplicitBucketHistogramConfiguration { Boundaries=[0.01,0.05,0.1,0.25,0.5,1,2,5,10],CardinalityLimit=8,TagKeys=["outcome"] }
                : new MetricStreamConfiguration{CardinalityLimit=100});
        if(OtlpBase() is {} endpoint)builder.AddOtlpExporter((o,r)=>{o.Protocol=OtlpExportProtocol.HttpProtobuf;o.Endpoint=new Uri(endpoint,"v1/metrics");o.TimeoutMilliseconds=2000;r.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds=60_000;});
        return builder.Build();
    }
    private static async Task CheckBackendAsync(string metric,double expected,string? outcome=null)
    {
        if(VerificationBase() is not {} endpoint)return;
        using var client=new HttpClient{BaseAddress=endpoint,Timeout=TimeSpan.FromSeconds(3)};
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while(true)
        {
            using var response=await client.GetAsync("/api/datasources/proxy/uid/prometheus/api/v1/query?query="+Uri.EscapeDataString(metric+"{job=\"orderflow-verification\""+(outcome is null ? "" : ",outcome=\""+outcome+"\"")+"}"),timeout.Token);
            response.EnsureSuccessStatusCode();using var data=JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var rows=data.RootElement.GetProperty("data").GetProperty("result");
            if(rows.GetArrayLength()>0 && double.Parse(rows[0].GetProperty("value")[1].GetString()!,System.Globalization.CultureInfo.InvariantCulture)==expected)return;
            await Task.Delay(100,timeout.Token); // Observe actual ingestion, not a concurrency/race sleep.
        }
    }
    private static async Task CheckTraceBackendAsync(string id)
    {
        if(VerificationBase() is not {} endpoint)return;
        using var client=new HttpClient{BaseAddress=endpoint,Timeout=TimeSpan.FromSeconds(3)};
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while(true)
        {
            using var response=await client.GetAsync("/api/datasources/proxy/uid/tempo/api/traces/"+id,timeout.Token);
            var body=await response.Content.ReadAsStringAsync(timeout.Token);
            if(response.IsSuccessStatusCode && body.Contains("notifications.handle",StringComparison.Ordinal))
            {
                Assert.Contains("checkout.create",body); Assert.Contains("outbox.publish",body);Assert.DoesNotContain("private-marker",body);
                await File.WriteAllTextAsync("/tmp/orderflow-telemetry/evidence/tempo-trace.json",body,timeout.Token); return;
            }
            await Task.Delay(100,timeout.Token);
        }
    }
    private sealed class SpanCapture : BaseProcessor<Activity>
    {
        public ConcurrentQueue<Activity> Spans {get;}=new();
        public override void OnEnd(Activity data)=>Spans.Enqueue(data);
    }
    private static async Task CheckLogsBackendAsync(string traceId)
    {
        if(VerificationBase() is not {} endpoint)return;
        using var client=new HttpClient{BaseAddress=endpoint,Timeout=TimeSpan.FromSeconds(3)};
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while(true)
        {
            using var response=await client.GetAsync("/api/datasources/proxy/uid/loki/loki/api/v1/query_range?limit=100&query="+Uri.EscapeDataString("{service_name=\"orderflow-verification\"} | trace_id = \""+traceId+"\" |= \"OutboxPublishFailed\""),timeout.Token);
            response.EnsureSuccessStatusCode(); var body=await response.Content.ReadAsStringAsync(timeout.Token);
            if(body.Contains("OutboxPublishFailed",StringComparison.Ordinal))
            {
                Assert.DoesNotContain("private-marker",body); Assert.Contains("broker_",body);
                await File.WriteAllTextAsync("/tmp/orderflow-telemetry/evidence/loki-failure.json",body,timeout.Token);return;
            }
            await Task.Delay(100,timeout.Token);
        }
    }
    private sealed class CombinedLog<T>(ILogger<T> actual, ILogger<T> observer) : ILogger<T>
    {
        public bool IsEnabled(LogLevel level)=>true;
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull=>actual.BeginScope(state);
        public void Log<TState>(LogLevel level,EventId id,TState state,Exception? error,Func<TState,Exception?,string> formatter)
        { actual.Log(level,id,state,error,formatter); observer.Log(level,id,state,error,formatter); }
    }
}
