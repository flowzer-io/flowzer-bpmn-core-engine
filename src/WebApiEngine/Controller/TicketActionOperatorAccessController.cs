using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WebApiEngine.Auth;
using WebApiEngine.Jobs;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>
/// Minimaler persönlicher Read-only-Betriebsnachweis für die separate administrative TT-Wiederfreigabe.
/// Kein Link-Secret-Zugriff, kein Workerrecht und keine Änderung von Instanzen/Jobs/Tickets.
/// </summary>
[ApiController, Route("instance"), Authorize(Policy = FlowzerPolicies.Operator)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TicketActionOperatorAccessController(TicketActionOperatorAccessService service,
    ICurrentUserContextAccessor currentUser) : ControllerBase
{
    /// <summary>TT muss Actor/azp/Instanzbindung und seine frische Adminberechtigung selbst prüfen; das Ergebnis ist kein Freigabetoken.</summary>
    [HttpGet("{instanceId:guid}/ticket-action-operator-access")]
    [ProducesResponseType<ApiStatusResult<TicketActionOperatorAccessDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<TicketActionOperatorAccessDto>>> Check(Guid instanceId, CancellationToken ct)
    {
        if (instanceId == Guid.Empty) return Problem(statusCode: 400, title: "Invalid operator access request");
        var result = await service.CheckAsync(instanceId, currentUser.GetCurrentUser(), ct);
        return result.Status switch
        {
            TicketActionOperatorAccessStatus.Ok => Ok(new ApiStatusResult<TicketActionOperatorAccessDto>(result.Access!)),
            TicketActionOperatorAccessStatus.NotFound => Problem(statusCode: 404, title: "Process instance not found"),
            TicketActionOperatorAccessStatus.Denied => Denied(),
            TicketActionOperatorAccessStatus.InvalidContext => Problem(statusCode: 409, title: "Operator execution context is no longer current"),
            _ => Problem(statusCode: 503, title: "Current operator access is unavailable"),
        };
    }

    private ObjectResult Denied()
    {
        Response.Headers[FlowzerPolicies.AccessDeniedHeader] = FlowzerPolicies.DeniedCapability;
        return Problem(statusCode: 403, title: "Current operator access denied");
    }
}
