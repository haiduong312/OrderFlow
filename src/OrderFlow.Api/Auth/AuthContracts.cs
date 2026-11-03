using System.ComponentModel.DataAnnotations;

namespace OrderFlow.Api.Auth;

public sealed class RegisterRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = "";

    [Required, StringLength(128, MinimumLength = 12)]
    public string Password { get; init; } = "";
}

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; init; } = "";

    [Required, StringLength(128)]
    public string Password { get; init; } = "";
}

public sealed record UserResponse(Guid Id, string Email, IReadOnlyList<string> Roles);
public sealed record AntiforgeryResponse(string RequestToken, string HeaderName);
