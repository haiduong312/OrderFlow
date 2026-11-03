using OrderFlow.Application.Modules.Identity;
using Xunit;

namespace OrderFlow.UnitTests;

public sealed class DevelopmentAccountOptionsTests
{
    [Theory]
    [InlineData("Production", true, "Admin")]
    [InlineData("Staging", true, "Customer")]
    [InlineData("Development", false, "Admin")]
    [InlineData("Development", true, "SuperAdmin")]
    public void Seed_requires_development_explicit_enable_and_known_role(string environment, bool enabled, string role)
    {
        var options = Valid();
        options.Enabled = enabled;
        options.Role = role;
        Assert.Throws<InvalidOperationException>(() => options.Validate(environment));
    }

    [Fact]
    public void Seed_has_no_default_credentials_and_rejects_unusable_lengths()
    {
        Assert.Throws<InvalidOperationException>(() => new DevelopmentAccountOptions { Enabled = true }.Validate("Development"));
        var options = Valid();
        options.Email = "invalid";
        Assert.Throws<InvalidOperationException>(() => options.Validate("Development"));
        options = Valid();
        options.Password = new string('x', 129);
        Assert.Throws<InvalidOperationException>(() => options.Validate("Development"));
        options.Password = "short";
        Assert.Throws<InvalidOperationException>(() => options.Validate("Development"));
        Valid().Validate("Development");
    }

    private static DevelopmentAccountOptions Valid() => new()
    {
        Enabled = true, Email = "seed@example.invalid", Password = "Aa1!" + Guid.NewGuid().ToString("N")
    };
}
