using BPMN.Infrastructure;

namespace StatefulWebApiEngine.StatefulWorkflowEngine;

public class DefinitionsInfo
{
    public required Definitions Definitions { get; init; }
    public int Version { get; set; }
    public bool IsDeployed { get; set; }
}