using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.RateLimiting;
using WebApiEngine.FormEmbedding;
using WebApiEngine.Limits;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>Separater Startformular-Anzeigevertrag ohne Human-Task- oder Instanzattrappen.</summary>
[ApiController, Route("form-embed/start")]
public sealed class StartFormEmbedController(StartFormEmbedLinkService links) : ControllerBase
{
    /// <summary>Persönlichen Einmaleinstieg an eine konkrete angezeigte Version binden.</summary>
    [HttpPost("/definition/meta/{definitionId}/start-form-link")]
    [ProducesResponseType<ApiStatusResult<StartFormEmbedLinkDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<WebApiEngine.Middleware.ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<StartFormEmbedLinkDto>>> Issue(string definitionId,
        CreateFormEmbedLinkRequestDto request, [FromQuery, Required] Guid? expectedDefinitionId)
    {
        NoStore();
        if (expectedDefinitionId is null || expectedDefinitionId == Guid.Empty)
            return Problem(statusCode: 400, title: "Definition version required", detail: "A displayed definition version is required.");
        var link = await links.IssueAsync(definitionId, expectedDefinitionId.Value, request.HostOrigin, HttpContext.RequestAborted);
        return link is null ? Unavailable() : Ok(new ApiStatusResult<StartFormEmbedLinkDto>(link));
    }

    /// <summary>Secret einmalig und ausschließlich zur anonymen Formularanzeige einlösen.</summary>
    [HttpPost("redeem"), AllowAnonymous, EnableCors("FlowzerFormEmbedRead")]
    [EnableRateLimiting(FlowzerLimitsExtensions.FormEmbedReadPolicy), RequestSizeLimit(1024)]
    [ProducesResponseType<ApiStatusResult<StartFormEmbedSnapshotDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<StartFormEmbedSnapshotDto>>> Redeem(RedeemFormEmbedLinkRequestDto request)
    {
        NoStore();
        var snapshot = await links.RedeemAsync(request.Secret, HttpContext.RequestAborted);
        return snapshot is null ? Unavailable() : Ok(new ApiStatusResult<StartFormEmbedSnapshotDto>(snapshot));
    }

    private void NoStore()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
    private ObjectResult Unavailable() => Problem(statusCode: 404, title: "Embedded start form unavailable",
        detail: "The form link or workflow is not available.");
}
