using BPMN.Flowzer.Events;
using BPMN.Process;
using core_engine;
using Model;
using StorageSystem;

namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class ModelController(IWebHostEnvironment env, IStorageSystem storageSystem) : ControllerBase
{
    /// <summary>
    /// Gibt eine Liste geladener Modelle zurück
    /// </summary>
    /// <returns></returns>
    /// <exception cref="NotImplementedException"></exception>
    [HttpGet]
    public IActionResult Index()
    {
        return Ok(storageSystem.ProcessStorage.GetAllProcessDefinitionIds());
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
            Processes = storageSystem.ProcessStorage.GetAllProcessesInfos()
                .Where(p => p.Process.DefinitionsId == id && p.IsActive)
                .Select(p => new
                {
                    p.Process.Id,
                    p.Process.Name,
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
        var model = await ModelParser.ParseModel(fileStream);

        var created = false;
        var updated = false;

        var processes =
            model.RootElements.OfType<Process>().ToList();

        foreach (var process in processes)
        {
            var version = 1;

            var oldProcessInfo = storageSystem.ProcessStorage.GetAllProcessesInfos()
                .SingleOrDefault(p =>
                    p.Process.Id == process.Id && p.IsActive);

            if (process == oldProcessInfo?.Process)
                continue;

            if (oldProcessInfo is not null)
            {
                version = oldProcessInfo.Version + 1;
                storageSystem.ProcessStorage.DeactivateProcessInfo(oldProcessInfo.Process.Id);
                updated = true;
                storageSystem.MessageSubscriptionStorage.RemoveProcessMessageSubscriptions(oldProcessInfo.Process.Id);
                // ToDo: Hier noch das deaktivieren des Prozesses implementieren
            }
            else created = true;

            var processInfo = new ProcessInfo
            {
                Process = process,
                Version = version,
                DeployedAt = DateTime.Now,
                IsActive = true
            };

            storageSystem.ProcessStorage.AddProcessInfo(processInfo);
            foreach (var flowzerMessageStartEvent in
                     processInfo.Process.StartFlowNodes.OfType<FlowzerMessageStartEvent>())
            {
                var messageDefinition = flowzerMessageStartEvent.MessageDefinition;
                var messageSubscription =
                    new MessageSubscription(messageDefinition with { FlowzerCorrelationKey = null }, process);
                storageSystem.MessageSubscriptionStorage.AddMessageSubscription(messageSubscription);
                // ToDo: Hier noch überprüfen, ob bereits eine Message mit dem Namen vorhanden ist und ggf. direkt die Instance starten
            }
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