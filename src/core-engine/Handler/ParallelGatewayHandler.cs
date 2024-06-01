using BPMN.Common;
using core_engine.Handler;
using Model;

namespace core_engine;

internal class ParallelGatewayHandler : IFlowNodeHandler
{
    public void Execute(ProcessInstance processInstance, Token token)
    {
        var sequenceFlowIds = processInstance.ProcessModel.FlowElements.OfType<SequenceFlow>()
            .Where(x => x.TargetRef.Id == token.CurrentFlowNode.Id)
            .Select(sf => sf.Id)
            .ToList();

        var actualActiveTokens = processInstance.Tokens
            .Where(t => t.CurrentFlowNode.Id == token.CurrentFlowNode.Id && t.State == FlowNodeState.Active)
            .ToList();

        if (sequenceFlowIds.Count > actualActiveTokens.Count) return;
        
        var filteredSequenceFlowIds = 
            sequenceFlowIds.Where(fn => actualActiveTokens.All(t => t.LastSequenceFlow?.Id != fn));

        if (filteredSequenceFlowIds.Any())
            return;

        var first = true;
        foreach (var sequenceFlowId in sequenceFlowIds)
        {
            actualActiveTokens
                .First(t => t.LastSequenceFlow?.Id == sequenceFlowId).State = 
                first ? FlowNodeState.Completing : FlowNodeState.Merged;
            first = false;
        }
    }
}