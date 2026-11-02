using OrderFlow.Worker;
using OrderFlow.Application.Persistence;

var seed = args is ["seed-development"];
using var host = WorkerHost.Build(seed ? [] : args);
if (seed)
{
    if (!host.Services.GetRequiredService<IHostEnvironment>().IsDevelopment())
    {
        throw new InvalidOperationException("Development seeding requires DOTNET_ENVIRONMENT=Development.");
    }

    await host.StartAsync();
    try
    {
        await using var scope = host.Services.CreateAsyncScope();
        await DevelopmentSeeder.SeedRolesAsync(scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>(),
            host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
    }
    finally
    {
        await host.StopAsync();
    }

    return;
}

await host.RunAsync();
