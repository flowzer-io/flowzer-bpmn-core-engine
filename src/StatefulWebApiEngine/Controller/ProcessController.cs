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
            engineState.ProcessInfos.SingleOrDefault(p => p.ProcessDefinition.Process.Id == id && p.ProcessDefinition.IsActive);

        if (processInfo is null) return NotFound();
        
        var instance = processInfo.ProcessDefinition.StartProcess();

        return Ok(instance);
    }
}