using System.Text.Json.Serialization;
using BPMN.Infrastructure;
using BPMN.Process;
using core_engine;

namespace StatefulWebApiEngine.StatefulWorkflowEngine;

public class ProcessInfo
{
    [JsonIgnore]
    public required ProcessDefinition ProcessDefinition { get; init; }
    public int Version { get; init; }
}