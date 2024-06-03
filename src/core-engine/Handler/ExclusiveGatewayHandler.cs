using core_engine.Handler;

namespace core_engine;

internal class ExclusiveGatewayHandler : DefaultFlowNodeHandler
{
    public override List<Token>? GenerateOutgoingTokens(FlowzerConfig config, ProcessInstance processInstance, Token token)
    {
        var outgoingTokens = base.GenerateOutgoingTokens(config, processInstance, token);
        if (outgoingTokens is null)
            return null;
        
        if (outgoingTokens.Count > 1)
        {
            outgoingTokens.RemoveRange(1,outgoingTokens.Count -1);
        }
        
        
        // 2.2 Wenn es einen Default-Sequenzfluss gibt, dann lösche diesen, falls es noch einen Sequenzfluss mit Bedingung
        // if (outgoingSequenceFlows.Any(s => s.ConditionExpression is not null) &&
        //     outgoingSequenceFlows.Any(s => s.FlowzerIsDefault is true))
        // {
        //     outgoingSequenceFlows = outgoingSequenceFlows.Where(s => s.FlowzerIsDefault is null or false);
        // }
        //
        
        return outgoingTokens;
    }
}