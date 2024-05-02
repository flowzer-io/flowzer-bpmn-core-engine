using BPMN_Model.Common;

namespace StatefulWebApiEngine.StatefulWorkflowEngine;

public class DefinitionsInfo
{
    public required Definitions Definitions { get; init; }
    public int Version { get; set; }
}