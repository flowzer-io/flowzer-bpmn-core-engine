using core_engine;
using Microsoft.AspNetCore.Mvc;
using StatefulWebApiEngine.StatefulWorkflowEngine;

namespace StatefulWebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class Model(EngineState engineState) : ControllerBase
{
    /// <summary>
    /// Gibt eine Liste geladener Modelle zurück
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    [HttpGet]
    public IActionResult Index()
    {
        throw new NotImplementedException();
    }
    
    /// <summary>
    /// Lädt ein Modell in den Speicher, um es für die Ausführung bereitzustellen
    /// </summary>
    /// <param name="xmlModel">XML-Daten des Modells</param>
    /// <returns></returns>
    [HttpPost]
    public async Task<IActionResult> LoadModel(string xmlModel)
    {
        var model = await ModelParser.ParseModel(xmlModel);
        model.Version = 1;
        var alreadyLoadedModel = engineState.Models.SingleOrDefault(m => m.Id == model.Id);
        if (alreadyLoadedModel is not null)
        {
            foreach (var processEngine in engineState.ProcessEngines.Where(pe => pe.Process.Model == alreadyLoadedModel))
            {
                processEngine.IsActive = false;
                engineState.ActiveMessages.RemoveAll(m => m.CatchHandler == processEngine);
            }

            model.Version = alreadyLoadedModel.Version + 1;
        }
        engineState.Models.Add(model);
        var processEngines = 
            model.Processes
            .Select(modelProcess => new ProcessEngine { Process = modelProcess });
        foreach (var processEngine in processEngines)
        {
            engineState.ProcessEngines.Add(processEngine);
            engineState.ActiveMessages
                .AddRange( processEngine.GetActiveCatchMessages()
                    .Select(m => new MessageSubscription(m, processEngine)));
        }
        
        return new OkResult();
    }
    
    
}