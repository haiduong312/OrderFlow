using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderFlow.Api.Hosting;
using OrderFlow.Application.Observability;
using OrderFlow.Worker.Emails;
using Xunit;

namespace OrderFlow.IntegrationTests.Deployment;

public sealed class DeploymentHostingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "orderflow-hosting-" + Guid.NewGuid().ToString("N"));
    private const string Password = "Fixture-only-wrapping-password-123!";
    private IConfiguration Configuration()
    {
        Directory.CreateDirectory(root);
        var keys = Path.Combine(root, "keys"); Directory.CreateDirectory(keys);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(keys, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var rsa = RSA.Create(2048);
        using var certificate = new CertificateRequest("CN=hosting-fixture", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var certificatePath = Path.Combine(root, "wrapping.pfx");
        File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx, Password));
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Deployment:Enabled"] = "true", ["Deployment:ProxyAddress"] = "172.29.240.10", ["Deployment:PublicHost"] = "localhost",
            ["Deployment:KeyDirectory"] = keys, ["Deployment:CertificatePath"] = certificatePath,
            ["Deployment:CertificatePassword"] = Password, ["Deployment:InstanceName"] = "api-test"
        }).Build();
    }
    private static ServiceProvider Provider(IConfiguration configuration)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddDeploymentHosting(configuration);
        var provider = services.BuildServiceProvider();
        _ = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().ToArray();
        return provider;
    }

    [Fact]
    public void Separate_process_providers_share_encrypted_persisted_keys_and_application_identity()
    {
        var config = Configuration();
        using var first = Provider(config);
        var protectedValue = first.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture-cookie-purpose").Protect("fixture-session");
        using var second = Provider(config);
        Assert.Equal("fixture-session", second.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture-cookie-purpose").Unprotect(protectedValue));
        var key = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(root, "keys"), "key-*.xml")));
        Assert.Contains("encryptedSecret", key, StringComparison.Ordinal);
        Assert.Contains("EncryptedData", key, StringComparison.Ordinal);
        Assert.DoesNotContain("<masterKey", key, StringComparison.Ordinal);
        config["Deployment:ApplicationName"] = "AnotherApplication";
        using var isolated = Provider(config);
        Assert.Throws<CryptographicException>(() => isolated.GetRequiredService<IDataProtectionProvider>().CreateProtector("fixture-cookie-purpose").Unprotect(protectedValue));
    }

    [Theory]
    [InlineData("172.29.240.10", true)]
    [InlineData("::ffff:172.29.240.10", true)]
    [InlineData("172.29.240.11", false)]
    public async Task Only_explicit_proxy_can_forward_scheme_host_and_client_address(string remote, bool trusted)
    {
        using var provider = Provider(Configuration());
        var context = new DefaultHttpContext(); context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        context.Request.Scheme = "http"; context.Request.Host = new HostString("internal-api");
        context.Request.Headers["X-Forwarded-For"] = "192.0.2.15";
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-Host"] = "localhost:8443";
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance,
            provider.GetRequiredService<IOptions<ForwardedHeadersOptions>>());
        await middleware.Invoke(context);
        Assert.Equal(trusted ? "https" : "http", context.Request.Scheme);
        Assert.Equal(trusted ? "localhost:8443" : "internal-api", context.Request.Host.Value);
        Assert.Equal(IPAddress.Parse(trusted ? "192.0.2.15" : remote), context.Connection.RemoteIpAddress);
    }

    [Fact]
    public void Container_transport_exceptions_are_explicit_and_narrow()
    {
        Assert.Throws<InvalidOperationException>(() => new DevelopmentSmtpOptions { Host = "mailpit" }.Validate());
        new DevelopmentSmtpOptions { Host = "mailpit", AllowContainerHost = true }.Validate();
        Assert.Throws<InvalidOperationException>(() => new DevelopmentSmtpOptions { Host = "external.invalid", AllowContainerHost = true }.Validate());
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Telemetry:Enabled"] = "true", ["Telemetry:OtlpEnabled"] = "true", ["Telemetry:Endpoint"] = "http://telemetry:4318/" }).Build();
        Assert.Throws<InvalidOperationException>(() => TelemetrySettings.Read(config));
        config["Telemetry:AllowContainerHost"] = "true"; Assert.True(TelemetrySettings.Read(config).OtlpEnabled);
        config["Telemetry:Endpoint"] = "http://external.invalid:4318/";
        Assert.Throws<InvalidOperationException>(() => TelemetrySettings.Read(config));
    }

    [Fact]
    public void Deployment_rejects_publicly_readable_key_directory_on_Linux()
    {
        if (!OperatingSystem.IsLinux()) return;
        var config = Configuration(); File.SetUnixFileMode(Path.Combine(root, "keys"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead);
        Assert.Throws<InvalidOperationException>(() => Provider(config));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}
