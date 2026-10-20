using core_engine.Extensions;

namespace core_engine.Handler;

/// <summary>Startet lokale Prozesspfade und bindet deren aktuellen Scope vor dem Ergebnisexport.</summary>
public class ProcessFlowNodeHandler : DefaultFlowNodeHandler
{
    /// <inheritdoc />
    public override void Execute(InstanceEngine processInstance, Token token)
    {
        var process = (IFlowElementContainer)token.CurrentBaseElement;
        if (processInstance.Tokens.All(t => t.ParentTokenId != token.Id)) // Wenn es noch keine ChildTokens gibt
        {
            var startFlowNodes = process.FlowElements.GetStartFlowNodes();
            // Ein ausdrücklich gemappter Subprozess exportiert nur seinen eigenen
            // Scope. Der historische Root-Alias ungemappter Modelle bleibt erhalten,
            // darf später aber nicht implizit in einen geschlossenen Parent importiert werden.
            token.OutputData = token.CurrentBaseElement is SubProcess { InputMappings.Count: > 0 }
                ? token.Variables ?? new Variables()
                : processInstance.GetProcessToken(token).Variables!;
            foreach (var startFlowNode in startFlowNodes)
            {
                processInstance.Tokens.Add(new Token
                {
                    CurrentBaseElement = 
                        startFlowNode.ApplyResolveExpression<FlowNode>(
                            processInstance.FlowzerConfig.ExpressionHandler.ResolveString, token.OutputData),
                    ProcessInstanceId = token.ProcessInstanceId,
                    ParentTokenId = token.Id,
                    State = FlowNodeState.Ready,
                    ActiveBoundaryEvents = [],
                });
            }
        }

        var subTokens = processInstance.Tokens.Where(t => t.ParentTokenId == token.Id).ToList();
        
        if (subTokens.All(x =>
                x.State is FlowNodeState.Completed or FlowNodeState.Merged or FlowNodeState.Withdrawn
            ))
        {
            // JSON-Persistenz bewahrt den anfänglichen Variables/OutputData-Alias nicht.
            // Human-Task-Ergebnisse gehören in den aktuellen Scope, nicht in dessen
            // Start-Snapshot. Nur ausdrücklich gemappte Subprozesse ändern diesen Vertrag.
            if (token.CurrentBaseElement is SubProcess { InputMappings.Count: > 0 })
                token.OutputData = token.Variables ?? new Variables();
            token.State = FlowNodeState.Completing;
        }
    }
}
