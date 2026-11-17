using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OrderFlow.IntegrationTests;

// Instrumentation of existing test-only rendezvous/transport failpoints. Never shipped in an API/Worker image.
internal static class FailureEvidence
{
    public static Stopwatch? Start() => Environment.GetEnvironmentVariable("ORDERFLOW_FAILURE_LOAD_CONFIG") is null ? null : Stopwatch.StartNew();

    public static void Record(Stopwatch? timer, string scenario, object facts)
    {
        if (timer is null) return;
        var path = Environment.GetEnvironmentVariable("ORDERFLOW_FAILURE_LOAD_CONFIG")!;
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var config = document.RootElement;
        if (!config.GetProperty("disposable").GetBoolean()
            || !Regex.IsMatch(config.GetProperty("project").GetString()!, "^orderflow-load-[a-f0-9]{32}$")
            || !Regex.IsMatch(scenario, "^[A-Za-z0-9-]{1,80}$"))
            throw new InvalidOperationException("Failure measurements require the explicit disposable lab manifest.");
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "results", "fault-timing");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, scenario + ".json"), JsonSerializer.Serialize(new
        {
            scenario, recovery_wall_ms = timer.Elapsed.TotalMilliseconds,
            latency_percentiles = "unmeasured: deterministic service/SQL harness, not an HTTP load sample",
            facts
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
