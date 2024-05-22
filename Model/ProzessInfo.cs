using System.Text.Json.Serialization;
using BPMN.Process;

namespace Model;

public class ProcessInfo
{
    [JsonIgnore]
    public required Process Process { get; init; }
    public int Version { get; init; }
    public DateTime DeployedAt { get; init; }
    public bool IsActive { get; set; }
}