using Microsoft.AspNetCore.Authorization;
using WebApiEngine.Ai;
using WebApiEngine.Auth;
using WebApiEngine.Middleware;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>Sichere, hostneutrale Verwaltung nicht geheimer KI-Verbindungsmetadaten.</summary>
[ApiController]
[Route("ai/connection")]
[Authorize(Policy = FlowzerPolicies.AiConnectionUse)]
public sealed class AiConnectionController(
    AiConnectionService service,
    IAuthorizationService authorizationService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiStatusResult<AiConnectionDto[]>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiStatusResult<AiConnectionDto[]>>> List()
    {
        var includeDisabled = await MayManage();
        return Ok(new ApiStatusResult<AiConnectionDto[]>(
            (await service.ListAsync(includeDisabled)).ToArray()));
    }

    [HttpGet("{connectionId:guid}")]
    [ProducesResponseType<ApiStatusResult<AiConnectionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<AiConnectionDto>>> Get(Guid connectionId) =>
        Ok(new ApiStatusResult<AiConnectionDto>(
            await service.GetAsync(connectionId, await MayManage())));

    [HttpPost]
    [Authorize(Policy = FlowzerPolicies.AiConnectionManage)]
    [ProducesResponseType<ApiStatusResult<AiConnectionDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<AiConnectionDto>>> Create(
        CreateAiConnectionRequestDto request)
    {
        var created = await service.CreateAsync(request);
        return CreatedAtAction(
            nameof(Get),
            new { connectionId = created.Id },
            new ApiStatusResult<AiConnectionDto>(created));
    }

    [HttpPut("{connectionId:guid}")]
    [Authorize(Policy = FlowzerPolicies.AiConnectionManage)]
    [ProducesResponseType<ApiStatusResult<AiConnectionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<AiConnectionDto>>> Update(
        Guid connectionId,
        UpdateAiConnectionRequestDto request) =>
        Ok(new ApiStatusResult<AiConnectionDto>(await service.UpdateAsync(connectionId, request)));

    [HttpPut("{connectionId:guid}/enabled")]
    [Authorize(Policy = FlowzerPolicies.AiConnectionManage)]
    [ProducesResponseType<ApiStatusResult<AiConnectionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<AiConnectionDto>>> SetEnabled(
        Guid connectionId,
        SetAiConnectionEnabledRequestDto request) =>
        Ok(new ApiStatusResult<AiConnectionDto>(await service.SetEnabledAsync(connectionId, request)));

    private async Task<bool> MayManage() =>
        (await authorizationService.AuthorizeAsync(User, FlowzerPolicies.AiConnectionManage)).Succeeded;
}
