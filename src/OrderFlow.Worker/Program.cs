using OrderFlow.Worker;
using OrderFlow.Application.Persistence;
using OrderFlow.Application.Modules.Identity;
using Microsoft.AspNetCore.Identity;

var seedAccount = args is ["seed-development-account"];
var seed = args is ["seed-development"] || seedAccount;
using var host = WorkerHost.Build(seed ? [] : args, runBackgroundJobs: !seed);
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

await host.RunAsync();
