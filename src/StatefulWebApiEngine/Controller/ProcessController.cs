using Microsoft.AspNetCore.Mvc;
using StatefulWebApiEngine.StatefulWorkflowEngine;

namespace StatefulWebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class ProcessController(EngineState engineState) : ControllerBase
{
    [HttpGet]
    public IActionResult Index()
    {
        return Ok(engineState.ProcessInfos
            .Where(definition => definition.ProcessDefinition.IsActive)
            .Select(processInfo => processInfo.ProcessDefinition.Process.Id));
    }

    [HttpPost, Route("{id}")]
    public IActionResult Start(string id)
    {
        var processInfo =
            engineState.ProcessInfos.Single(p => p.ProcessDefinition.Process.Id == id);

        var instance = processInfo.ProcessDefinition.StartProcess();

        return Ok(instance);
    }
}