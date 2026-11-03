using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Api.Auth.Authorization;
using OrderFlow.Application.Common.Authorization;
using OrderFlow.Application.Common.Errors;

namespace OrderFlow.IntegrationTests.Authorization;

// Representative data/controller exist only in the test assembly, not in the API.
public sealed record RepresentativeResource(Guid Id, Guid OwnerId, bool IsClosed = false) : IOwnedResource
{
    public void CheckCanClose()
    {
        if (IsClosed) throw new ApplicationFailureException(ApplicationError.StateConflict);
    }
}

public sealed class RepresentativeResourceStore
{
    private readonly ConcurrentDictionary<Guid, RepresentativeResource> resources = new();

    public RepresentativeResource Add(Guid ownerId, bool closed = false)
    {
        var resource = new RepresentativeResource(Guid.NewGuid(), ownerId, closed);
        resources[resource.Id] = resource;
        return resource;
    }

    public RepresentativeResource? Find(Guid id) => resources.GetValueOrDefault(id);

    // Models a later owner-constrained query. This is not SQL/transaction test evidence.
    public RepresentativeResource? FindOwned(Guid id, Guid? ownerId) =>
        Find(id) is { } resource && resource.OwnerId == ownerId ? resource : null;
}

[ApiController]
[Route("_tests/resource-authorization")]
public sealed class ResourceAuthorizationProbeController(RepresentativeResourceStore store,
    ICurrentUser currentUser, ResourceAccess access) : ControllerBase
{
    [HttpGet("customer/{id:guid}"), Authorize(Policy = AuthorizationPolicies.CustomerAccess)]
    public async Task<IActionResult> Customer(Guid id, CancellationToken cancellationToken)
    {
        var resource = store.FindOwned(id, currentUser.UserId);
        await access.RequireCustomerOwnershipAsync(resource, cancellationToken);
        return Ok(new { resource!.Id });
    }

    [HttpGet("admin/{id:guid}"), Authorize(Policy = AuthorizationPolicies.AdminAccess)]
    public async Task<IActionResult> Admin(Guid id, CancellationToken cancellationToken)
    {
        var resource = store.Find(id);
        await access.RequireAdminResourceAsync(resource, cancellationToken);
        return Ok(new { resource!.Id });
    }

    [HttpPost("admin/{id:guid}/check-close"), Authorize(Policy = AuthorizationPolicies.AdminAccess)]
    public async Task<IActionResult> CheckClose(Guid id, CancellationToken cancellationToken)
    {
        var resource = store.Find(id);
        await access.RequireAdminResourceAsync(resource, cancellationToken);
        resource!.CheckCanClose(); // Permission never bypasses a business state rule.
        return NoContent();
    }
}
