using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderFlow.Application.Common.Errors;
using OrderFlow.Application.Common.Time;
using OrderFlow.Application.Modules.Identity;
using OrderFlow.Application.Persistence;

namespace OrderFlow.Api.Auth;

public sealed class AuthSessionService(UserManager<ApplicationUser> users, RoleManager<IdentityRole<Guid>> roles,
    SignInManager<ApplicationUser> signIn, OrderFlowDbContext database, TimeProvider clock)
{
    public async Task<UserResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await roles.RoleExistsAsync(IdentityPolicy.Customer))
        {
            throw new InvalidOperationException("Provision the Customer role before accepting registration.");
        }

        var email = request.Email.Trim();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            CreatedAt = BusinessTime.GetUtcNow(clock)
        };
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await users.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                throw RegistrationFailure(result);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var assigned = await users.AddToRoleAsync(user, IdentityPolicy.Customer);
            if (!assigned.Succeeded)
            {
                throw new InvalidOperationException("Customer role assignment failed; registration was rolled back.");
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "UserNameIndex" })
        {
            await transaction.RollbackAsync(cancellationToken);
            throw RegistrationFailure(IdentityResult.Failed(new IdentityError { Code = "DuplicateUserName" }));
        }

        return new UserResponse(user.Id, user.Email!, [IdentityPolicy.Customer]);
    }

    public async Task<UserResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalized = users.NormalizeName(request.Email.Trim());
        var id = await database.Users.AsNoTracking().Where(user => user.NormalizedUserName == normalized)
            .Select(user => (Guid?)user.Id).SingleOrDefaultAsync(cancellationToken);
        if (id is null)
        {
            return null;
        }

        // Serialize Identity lockout updates across processes, not just within this API instance.
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var user = await database.Users.FromSqlInterpolated($"""
            SELECT * FROM identity."Users" WHERE "Id" = {id.Value} FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        await transaction.CommitAsync(cancellationToken);
        if (!result.Succeeded || user.TwoFactorEnabled)
        {
            return null;
        }

        // Ticket/role reads and cookie issuance happen after the database transaction is finished.
        cancellationToken.ThrowIfCancellationRequested();
        await signIn.SignInAsync(user, isPersistent: false);
        return await DescribeAsync(user, cancellationToken);
    }

    public async Task<UserResponse> DescribeAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var assigned = await users.GetRolesAsync(user);
        return new UserResponse(user.Id, user.Email!, assigned.Order(StringComparer.Ordinal).ToArray());
    }

    private static ApplicationFailureException RegistrationFailure(IdentityResult result)
    {
        var field = result.Errors.Any(error => error.Code.StartsWith("Password", StringComparison.Ordinal)) ? "password" : "email";
        var message = field == "password"
            ? "Password must have 12–128 characters, at least four distinct characters, uppercase, lowercase, a digit and a symbol."
            : "An account could not be registered with these details.";
        return new ApplicationFailureException(ApplicationError.ValidationFailed,
            new Dictionary<string, string[]> { [field] = [message] });
    }
}
