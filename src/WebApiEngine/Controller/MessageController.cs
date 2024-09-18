using AutoMapper;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class MessageController(IStorageSystem storageSystem,
    BpmnLogic bpmnLogic,
    IMapper mapper
    ) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        return Ok((await storageSystem.SubscriptionStorage.GetAllMessageSubscriptions()).Select(m => m.Message));
    }

    [HttpPost]
    public async Task<IActionResult> HandleMessage([FromBody] MessageDto messageDto)
    {
        try
        {
            var message = mapper.Map<Message>(messageDto);
            await bpmnLogic.HandleMessage(message);
        }
        catch (Exception e)
        {
            return BadRequest(e.Message);
        }
        string correlationText = "";
        if (messageDto.CorrelationKey != null)
            correlationText = $" with correlation Key '{messageDto.CorrelationKey}'";
        var response =
            $"Message '{messageDto.Name}'{correlationText} was sent to the process instance.";

        return Ok(response);
    }
}