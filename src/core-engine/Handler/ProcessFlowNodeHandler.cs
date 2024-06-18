using core_engine.Extensions;

namespace core_engine.Handler;

public class ProcessFlowNodeHandler : DefaultFlowNodeHandler
{
    public override void Execute(InstanceEngine processInstance, Token token)
    {
        var process = (IFlowElementContainer)token.CurrentBaseElement;
        if (processInstance.Tokens.Count(t => t.ParentTokenId == token.Id) == 0)
        {
            var startFlowNodes = process.FlowElements.GetStartFlowNodes();
            token.OutputData = processInstance.ProcessVariables;
            foreach (var startFlowNode in startFlowNodes)
            {
                processInstance.Tokens.Add(new Token
                {
                    CurrentBaseElement = 
                        startFlowNode.ApplyResolveExpression<FlowNode>(
                            FlowzerConfig.Default.ExpressionHandler.ResolveString, token.OutputData),
                    ParentTokenId = token.Id,
                    State = FlowNodeState.Active,
                    ActiveBoundaryEvents = [],
                });
            }
        }

        var subTokens = processInstance.Tokens.Where(t => t.ParentTokenId == token.Id).ToList();
        
        if (subTokens.All(x =>
                x.State is FlowNodeState.Completed or FlowNodeState.Merged or FlowNodeState.Withdrawn
            ))
            token.State = FlowNodeState.Completing;
    }
}