using Microsoft.AspNetCore.Mvc;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

[ApiController, Route("notifications")]
public sealed class NotificationController(UserTaskNotificationService notifications) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiStatusResult<NotificationDto[]>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<NotificationDto[]>>> Get(
        [FromQuery] DateTimeOffset? before = null,
        [FromQuery] int limit = 50,
        [FromQuery] bool unreadOnly = false)
    {
        if (limit is < 1 or > 100)
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid notification query",
                detail: "The limit must be between 1 and 100.");
        var result = await notifications.GetAsync(before, limit, unreadOnly);
        return Ok(new ApiStatusResult<NotificationDto[]>(result.ToArray()));
    }

    [HttpPost("{notificationId:guid}/read")]
    [ProducesResponseType<ApiStatusResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult>> MarkRead(Guid notificationId) =>
        await notifications.MarkReadAsync(notificationId)
            ? Ok(new ApiStatusResult { Successful = true })
            : NotFound(new ApiStatusResult("The notification was not found."));
}
