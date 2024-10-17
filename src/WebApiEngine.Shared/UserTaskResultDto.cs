using System.Dynamic;
using System.Text.Json.Serialization;

namespace WebApiEngine.Shared;

public class UserTaskResultDto
{
    public required string FlowNodeId { get; set; }
    public Guid TokenId { get; set; }
    public Guid? ProcessInstanceId { get; set; }
    
    [JsonConverter(typeof(ExpandoObjectConverter))]
    public ExpandoObject? Data { get; set; }
}