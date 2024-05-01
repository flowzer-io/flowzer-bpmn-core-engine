using System.Text;
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
        return Ok(engineState.Models.Select(m => m.Id));
    }

    /// <summary>
    /// Lädt ein Modell in den Speicher, um es für die Ausführung bereitzustellen
    /// </summary>
    /// <param name="base64EncodedXmlModel">XML-Daten des Modells, Base64 codiert</param>
    /// <returns></returns>
    [HttpPost]
    [Consumes("application/xml")]
    public async Task<IActionResult> LoadModel()
    {
        string xmlModel;
        using (var stream = new StreamReader(Request.Body))
        {
            xmlModel = await stream.ReadToEndAsync();
        }

        // var xmlModel = Encoding.UTF8.GetString(base64EncodedXmlModel);
        var model = ModelParser.ParseModel(xmlModel);
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
        
        return Ok();
    }
    
    
}