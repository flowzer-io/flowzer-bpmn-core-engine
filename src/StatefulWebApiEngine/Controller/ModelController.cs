using BPMN.Common;
using BPMN.Process;
using core_engine;
using Microsoft.AspNetCore.Mvc;
using StatefulWebApiEngine.StatefulWorkflowEngine;

namespace StatefulWebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class ModelController(EngineState engineState, IWebHostEnvironment env) : ControllerBase
{
    /// <summary>
    /// Gibt eine Liste geladener Modelle zurück
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    [HttpGet]
    public IActionResult Index()
    {
        return Ok(engineState.ProcessInfos
            .Select(m => m.ProcessDefinition.Process.DefinitionsId).Distinct());
    }

    /// <summary>
    /// Gibt eine Liste der Prozesse für ein Modell zurück
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    [HttpGet, Route("{id}")]
    public IActionResult GetProcesses(string id)
    {
        return Ok(new
        {
            DefinitionId = id,
            Processes = engineState.ProcessInfos
                .Where(p => p.ProcessDefinition.Process.DefinitionsId == id && p.ProcessDefinition.IsActive)
                .Select(p => new
                {
                    p.ProcessDefinition.Process.Id,
                    p.ProcessDefinition.Process.Name,
                    // p.ProcessDefinition.Process.StartFlowNodes
                })
        });
    }

    /// <summary>
    /// Lädt ein Modell in den Speicher, um es für die Ausführung bereitzustellen
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> LoadModel([FromHeader] string filename = "model1.bpmn")
    {
        var fileStream = new FileStream(filename, FileMode.Open);
        var fileStream2 = new FileStream(filename, FileMode.Open);
        var model = await ModelParser.ParseModel(fileStream);

        var created = false;
        var updated = false;
        
        var processes =
            model.RootElements.OfType<Process>().ToList();

        foreach (var process in processes)
        {
            var version = 1;

            var oldProcessInfo = engineState.ProcessInfos.SingleOrDefault(p =>
                p.ProcessDefinition.Process.Id == process.Id && p.ProcessDefinition.IsActive);
            
            if(process == oldProcessInfo?.ProcessDefinition.Process)
                continue;
            
            if (oldProcessInfo is not null)
            {
                version = oldProcessInfo.Version + 1;
                oldProcessInfo.ProcessDefinition.IsActive = false;
                updated = true;
                // ToDo: Hier noch das deaktivieren des Prozesses implementieren
            }
            else created = true;

            var processInfo = new ProcessInfo
            {
                ProcessDefinition = new ProcessDefinition
                {
                    Process = process,
                    DeployedAt = DateTime.Now,
                    IsActive = true
                },
                Version = version
            };

            engineState.ProcessInfos.Add(processInfo);

            // ToDo: Hier dann das aktivieren des Prozesses implementieren
            
            // engineState.ActiveMessages
            //     .AddRange( processEngine.GetActiveCatchMessages()
            //         .Select(m => new MessageSubscription(m, processEngine)));
        }

        if (!created && !updated)
            return Ok(env.IsDevelopment() ? processes : null);
        return CreatedAtAction(nameof(GetProcesses), new { model.Id },
            env.IsDevelopment() ? processes : null);
    }
    
    /// <summary>
    /// Gibt den Hash der Prozesse eines Models zurück
    /// </summary>
    [HttpPost, Route("hash")]
    public async Task<IActionResult> GetHash([FromHeader] string filename = "model1.bpmn")
    {
        var fileStream = new FileStream(filename, FileMode.Open);
        var model = await ModelParser.ParseModel(fileStream);

        var processes =
            model.RootElements.OfType<Process>().ToList();
        
        return Ok(processes.ToDictionary(p => p.Id, p => p.GetHashCode()));
    }
}