using core_engine;

namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class ProcessController(IStorageSystem storageSystem) : ControllerBase
{
    [HttpGet]
    public IActionResult Index()
    {
        return Ok(storageSystem.ProcessStorage.GetAllProcessesInfos()
            .Where(definition => definition.IsActive)
            .Select(processInfo => processInfo.Process.Id));
    }

    [HttpPost, Route("{id}")]
    public IActionResult Start(string id)
    {
        var processInfo =
            storageSystem.ProcessStorage.GetAllProcessesInfos()
                .SingleOrDefault(p => p.Process.Id == id && 
                                      p.IsActive);

        if (processInfo is null) return NotFound();
        
        var instanceEngine = new ProcessEngine(processInfo.Process).StartProcess();
        storageSystem.InstanceStorage.AddInstance(instanceEngine.Instance);

        return Ok(instanceEngine.Instance);
    }
}