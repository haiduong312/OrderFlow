using OrderFlow.Application.Common.Authorization;

namespace OrderFlow.Api.Auth.Authorization;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId => accessor.HttpContext is { } context ? UserIdentity.Read(context.User)?.Id : null;
}
