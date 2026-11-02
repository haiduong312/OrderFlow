using Microsoft.AspNetCore.Identity;

namespace OrderFlow.Application.Modules.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAt { get; set; }
}
