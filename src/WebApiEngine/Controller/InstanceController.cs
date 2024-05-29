namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class InstanceController(IStorageSystem storageSystem) : ControllerBase
{
    [HttpGet]
    public IActionResult Index()
    {
        return Ok(storageSystem.InstanceStorage.GetAllInstances());
    }
}