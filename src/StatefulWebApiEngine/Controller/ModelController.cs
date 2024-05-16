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
        return Ok(engineState.Models.Select(m => m.Definitions));
    }

    [HttpGet, Route("{id}")]
    public IActionResult Get(string id)
    {
        return Ok(engineState.Models.Single(m => m.Id == id).Definitions);
    }

    /// <summary>
    /// Lädt ein Modell in den Speicher, um es für die Ausführung bereitzustellen
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> LoadModel([FromHeader] string filename = "model1.bpmn")
    {
        var fileStream = new FileStream(filename, FileMode.Open);
        var model = await ModelParser.ParseModel(fileStream);
        var definitionsInfo = new DefinitionsInfo
        {
            Definitions = model, 
            IsDeployed = true, 
            Id = model.Id,
            Version = 1,
            BpmnFileHash = model.FlowzerFileHash
        };
        
        var alreadyDeployedDefinitionsInfo = 
            engineState.Models.SingleOrDefault(m => m.Id == model.Id && m.IsDeployed);
        
        if (alreadyDeployedDefinitionsInfo is not null)
        {
            if (alreadyDeployedDefinitionsInfo.BpmnFileHash == definitionsInfo.BpmnFileHash)
                return Ok();
            
            definitionsInfo.Version = alreadyDeployedDefinitionsInfo.Version + 1;
            alreadyDeployedDefinitionsInfo.IsDeployed = false;
        }
        
        engineState.Models.Add(definitionsInfo);
        var processes =
            model.RootElements.OfType<Process>()
                .Select(modelProcess => new ProcessDefinition { Process = modelProcess });
        foreach (var processDefinition in processes)
        {
            processDefinition.IsActive = true;
            foreach (var otherProcessDefinitions in engineState.ProcessDefinitions.Where(pd =>
                         pd.Process.Id == processDefinition.Process.Id && pd.IsActive))
            {
                otherProcessDefinitions.IsActive = false;
            }
            engineState.ProcessDefinitions.Add(processDefinition);
            
            // engineState.ActiveMessages
            //     .AddRange( processEngine.GetActiveCatchMessages()
            //         .Select(m => new MessageSubscription(m, processEngine)));
        }

        return CreatedAtAction(nameof(Get), new { Id = model.Id }, env.IsDevelopment() ? definitionsInfo.Definitions.RootElements.OfType<Process>() : null);
    }
}