using Microsoft.AspNetCore.Authorization;
using WebApiEngine.Ai;
using WebApiEngine.Auth;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

/// <summary>Nur lesbarer Katalog fest installierter, typisierter KI-Werkzeuge.</summary>
[ApiController]
[Route("ai/tool")]
[Authorize(Policy = FlowzerPolicies.AiConnectionUse)]
public sealed class AiToolController(AiToolRegistry registry) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiStatusResult<AiToolDto[]>>(StatusCodes.Status200OK)]
    public ActionResult<ApiStatusResult<AiToolDto[]>> List() =>
        Ok(new ApiStatusResult<AiToolDto[]>(registry.List()
            .Select(tool => new AiToolDto
            {
                Id = tool.Definition.Id,
                Version = tool.Definition.Version,
                Name = tool.Definition.Name,
                Description = tool.Definition.Description,
                InputSchema = tool.Definition.InputSchema,
                OutputSchema = tool.Definition.OutputSchema,
                SideEffect = (AiToolSideEffectDto)tool.Definition.SideEffect,
                AllowsPreApproval = tool.Definition.AllowsPreApproval,
                ContractHash = tool.ContractHash
            })
            .ToArray()));
}
