using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

namespace OrderFlow.Api.Hosting;

public sealed class DeploymentHostingSettings
{
    public bool Enabled { get; set; }
    public string ProxyAddress { get; set; } = "";
    public string PublicHost { get; set; } = "localhost";
    public string KeyDirectory { get; set; } = "";
    public string ApplicationName { get; set; } = "OrderFlow.Deployment";
    public string CertificatePath { get; set; } = "";
    public string CertificatePassword { get; set; } = "";
    public string InstanceName { get; set; } = "";
    public bool ExposeInstanceHeader { get; set; }
}

public static class DeploymentHosting
{
    // Explicit single operator step BEFORE cold replicas start; no session/cookie or HTTP server.
    // Reuse the configured encrypted repository/application identity; never export key material.
    public static void InitializeKeyRing(IServiceProvider services)
    {
        if (!services.GetRequiredService<IConfiguration>().GetValue<bool>("Deployment:Enabled"))
            throw new InvalidOperationException("Key initialization requires explicit Deployment:Enabled and protected shared storage.");
        var protector = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("OrderFlow.KeyRingBootstrap.v1");
        const string probe = "OrderFlow key-ring verification";
        if (protector.Unprotect(protector.Protect(probe)) != probe)
            throw new InvalidOperationException("The shared key ring could not be verified.");
    }

    public static DeploymentHostingSettings AddDeploymentHosting(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("Deployment").Get<DeploymentHostingSettings>() ?? new();
        if (!settings.Enabled) return settings;
        if (!IPAddress.TryParse(settings.ProxyAddress, out var proxy) || proxy.Equals(IPAddress.Any) || proxy.Equals(IPAddress.IPv6Any)
            || !Path.IsPathFullyQualified(settings.KeyDirectory) || !Path.IsPathFullyQualified(settings.CertificatePath)
            || settings.ApplicationName.Length is < 1 or > 100 || settings.CertificatePassword.Length < 16
            || Uri.CheckHostName(settings.PublicHost) == UriHostNameType.Unknown
            || settings.InstanceName.Length is < 1 or > 40 || settings.InstanceName.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new InvalidOperationException("Configure explicit deployment proxy/host, shared key directory, protected certificate and safe application/instance identity.");
        Directory.CreateDirectory(settings.KeyDirectory);
        if (OperatingSystem.IsLinux() && (File.GetUnixFileMode(settings.KeyDirectory) &
            (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            throw new InvalidOperationException("The shared Data Protection directory must be private (0700) to the API identity.");
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(settings.CertificatePath, settings.CertificatePassword, X509KeyStorageFlags.EphemeralKeySet);
        if (!certificate.HasPrivateKey) { certificate.Dispose(); throw new InvalidOperationException("Data Protection requires the private wrapping certificate."); }
        services.AddSingleton<IHostedService>(_ => new CertificateLifetime(certificate));
        services.AddDataProtection().SetApplicationName(settings.ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(settings.KeyDirectory)).ProtectKeysWithCertificate(certificate);
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.ForwardLimit = 1; options.RequireHeaderSymmetry = true;
            options.KnownIPNetworks.Clear(); options.KnownProxies.Clear();
            options.KnownProxies.Add(proxy); options.KnownProxies.Add(proxy.MapToIPv6());
            options.AllowedHosts.Clear(); options.AllowedHosts.Add(settings.PublicHost);
        });
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(25));
        return settings;
    }

    private sealed class CertificateLifetime(X509Certificate2 certificate) : IHostedService, IDisposable
    {
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public Task StopAsync(CancellationToken token) => Task.CompletedTask;
        public void Dispose() => certificate.Dispose();
    }
}
