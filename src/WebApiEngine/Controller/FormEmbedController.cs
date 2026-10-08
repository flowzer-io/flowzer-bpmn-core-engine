using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using WebApiEngine.FormEmbedding;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>Isolierte Read-only-Grenze; das Secret wird niemals ein API-Bearer-Token.</summary>
[ApiController, Route("form-embed")]
public sealed class FormEmbedController(FormEmbedLinkService links) : ControllerBase
{
    /// <summary>Persönlichen Einmaleinstieg vom authentifizierten Backend aus anfordern.</summary>
    [HttpPost("/usertask/{taskId:guid}/form-link")]
    [ProducesResponseType<ApiStatusResult<FormEmbedLinkDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiStatusResult<FormEmbedLinkDto>>> Issue(Guid taskId, CreateFormEmbedLinkRequestDto request)
    {
        NoStore();
        var link = await links.IssueAsync(taskId, request.HostOrigin, HttpContext.RequestAborted);
        return link is null ? Unavailable() : Ok(new ApiStatusResult<FormEmbedLinkDto>(link));
    }

    /// <summary>Einmalig den Formularsnapshot lesen, ohne Cookie oder zweite Anmeldung.</summary>
    [HttpPost("redeem")]
    [AllowAnonymous]
    [EnableCors("FlowzerFormEmbedRead")]
    [RequestSizeLimit(1024)]
    [ProducesResponseType<ApiStatusResult<FormEmbedSnapshotDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiStatusResult<FormEmbedSnapshotDto>>> Redeem(RedeemFormEmbedLinkRequestDto request)
    {
        NoStore();
        var snapshot = await links.RedeemAsync(request.Secret, HttpContext.RequestAborted);
        return snapshot is null ? Unavailable() : Ok(new ApiStatusResult<FormEmbedSnapshotDto>(snapshot));
    }

    private void NoStore()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private ObjectResult Unavailable() => Problem(statusCode: StatusCodes.Status404NotFound,
        title: "Embedded form unavailable", detail: "The form link or task is not available.");
}
