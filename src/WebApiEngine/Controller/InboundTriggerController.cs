using Microsoft.AspNetCore.Authorization;
using WebApiEngine.Auth;
using WebApiEngine.InboundTriggers;
using WebApiEngine.Middleware;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>
/// Verwaltung der von außen aufrufbaren Auslöser. Sie gehört dem Betrieb: Wer hier anlegt,
/// öffnet eine Adresse, die ohne Anmeldung Workflows startet.
///
/// Das Geheimnis steht ausschließlich in der Antwort auf <c>POST</c> und auf
/// <c>POST {id}/rotate-secret</c>. <c>GET</c> liefert es nie — ein Lesen-und-Anzeigen wäre
/// sonst ein zweiter Weg, an ein einmal vergebenes Geheimnis zu kommen.
/// </summary>
[ApiController]
[Route("inbound-trigger")]
[Authorize(Policy = FlowzerPolicies.Operator)]
public sealed class InboundTriggerController(
    InboundTriggerService service,
    ICurrentUserContextAccessor currentUserContextAccessor) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiStatusResult<InboundTriggerDto[]>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiStatusResult<InboundTriggerDto[]>>> Index() =>
        Ok(new ApiStatusResult<InboundTriggerDto[]>((await service.ListAsync()).ToArray()));

    /// <summary>
    /// Legt einen Auslöser an. Die Antwort enthält Schlüssel und Geheimnis — das Geheimnis genau
    /// dieses eine Mal.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<ApiStatusResult<InboundTriggerSecretDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<InboundTriggerSecretDto>>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiStatusResult<InboundTriggerSecretDto>>> Create(
        [FromBody] CreateInboundTriggerRequestDto request)
    {
        var userId = currentUserContextAccessor.GetCurrentUser()
            .RequireResolvedUserId("creating inbound triggers");
        try
        {
            return Ok(new ApiStatusResult<InboundTriggerSecretDto>(await service.CreateAsync(request, userId)));
        }
        catch (InboundTriggerValidationException exception)
        {
            return BadRequest(new ApiStatusResult<InboundTriggerSecretDto>(exception.Message));
        }
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ApiStatusResult<InboundTriggerDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<InboundTriggerDto>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiStatusResult<InboundTriggerDto>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiStatusResult<InboundTriggerDto>>> Update(
        [FromRoute] Guid id,
        [FromBody] UpdateInboundTriggerRequestDto request)
    {
        try
        {
            var updated = await service.UpdateAsync(id, request);
            return updated is null
                ? NotFound(new ApiStatusResult<InboundTriggerDto>($"Es gibt keinen Auslöser mit der Kennung {id}."))
                : Ok(new ApiStatusResult<InboundTriggerDto>(updated));
        }
        catch (InboundTriggerValidationException exception)
        {
            return BadRequest(new ApiStatusResult<InboundTriggerDto>(exception.Message));
        }
    }

    /// <summary>
    /// Gibt ein neues Geheimnis aus und zeigt es einmal. Das alte gilt sofort nicht mehr.
    ///
    /// Bewusst POST und nicht GET: Der Aufruf ändert etwas. Als GET genügte ein Link oder ein
    /// Vorablade-Versuch des Browsers, um eine laufende Anbindung abzuschalten.
    /// </summary>
    [HttpPost("{id:guid}/rotate-secret")]
    [ProducesResponseType<ApiStatusResult<InboundTriggerSecretDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<InboundTriggerSecretDto>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiStatusResult<InboundTriggerSecretDto>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiStatusResult<InboundTriggerSecretDto>>> RotateSecret([FromRoute] Guid id)
    {
        try
        {
            var rotated = await service.RotateSecretAsync(id);
            return rotated is null
                ? NotFound(new ApiStatusResult<InboundTriggerSecretDto>($"Es gibt keinen Auslöser mit der Kennung {id}."))
                : Ok(new ApiStatusResult<InboundTriggerSecretDto>(rotated));
        }
        catch (InboundTriggerValidationException exception)
        {
            return BadRequest(new ApiStatusResult<InboundTriggerSecretDto>(exception.Message));
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType<ApiStatusResult<string>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<string>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiStatusResult<string>>> Delete([FromRoute] Guid id) =>
        await service.DeleteAsync(id)
            ? Ok(new ApiStatusResult<string> { Successful = true, Result = id.ToString() })
            : NotFound(new ApiStatusResult<string>($"Es gibt keinen Auslöser mit der Kennung {id}."));
}
