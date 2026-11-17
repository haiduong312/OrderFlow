using OrderFlow.Worker;
using OrderFlow.Application.Persistence;
using OrderFlow.Application.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using OrderFlow.Application.Observability;
using System.Text.Json;
using OrderFlow.Application.Hosting;

var seedAccount = args is ["seed-development-account"];
var seed = args is ["seed-development"] || seedAccount;
var readiness = args is ["check-readiness"];
var migrate = args is ["migrate"];
using var host = WorkerHost.Build(seed || readiness || migrate ? [] : args, runBackgroundJobs: !seed && !readiness && !migrate);
if (migrate)
{
    using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    await DeploymentMigration.RunAsync(host.Services, budget.Token);
    return;
}
if (readiness)
{
    await host.StartAsync();
    try
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var report = await host.Services.GetRequiredService<IRuntimeReadiness>().CheckAsync(budget.Token);
        Console.WriteLine(JsonSerializer.Serialize(report));
        Environment.ExitCode = report.Ready ? 0 : 1;
    }
    finally { await host.StopAsync(); }
    return;
}
if (seed)
{
    if (!host.Services.GetRequiredService<IHostEnvironment>().IsDevelopment())
    {
        throw new InvalidOperationException("Development seeding requires DOTNET_ENVIRONMENT=Development.");
    }

    var account = host.Services.GetRequiredService<IConfiguration>()
        .GetSection("DevelopmentSeed:Account").Get<DevelopmentAccountOptions>() ?? new();
    if (seedAccount)
    {
        account.Validate(host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
    }

    await host.StartAsync();
    try
    {
        await using var scope = host.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
        var token = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
        if (seedAccount)
        {
            var created = await DevelopmentAccountSeeder.SeedAsync(
                host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName, account,
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), database,
                scope.ServiceProvider.GetRequiredService<TimeProvider>(), token);
            host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DevelopmentSeed")
                .LogInformation("Development account seed finished; account created: {Created}. Existing credentials were not reset.", created);
        }
        else
        {
            await DevelopmentSeeder.SeedRolesAsync(database, token);
        }
    }
    finally
    {
        await host.StopAsync();
    }

    return;
}

await StartupReadiness.WaitAsync(host.Services, host.Services.GetRequiredService<IConfiguration>(), CancellationToken.None);
var backgroundServices = host.Services.GetServices<IHostedService>().OfType<BackgroundService>().ToArray();
await host.RunAsync();
if (backgroundServices.Any(service => service.ExecuteTask?.IsFaulted == true)) Environment.ExitCode = 1;
