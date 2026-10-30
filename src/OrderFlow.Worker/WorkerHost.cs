namespace OrderFlow.Worker;

public static class WorkerHost
{
    public static IHost Build(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton(TimeProvider.System);

        // No background jobs exist yet. The host waits for normal shutdown signals.
        return builder.Build();
    }
}
