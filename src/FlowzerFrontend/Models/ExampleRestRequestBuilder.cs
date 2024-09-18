using System.Dynamic;
using Microsoft.AspNetCore.Components.Forms;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Models;

public class ExampleRestRequestBuilder
{
    public RestExampleRequest BuildMessageRequest(MessageDefinitionDto item, Guid? instanceId)
    {

        var dto = new MessageDto()
        {
            Name = item.Name,
            CorrelationKey = item.FlowzerCorrelationKey,
            Variables = new ExpandoObject(),
            InstanceId = instanceId
        };

        dto.Variables.TryAdd("Value1", "val1");
        dto.Variables.TryAdd("Value2", 123);
        
       var data = System.Text.Json.JsonSerializer.Serialize(dto, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        return new RestExampleRequest()
        {
            Url = "/message",
            Body = data
        };
    }
}