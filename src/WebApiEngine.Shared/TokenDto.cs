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

    /// <summary>
    /// Zeitpunkt, zu dem das Token erzeugt wurde (UTC).
    /// Grundlage für die Verlaufsdarstellung in der Oberfläche.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Zeitpunkt des letzten Statuswechsels (UTC).
    /// </summary>
    public DateTime LastStateChangeTime { get; set; }
}