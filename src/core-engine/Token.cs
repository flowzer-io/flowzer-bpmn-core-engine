using BPMN.Common;

namespace core_engine;

public class Token
{
    public required FlowNode CurrentFlowNode { get; set; }
    public FlowNode? PreviousFlowNode { get; set; }
    public SequenceFlow? LastSequenceFlow { get; set; }
}