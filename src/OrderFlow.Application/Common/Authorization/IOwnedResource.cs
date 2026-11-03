namespace OrderFlow.Application.Common.Authorization;

// Implement on a server-loaded entity or ownership projection, never an input DTO.
public interface IOwnedResource
{
    Guid OwnerId { get; }
}
