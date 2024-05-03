using System.Text.Json.Serialization;
using BPMN.Infrastructure;

namespace StatefulWebApiEngine.StatefulWorkflowEngine;

public class DefinitionsInfo
{
    [JsonIgnore]
    public Definitions? Definitions { get; set; }
    public required string BpmnFileHash { get; init; }
    public required string Id { get; init; }
    public int Version { get; set; }
    public bool IsDeployed { get; set; }
}