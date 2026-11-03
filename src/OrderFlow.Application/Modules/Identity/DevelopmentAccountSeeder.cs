using Microsoft.AspNetCore.Identity;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Application.Modules.Identity;

public static class DevelopmentAccountSeeder
{
    public static async Task<bool> SeedAsync(string environmentName, DevelopmentAccountOptions options,
        UserManager<ApplicationUser> users, OrderFlowDbContext database, TimeProvider clock, CancellationToken cancellationToken)
    {
        // Validate again here so callers cannot bypass the command's environment/opt-in guard.
        options.Validate(environmentName);
        await DevelopmentSeeder.SeedRolesAsync(database, cancellationToken);
        var email = options.Email!.Trim();
        cancellationToken.ThrowIfCancellationRequested();
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            var roles = await users.GetRolesAsync(existing);
            if (roles.Count != 1 || roles[0] != options.Role)
            {
                throw new InvalidOperationException("Existing seed identity has different roles; no account was changed or promoted.");
            }

            return false; // Existing credentials/roles are deliberately not reset.
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), Email = email, UserName = email, CreatedAt = BusinessTime.GetUtcNow(clock)
        };
        cancellationToken.ThrowIfCancellationRequested();
        if (!(await users.CreateAsync(user, options.Password!)).Succeeded)
        {
            throw new InvalidOperationException("Development seed account creation failed Identity validation; no account was committed.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!(await users.AddToRoleAsync(user, options.Role)).Succeeded)
        {
            throw new InvalidOperationException("Development seed role assignment failed; account creation was rolled back.");
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
