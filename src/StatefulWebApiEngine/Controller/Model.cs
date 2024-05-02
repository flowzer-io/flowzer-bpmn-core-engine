using System.Text;
using BPMN.Process;
using core_engine;
using Microsoft.AspNetCore.Mvc;
using StatefulWebApiEngine.StatefulWorkflowEngine;

namespace StatefulWebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class Model(EngineState engineState, IWebHostEnvironment env) : ControllerBase
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
        return Ok(engineState.Models.Single(m => m.Definitions.Id == id));
    }

    /// <summary>
    /// Lädt ein Modell in den Speicher, um es für die Ausführung bereitzustellen
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> LoadModel([FromHeader] string filename = "model1.bpmn")
    {
        var fileStream = new FileStream(filename, FileMode.Open);
        var model = await ModelParser.ParseModel(fileStream);
        var definitionsInfo = new DefinitionsInfo { Definitions = model, IsDeployed = true, Version = 1 };
        
        var alreadyLoadedDefinitionsInfo = 
            engineState.Models.SingleOrDefault(m => m.Definitions.Id == model.Id && m.IsDeployed);
        
        if (alreadyLoadedDefinitionsInfo is not null)
        {
        // ToDo: Hier den Hash vergleichen
        //     
        //     // foreach (var processEngine in engineState.ProcessDefinitions.Where(pe =>
        //     //              pe. == alreadyLoadedModel && pe.IsActive))
        //     // {
        //     //     processEngine.IsActive = false;
        //     //     engineState.ActiveMessages.RemoveAll(m => m.CatchHandler == processEngine);
        //     // }
        //
        //     model.Version = alreadyLoadedModel.Version + 1;
        //     engineState.Models.Remove(alreadyLoadedModel); // ToDo: Wirklich Removen?
        }
        
        engineState.Models.Add(definitionsInfo);
        var processes =
            model.RootElements.OfType<Process>()
                .Select(modelProcess => new ProcessDefinition { Process = modelProcess });
        foreach (var process in processes)
        {
            engineState.ProcessDefinitions.Add(process);
            // engineState.ActiveMessages
            //     .AddRange( processEngine.GetActiveCatchMessages()
            //         .Select(m => new MessageSubscription(m, processEngine)));
        }

        return CreatedAtAction(nameof(Get), new { Id = model.Id }, env.IsDevelopment() ? model : null);
    }
}