using core_engine.Exceptions;

namespace core_engine.Handler;

internal class ParallelGatewayHandler : DefaultFlowNodeHandler
{
    public override void Execute(InstanceEngine processInstance, Token token)
    {
        var incomingSequenceFlowIds = processInstance.Process.FlowElements.OfType<SequenceFlow>()
            .Where(x => x.TargetRef.Id == token.CurrentBaseElement.Id)
            .Select(sf => sf.Id)
            .ToList();

        var tokensAtInput = new List<Token>();
        foreach (var tokenAtInput in incomingSequenceFlowIds
                     .Select(incomingSequenceFlowId => processInstance.Tokens
                         .FirstOrDefault(x =>
                             x.CurrentBaseElement.Id == token.CurrentBaseElement.Id &&
                             x.LastSequenceFlow?.Id == incomingSequenceFlowId && x.State == FlowNodeState.Active)))
        {
            if (tokenAtInput == null)
                return; //not all incoming flow nodes are active
            tokensAtInput.Add(tokenAtInput);
        }

        tokensAtInput[0].State = FlowNodeState.Completing;
        foreach (var tokenToMerge in tokensAtInput[1..])
            tokenToMerge.State = FlowNodeState.Merged;
    }

    public override List<Token>? GenerateOutgoingTokens(FlowzerConfig config, InstanceEngine processInstance, Token token)
    {
        if (processInstance.Process.FlowElements
            .OfType<SequenceFlow>()
            .Any(x => x.SourceRef == token.CurrentFlowNode && (x.FlowzerCondition is not null || x.FlowzerIsDefault is true)))
            throw new FlowzerRuntimeException("There is a SequenceFlow with a Condition or default for Parallel Gateway");
        
        return base.GenerateOutgoingTokens(config, processInstance, token);
    }
}