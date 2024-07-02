using BPMN.Foundation;
using core_engine.Exceptions;
using core_engine.Extensions;

namespace core_engine.Handler;

public class DefaultFlowNodeHandler : IFlowNodeHandler
{
    public virtual void Execute(InstanceEngine processInstance, Token token)
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
    public virtual List<Token>? GenerateOutgoingTokens(FlowzerConfig config, InstanceEngine processInstance,
        Token token)
    {
        // Wenn es keinen Parent gibt, dann ist es der MasterToken und es gibt keine ausgehenden Sequenzflüsse
        if (token.ParentTokenId == null) return []; 
        
        // 1.1 Finde alle passenden FlowElements des aktuellen FlowNodes
        List<FlowElement>? flowElements = null;
        var currentToken = token;
        while (flowElements == null)
        {
            currentToken = processInstance.Tokens.Single(x => x.Id == currentToken.ParentTokenId);
            if (currentToken.CurrentBaseElement is Process or SubProcess)
            {
                flowElements = ((IFlowElementContainer)currentToken.CurrentBaseElement).FlowElements;
            }
                
        }

        // 1.2 Finde alle ausgehenden Sequenzflüsse des aktuellen FlowNodes
        var outgoingSequenceFlows = flowElements
            .OfType<SequenceFlow>()
            .Where(x => x.SourceRef.Id == token.CurrentBaseElement.Id)
            .ToArray();


        // 2.1 Filtere die Sequenzflüsse, entferne alle die Bedingungen haben und deren Bedingungen NICHT erfüllt sind
        var sequenceFlowsWithConditionsPresent = outgoingSequenceFlows.Any(x => x.FlowzerCondition is not null);
        outgoingSequenceFlows = outgoingSequenceFlows.Where(x =>
            x.FlowzerCondition is null
            || config.ExpressionHandler.MatchExpression(processInstance.VariablesToken(token).Variables!, x.FlowzerCondition)
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

        return outgoingSequenceFlows.Select(x =>
            new Token
            {
                ParentTokenId = token.ParentTokenId,
                CurrentBaseElement =
                    x.TargetRef.ApplyResolveExpression<FlowNode>(config.ExpressionHandler.ResolveString,
                        processInstance.VariablesToken(token).Variables!),
                LastSequenceFlow = x,
                State = FlowNodeState.Ready,
                ActiveBoundaryEvents = processInstance.Process
                    .FlowElements
                    .OfType<BoundaryEvent>()
                    .Where(b => b.AttachedToRef == x.TargetRef)
                    .Select(b => b.ApplyResolveExpression<BoundaryEvent>
                        (config.ExpressionHandler.ResolveString, processInstance.VariablesToken(token).Variables!)).ToList()
            }
        ).ToList();
    }
}