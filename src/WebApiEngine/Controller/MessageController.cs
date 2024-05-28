using core_engine;
using Model;
using Newtonsoft.Json.Linq;

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
    public IActionResult HandleMessage([FromBody] Message message)
    {
        var messageSubscription = storageSystem.MessageSubscriptionStorage
                                      .GetMessageSubscription(message.Name, message.CorrelationKey).FirstOrDefault()
                                  ?? storageSystem.MessageSubscriptionStorage
                                      .GetMessageSubscription(message.Name).FirstOrDefault();

        if (messageSubscription is null)
        {
            return BadRequest(
                $"No process instance is waiting for a message with the name \"{message.Name}\" and correlation key \"{message.CorrelationKey}\".");
            
            // ToDo: Hier noch einen Message zwischenspeicher einbauen, der dann wiederrum bei neuen Prozessdefinitionen und Instanzen geprüft wird
        }
        
        if (messageSubscription.ProcessInstance != null)
        {
            storageSystem.InstanceStorage.AddInstance(new InstanceEngine(messageSubscription.ProcessInstance)
                .HandleMessage(message));
        }
        
        storageSystem.InstanceStorage.AddInstance(new ProcessEngine(messageSubscription.Process)
            .HandleMessage(message));

        var response =
            $"Message \" {message.Name} \" with correlation Key \" {message.CorrelationKey} \" was sent to the process instance.";
        // if (message.Variables is not null)
        // {
        //     
        //     response += "\r\n\r\n The following variables were attached to the message: \r\n";
        //     // response += $"\" {message.Variables.Value} \", ";
        //     var jObject = JObject.Parse(message.Variables.Value.ToString());
        //     foreach (var variable in jObject.Properties())
        //     {
        //         response += $"{variable.Name} with value {variable.Value},\r\n";
        //     }
        // }

        return Ok(response);
    }
}