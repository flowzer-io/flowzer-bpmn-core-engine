namespace core_engine.Handler;

public class DefaultFlowNodeHandler : IFlowNodeHandler
{
    public virtual void Execute(ProcessInstance processInstance, Token token)
    {
        token.State = FlowNodeState.Completing;
    }

    /// <summary>
    /// Geht vom aktuellen Token aus entsprechend der Sequenzflüsse zu den nächsten FlowNodes und legt entsprechend
    /// neue Tokens an. Dabei wird auf die Bedingungen der Sequenzflüsse geachtet. Alle Sequenfzlüsse, die keine Bedingung
    /// haben oder deren Bedingung erfüllt ist, erzeugen einen neuen Token. Sollte es Sequenzflüsse geben, die Bedingungen
    /// haben aber keine erfüllt ist, wird, falls vorhanden, ein Token für den Standardfluss erzeugt.
    /// </summary>
    /// <param name="processInstance">Instanz des Prozesses</param>
    /// <param name="token">Aktueller Token, von dem aus weitergelaufen werden soll</param>
    /// <param name="config">Flowzer Konfiguration</param>
    /// <exception cref="NotImplementedException"></exception>
    public virtual List<Token>? GenerateOutgoingTokens(FlowzerConfig config, ProcessInstance processInstance, Token token)
    {
        
        // 1. Finde alle ausgehenden Sequenzflüsse des aktuellen FlowNodes
        var outgoingSequenceFlows = processInstance.ProcessModel.FlowElements
            .OfType<SequenceFlow>()
            .Where(x => x.SourceRef == token.CurrentFlowNode)
            .ToArray();
        

        // 2.1 Filtere die Sequenzflüsse, entferne alle die Bedingungen haben und deren Bedingungen NICHT erfüllt sind
        var sequenceFlowsWithConditionsPresent = outgoingSequenceFlows.Any(x => x.FlowzerCondition is not null);
        outgoingSequenceFlows = outgoingSequenceFlows.Where(x =>
            x.FlowzerCondition is null
            || config.ExpressionHandler.MatchExpression(processInstance.ProcessVariables, x.FlowzerCondition)
        ).ToArray();
        
        // 2.2 Wenn es einen Default-Sequenzfluss gibt, dann lösche diesen, falls es noch einen Sequenzfluss mit Bedingung
        if (sequenceFlowsWithConditionsPresent && outgoingSequenceFlows.Any(x => x.FlowzerIsDefault == true))
        {
            outgoingSequenceFlows = outgoingSequenceFlows.Where(x => x.FlowzerIsDefault is null or false).ToArray();
        }
        
        // 3. Erzeuge für jeden Sequenzfluss ein neues Token
        // 3.1 Setze den neuen FlowNode des Tokens auf den FlowNode des Sequenzflusses
        // 3.2 Setze den State des Tokens auf Ready
        // 3.3 Füge das Token der Liste der Tokens hinzu
        
        return outgoingSequenceFlows.Select(x=>
            new Token
            {
                ProcessInstance = processInstance,
                ProcessInstanceId = processInstance.Id,
                CurrentFlowNode = x.TargetRef.ApplyResolveExpression<FlowNode>(config.ExpressionHandler.ResolveString, processInstance.ProcessVariables),
                LastSequenceFlow = x,
                State = FlowNodeState.Ready,
                ActiveBoundaryEvents = processInstance.ProcessModel
                    .FlowElements
                    .OfType<BoundaryEvent>()
                    .Where(b => b.AttachedToRef == x.TargetRef)
                    .Select(b => b.ApplyResolveExpression<BoundaryEvent>
                        (config.ExpressionHandler.ResolveString, processInstance.ProcessVariables)).ToList()
            }
        ).ToList();

    }
}