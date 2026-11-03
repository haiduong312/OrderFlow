using System.ComponentModel.DataAnnotations;

namespace OrderFlow.Application.Modules.Identity;

public sealed class DevelopmentAccountOptions
{
    public bool Enabled { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string Role { get; set; } = IdentityPolicy.Customer;

    public void Validate(string environmentName)
    {
        if (!string.Equals(environmentName, "Development", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Development account seeding is permitted only in Development.");
        }

        if (!Enabled)
        {
            throw new InvalidOperationException("Explicitly enable DevelopmentSeed:Account:Enabled before seeding an account.");
        }

        if (string.IsNullOrWhiteSpace(Email) || Email.Trim().Length > 256
            || !new EmailAddressAttribute().IsValid(Email.Trim())
            || string.IsNullOrWhiteSpace(Password) || Password.Length is < 12 or > 128
            || Role is not (IdentityPolicy.Admin or IdentityPolicy.Customer))
        {
            throw new InvalidOperationException("Configure a development seed email, password and Admin/Customer role privately.");
        }
    }
}
