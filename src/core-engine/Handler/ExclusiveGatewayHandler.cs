using core_engine.Exceptions;

namespace core_engine.Handler;

internal class ExclusiveGatewayHandler : DefaultFlowNodeHandler
{
    public override List<Token> GenerateOutgoingTokens(FlowzerConfig config, InstanceEngine processInstance, Token token)
    {
        var outgoingSequenceFlows = processInstance.Process.FlowElements
            .OfType<SequenceFlow>()
            .Where(x => x.SourceRef == token.CurrentFlowNode)
            .ToArray();
        
        if (outgoingSequenceFlows.Length > 1 && outgoingSequenceFlows.Any(x => x.FlowzerCondition is null && x.FlowzerIsDefault is null or false))
            throw new FlowzerRuntimeException("There is a SequenceFlow without a Condition and not default for Exclusive Gateway");
        
        var outgoingTokens = base.GenerateOutgoingTokens(config, processInstance, token);

        if (outgoingTokens is null || outgoingTokens.Count == 0)
            throw new FlowzerRuntimeException("No Condition is true for Exclusive Gateway");
        
        return outgoingTokens.Take(1).ToList();
    }
}