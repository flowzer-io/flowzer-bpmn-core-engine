using Model;
using Newtonsoft.Json.Linq;
using StorageSystem;

namespace WebApiEngine.Controller;

[ApiController, Route("[controller]")]
public class MessageController(IStorageSystem storageSystem) : ControllerBase
{
    [HttpGet]
    public IActionResult Index()
    {
        return Ok(storageSystem.MessageSubscriptionStorage.GetAllMessageSubscriptions().Select(m => m.Message));
    }
    
    [HttpPost]
    public IActionResult SendMessage([FromBody] Message message)
    {
        // var processInfo =
        //     storageSystem.ProcessStorage.GetAllProcessesInfos()
        //         .SingleOrDefault(p => p.Process.Id == id && 
        //                               p.IsActive);
        //
        // if (processInfo is null) return NotFound();
        //
        // var instanceEngine = new ProcessEngine(processInfo.Process).SendMessage(message);

        var response = $"Message \" {message.Name} \" with correlation Key \" {message.CorrelationKey} \" was sent to the process instance.";
        if (message.Variables is not null)
        {
            
            response += "\r\n\r\n The following variables were attached to the message: \r\n";
            // response += $"\" {message.Variables.Value} \", ";
            var jObject = JObject.Parse(message.Variables.Value.ToString());
            foreach (var variable in jObject.Properties())
            {
                response += $"{variable.Name} with value {variable.Value},\r\n";
            }
        }
        
        return Ok(response);
    }
}