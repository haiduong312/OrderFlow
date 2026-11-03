using System.Security.Claims;

namespace OrderFlow.Api.Auth.Authorization;

internal static class UserIdentity
{
    public static (Guid Id, ClaimsIdentity Identity)? Read(ClaimsPrincipal principal)
    {
        // V1 Identity cookies contain one authenticated identity and one user ID.
        // Ambiguous/malformed claims must not become an application identity.
        var identities = principal.Identities.Where(identity => identity.IsAuthenticated).Take(2).ToArray();
        if (identities.Length != 1)
        {
            return null;
        }

        var identity = identities[0];
        var identifiers = identity.FindAll(ClaimTypes.NameIdentifier).Take(2).ToArray();
        return identifiers.Length == 1 && Guid.TryParse(identifiers[0].Value, out var id) && id != Guid.Empty
            ? (id, identity)
            : null;
    }
}
