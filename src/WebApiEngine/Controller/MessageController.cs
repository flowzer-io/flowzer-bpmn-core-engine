using core_engine;
using core_engine.InstanceEngine;
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

        if (messageSubscription.ProcessInstanceId != null)
        {
            var instanceEngine =
                new InstanceEngine(
                    storageSystem.InstanceStorage.GetInstanceById(messageSubscription.ProcessInstanceId.Value));
            instanceEngine.HandleMessage(message);
            // TODO: Hier muss noch sowas wie SafeChanges eingebaut werden und die Subscriptions neu gesetzt werden etc.
        }
        else
            storageSystem.InstanceStorage.AddInstance(
                new ProcessEngine(storageSystem.ProcessStorage.GetActiveProcessInfo(messageSubscription.ProcessId)
                        .Process)
                    .HandleMessage(message).Instance);

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