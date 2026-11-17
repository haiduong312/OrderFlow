using Microsoft.Extensions.Configuration;

namespace OrderFlow.Application.Observability;

public sealed class TelemetrySettings
{
    public bool Enabled { get; set; }
    public bool OtlpEnabled { get; set; }
    public bool AllowContainerHost { get; set; }
    public string? InstanceName { get; set; }
    public string Endpoint { get; set; } = "http://127.0.0.1:4318/";
    public double TraceSampleRatio { get; set; } = 1;
    public int SnapshotIntervalSeconds { get; set; } = 15;
    public static TelemetrySettings Read(IConfiguration configuration)
    {
        var settings = configuration.GetSection("Telemetry").Get<TelemetrySettings>() ?? new();
        if (!settings.Enabled) return settings;
        if (settings.InstanceName is { } instance && (instance.Length is < 1 or > 40 || instance.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')))
            throw new InvalidOperationException("Telemetry instance must be a static deployment identity of 1–40 ASCII letters/digits/dashes, never a business identifier.");
        if (settings.TraceSampleRatio is < 0 or > 1 || !double.IsFinite(settings.TraceSampleRatio)
            || settings.SnapshotIntervalSeconds is < 5 or > 300)
            throw new InvalidOperationException("Telemetry requires a finite sample ratio 0–1 and snapshot interval 5–300 seconds.");
        if (settings.OtlpEnabled && (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || uri.Scheme == "http" && !uri.IsLoopback && !(settings.AllowContainerHost && uri.Host == "telemetry" && uri.Port == 4318)
            || uri.AbsolutePath != "/" || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0))
            throw new InvalidOperationException("Telemetry endpoint must be an HTTPS, loopback or explicitly allowed telemetry:4318 OTLP base URL without credentials, query or path.");
        return settings;
    }
}
