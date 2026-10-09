using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApiEngine.Auth;
using WebApiEngine.Jobs;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>Read-only Initiatorzugang genau eines eigenen TT-Ticket-Service-Auftrags.</summary>
[ApiController, Route("job"), Authorize(Policy = FlowzerPolicies.Worker)]
public sealed class ServiceTaskInitiatorAccessController(ServiceTaskInitiatorAccessService service,
    ICurrentUserContextAccessor currentUser) : ControllerBase
{
    /// <summary>
    /// Liefert den aktuellen Zugang des gespeicherten Initiators. False ist ein bestätigter
    /// Entzug, 503 eine technische Unklarheit. Kein Jobabschluss, Retry oder Rechteersatz.
    /// </summary>
    [HttpPost("{jobId:guid}/initiator-access")]
    [ProducesResponseType<ApiStatusResult<ServiceTaskInitiatorAccessDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<ServiceTaskInitiatorAccessDto>>> Check(Guid jobId,
        [FromBody, Required] ServiceTaskInitiatorAccessRequestDto request, CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty || !ServiceTaskInitiatorAccessService.IsValidWorkerId(request.WorkerId))
            return Problem(statusCode: 400, title: "Invalid worker access request");
        var userId = currentUser.GetCurrentUser().RequireResolvedUserId("checking job initiator access");
        var outcome = await service.CheckAsync(jobId, userId, request.WorkerId, cancellationToken);
        return outcome.Status switch
        {
            ServiceTaskInitiatorAccessStatus.Ok => Ok(new ApiStatusResult<ServiceTaskInitiatorAccessDto>(outcome.Access!)),
            ServiceTaskInitiatorAccessStatus.NotFound => Problem(statusCode: 404, title: "Service task job not found"),
            ServiceTaskInitiatorAccessStatus.LeaseLost => Problem(statusCode: 409, title: "Worker lease is no longer current"),
            ServiceTaskInitiatorAccessStatus.InvalidContext => Problem(statusCode: 409, title: "Job execution context is no longer current"),
            _ => Problem(statusCode: 503, title: "Current initiator access is unavailable")
        };
    }
}
