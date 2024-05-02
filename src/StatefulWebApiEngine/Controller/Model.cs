using System.Text;
using BPMN_Model.Common;
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
        var alreadyLoadedModel = engineState.Models.SingleOrDefault(m => m.Definitions.Id == model.Id);

        // if (alreadyLoadedModel is not null)
        // {
        //     if (model.GetHashCode() == alreadyLoadedModel.GetHashCode())
        //         return Ok();
        //
        //     foreach (var processEngine in engineState.ProcessEngines.Where(pe =>
        //                  pe.Process.Definitions == alreadyLoadedModel && pe.IsActive))
        //     {
        //         processEngine.IsActive = false;
        //         engineState.ActiveMessages.RemoveAll(m => m.CatchHandler == processEngine);
        //     }
        //
        //     model.Version = alreadyLoadedModel.Version + 1;
        //     engineState.Models.Remove(alreadyLoadedModel); // ToDo: Wirklich Removen?
        // }
        //
        // engineState.Models.Add(model);
        // var processEngines =
        //     model.Processes
        //         .Select(modelProcess => new ProcessEngine { Process = modelProcess });
        // foreach (var processEngine in processEngines)
        // {
        //     engineState.ProcessEngines.Add(processEngine);
        //     // engineState.ActiveMessages
        //     //     .AddRange( processEngine.GetActiveCatchMessages()
        //     //         .Select(m => new MessageSubscription(m, processEngine)));
        // }

        return CreatedAtAction(nameof(Get), new { Id = model.Id }, env.IsDevelopment() ? model : null);
    }
}