using OrderFlow.Worker;

using var host = WorkerHost.Build(args);
await host.RunAsync();
