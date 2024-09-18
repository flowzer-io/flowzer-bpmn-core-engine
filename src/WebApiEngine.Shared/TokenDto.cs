using System.Dynamic;
using System.Text.Json.Serialization;

namespace WebApiEngine.Shared;

public class TokenDto
{
    public Guid Id { get; set; }
    public FlowNodeStateDto State { get; set; }
    
    public string? CurrentFlowNodeId { get; set; }
    
    [JsonConverter(typeof(ExpandoObjectConverter))]
    public ExpandoObject? CurrentFlowElement { get; set; }
    
    [JsonConverter(typeof(ExpandoObjectConverter))]
    public ExpandoObject? Variables { get; set; }
    
    [JsonConverter(typeof(ExpandoObjectConverter))]
    public ExpandoObject? OutputData { get; set; }
    
    
    public Guid? PreviousTokenId { get; set; }
    public Guid? ParentTokenId { get; init; }
    
    
}