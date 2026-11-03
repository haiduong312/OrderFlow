namespace OrderFlow.Application.Common.Authorization;

// Application services must not read HttpContext, claims or client-supplied user IDs.
public interface ICurrentUser
{
    Guid? UserId { get; }
}
