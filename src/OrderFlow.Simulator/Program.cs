namespace OrderFlow.Simulator;

public static class Program
{
    public static async Task Main(string[] args) => await SimulatorHost.Build(args).RunAsync();
}
