using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class MessageController(IStorageSystem storageSystem, BpmnLogic bpmnLogic) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        return Ok((await storageSystem.SubscriptionStorage.GetAllMessageSubscriptions()).Select(m => m.Message));
    }

    [HttpPost]
    public async Task<IActionResult> HandleMessage([FromBody] Message message)
    {

        try
        {
            await bpmnLogic.HandleMessage(message);
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
        var response =
            $"Message \" {message.Name} \" with correlation Key \" {message.CorrelationKey} \" was sent to the process instance.";

        return Ok(response);
    }
}