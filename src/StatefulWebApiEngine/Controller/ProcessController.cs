using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;
using StatefulWebApiEngine.StatefulWorkflowEngine;

namespace StatefulWebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class ProcessController(EngineState engineState) : ControllerBase
{
    [HttpGet]
    public IActionResult Index()
    {
        return Ok(engineState.ProcessDefinitions
            .Where(definition => definition.IsActive)
            .Select(processDefinition => processDefinition.Process.Id));
    }

    [HttpPost, Route("{id}")]
    public IActionResult Start(string id)
    {
        var processDefinition =
            engineState.ProcessDefinitions.Single(p => p.Process.Id == id);

        var instance = processDefinition.StartProcess();

        return Ok(instance);
    }
}