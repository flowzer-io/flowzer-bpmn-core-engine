namespace core_engine.Handler;

public class DefaultFlowNodeHandler : IFlowNodeHandler
{
    public virtual void Execute(ProcessInstance processInstance, Token token)
    {
        token.State = FlowNodeState.Completing;
    }
    
    /// <summary>
    /// Geht vom aktuellen Token aus entsprechend der Sequenzflüsse zu den nächsten FlowNodes und legt entsprechnd
    /// neue Tokens an. Dabei wird auf die Bedingungen der Sequenzflüsse geachtet.
    /// </summary>
    /// <param name="token">Aktueller Token, von dem aus weitergelaufen werden soll</param>
    /// <exception cref="NotImplementedException"></exception>
    public virtual List<Token>? GenerateOutgoingTokens(FlowzerConfig config, ProcessInstance processInstance, Token token)
    {
        
        // 1. Finde alle ausgehenden Sequenzflüsse des aktuellen FlowNodes
        var outgoingSequenceFlows = processInstance.ProcessModel.FlowElements
            .OfType<SequenceFlow>()
            .Where(x => x.SourceRef == token.CurrentFlowNode);
        

        // 2.1 Filtere die Sequenzflüsse, entferne alle die Bedingungen haben und deren Bedingungen NICHT erfüllt sind
        outgoingSequenceFlows = outgoingSequenceFlows.Where(x =>
            x.FlowzerCondition is null
            || config.ExpressionHandler.MatchExpression(processInstance.ProcessVariables, x.FlowzerCondition)
        ).ToArray();
        
        
        // 3. Erzeuge für jeden Sequenzfluss ein neues Token
        // 3.1 Setze den neuen FlowNode des Tokens auf den FlowNode des Sequenzflusses
        // 3.2 Setze den State des Tokens auf Ready
        // 3.3 Füge das Token der Liste der Tokens hinzu

        return outgoingSequenceFlows.Select(x=>
            new Token
            {
                ProcessInstance = processInstance,
                ProcessInstanceId = processInstance.Id,
                CurrentFlowNode = x.TargetRef with { },
                LastSequenceFlow = x,
                State = FlowNodeState.Ready
            }
        ).ToList();

    }
}