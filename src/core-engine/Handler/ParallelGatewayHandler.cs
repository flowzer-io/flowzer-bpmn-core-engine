using core_engine.Handler;

namespace core_engine;

internal class ParallelGatewayHandler : DefaultFlowNodeHandler
{
    public override void Execute(ProcessInstance processInstance, Token token)
    {
        var incomingSequenceFlowIds = processInstance.ProcessModel.FlowElements.OfType<SequenceFlow>()
            .Where(x => x.TargetRef.Id == token.CurrentFlowNode.Id)
            .Select(sf => sf.Id)
            .ToList();
        
        var tokensAtInput = new List<Token>();
        foreach (var incomingSequenceFlowId in incomingSequenceFlowIds)
        {
            var tokenAtInput = processInstance.Tokens
                .FirstOrDefault(x =>
                    x.CurrentFlowNode.Id == token.CurrentFlowNode.Id &&
                    x.LastSequenceFlow?.Id == incomingSequenceFlowId && x.State == FlowNodeState.Active);
            if (tokenAtInput == null) 
                return; //not all incoming flow nodes are active
            tokensAtInput.Add(tokenAtInput);
        }
        
        tokensAtInput.First().State = FlowNodeState.Completing;
        foreach (var tokenToMerge in tokensAtInput[1..])
            tokenToMerge.State = FlowNodeState.Merged;
    }
}