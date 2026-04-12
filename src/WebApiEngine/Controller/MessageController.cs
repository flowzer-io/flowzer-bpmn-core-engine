using WebApiEngine.BusinessLogic;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class MessageController(IStorageSystem storageSystem,
    BpmnBusinessLogic bpmnBusinessLogic) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        return Ok((await storageSystem.SubscriptionStorage.GetAllMessageSubscriptions()).Select(m => m.Message));
    }

    [HttpPost]
    public async Task<ActionResult<ApiStatusResult<string>>> HandleMessage([FromBody] MessageDto messageDto)
    {
        try
        {
            var message = messageDto.ToModel();
            await bpmnBusinessLogic.HandleMessage(message);
        }
        catch (ArgumentException e)
        {
            return BadRequest(new ApiStatusResult<string>(errorMessage: e.Message));
        }
        catch (FileNotFoundException e)
        {
            return NotFound(new ApiStatusResult<string>(errorMessage: e.Message));
        }
        string correlationText = "";
        if (messageDto.CorrelationKey != null)
            correlationText = $" with correlation Key '{messageDto.CorrelationKey}'";
        var response =
            $"Message '{messageDto.Name}'{correlationText} was sent to the process instance.";

        return Ok(new ApiStatusResult<string>
        {
            Successful = true,
            Result = response
        });
    }
}
