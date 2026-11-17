using Microsoft.Extensions.Configuration;

namespace OrderFlow.Application.Observability;

public sealed class TelemetrySettings
{
    public bool Enabled { get; set; }
    public bool OtlpEnabled { get; set; }
    public string Endpoint { get; set; } = "http://127.0.0.1:4318/";
    public double TraceSampleRatio { get; set; } = 1;
    public int SnapshotIntervalSeconds { get; set; } = 15;
    public static TelemetrySettings Read(IConfiguration configuration)
    {
        var settings = configuration.GetSection("Telemetry").Get<TelemetrySettings>() ?? new();
        if (!settings.Enabled) return settings;
        if (settings.TraceSampleRatio is < 0 or > 1 || !double.IsFinite(settings.TraceSampleRatio)
            || settings.SnapshotIntervalSeconds is < 5 or > 300)
            throw new InvalidOperationException("Telemetry requires a finite sample ratio 0–1 and snapshot interval 5–300 seconds.");
        if (settings.OtlpEnabled && (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.Scheme == "http" && !uri.IsLoopback
            || uri.AbsolutePath != "/" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0))
            throw new InvalidOperationException("Telemetry endpoint must be an HTTPS or loopback HTTP OTLP base URL without credentials, query or path.");
        return settings;
    }
}
