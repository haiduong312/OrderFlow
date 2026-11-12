using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using OrderFlow.Application.Modules.Notifications;

namespace OrderFlow.Api.Notifications;

[ApiController, Route("api/notifications"), Authorize]
[RequestSizeLimit(128 * 1024)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class NotificationsController(NotificationService service) : ControllerBase
{
    [HttpGet]
    public Task<NotificationPage> List([FromQuery] NotificationQuery query, CancellationToken token) => service.ListAsync(query, token);
    [HttpGet("unread-count")]
    public Task<UnreadNotificationCount> Unread(CancellationToken token) => service.UnreadAsync(token);
    [HttpGet("{id:guid}")]
    public Task<NotificationResponse> Get(Guid id, CancellationToken token) => service.GetAsync(id, token);
    [HttpPost("{id:guid}/read")]
    public Task<NotificationResponse> Read(Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MarkNotificationReadRequest? request, CancellationToken token) => service.ReadAsync(id, token);
}
