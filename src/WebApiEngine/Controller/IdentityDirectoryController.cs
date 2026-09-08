using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.IdentityDirectory;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>
/// Betriebsvertrag fuer den lesenden Keycloak-Abgleich. Der Bestand selbst wird erst ueber
/// kontextgebundene Suchendpunkte der Auswahlfelder freigegeben; hier sind nur Aggregate sichtbar.
/// </summary>
[ApiController, Route("identity-directory")]
[Authorize(Policy = FlowzerPolicies.IdentityDirectoryOperator)]
public sealed class IdentityDirectoryController(
    IIdentityDirectoryStorage storage,
    IdentityDirectoryBackgroundService backgroundService,
    IOptions<KeycloakDirectoryOptions> options,
    ILogger<IdentityDirectoryController> logger) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType<ApiStatusResult<IdentityDirectoryStatusDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<IdentityDirectoryStatusDto>>> GetStatus()
    {
        if (!options.Value.Enabled)
        {
            return Ok(new ApiStatusResult<IdentityDirectoryStatusDto>(CreateStatus(enabled: false, status: null)));
        }

        try
        {
            return Ok(new ApiStatusResult<IdentityDirectoryStatusDto>(
                CreateStatus(enabled: true, await storage.GetSyncStatus())));
        }
        catch (Exception)
        {
            logger.LogError("Identity-directory status could not be read.");
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Identity directory unavailable",
                detail: "The identity-directory status is currently unavailable.");
        }
    }

    [HttpPost("sync")]
    [ProducesResponseType<ApiStatusResult<IdentityDirectoryStatusDto>>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public ActionResult<ApiStatusResult<IdentityDirectoryStatusDto>> Synchronize()
    {
        var outcome = backgroundService.RequestSynchronization();
        return outcome switch
        {
            IdentityDirectoryBackgroundService.ManualTriggerOutcome.Disabled => Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Identity directory disabled",
                detail: "Identity-directory synchronization is not enabled for this installation."),
            IdentityDirectoryBackgroundService.ManualTriggerOutcome.Busy => Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Identity directory synchronization already running",
                detail: "A directory synchronization already owns the current lease."),
            _ => Accepted(new ApiStatusResult<IdentityDirectoryStatusDto>(
                CreateStatus(enabled: true, status: null, stateOverride: "queued")))
        };
    }

    private static IdentityDirectoryStatusDto CreateStatus(
        bool enabled,
        DirectorySyncStatus? status,
        string? stateOverride = null) => new()
    {
        Enabled = enabled,
        State = stateOverride ?? (!enabled ? "disabled" : status?.State.ToString().ToLowerInvariant() ?? "pending"),
        ActiveGenerationId = status?.ActiveGenerationId,
        RunningGenerationId = status?.RunningGenerationId,
        AttemptedAtUtc = status?.AttemptedAtUtc,
        LeaseExpiresAtUtc = status?.LeaseExpiresAtUtc,
        SucceededAtUtc = status?.SucceededAtUtc,
        FailedAtUtc = status?.FailedAtUtc,
        UserCount = status?.UserCount ?? 0,
        GroupCount = status?.GroupCount ?? 0,
        MembershipCount = status?.MembershipCount ?? 0,
        ErrorCode = status?.ErrorCode,
        ErrorMessage = status?.ErrorMessage
    };
}
