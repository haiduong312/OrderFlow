using OrderFlow.Application.Persistence;

namespace OrderFlow.Worker;

public static class WorkerHost
{
    public static IHost Build(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddOrderFlowPersistence(builder.Configuration);

        // No background jobs exist yet. The host waits for normal shutdown signals.
        return builder.Build();
    }
}
