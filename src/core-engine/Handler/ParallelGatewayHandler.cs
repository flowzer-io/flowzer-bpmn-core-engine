using core_engine.Exceptions;

namespace core_engine.Handler;

internal class ParallelGatewayHandler : DefaultFlowNodeHandler
{
    public override void Execute(InstanceEngine processInstance, Token token)
    {
        // Der Container des Tokens, nicht der Prozess: Ein paralleles Gateway in einem
        // Subprozess faende seine Sequenzfluesse auf der obersten Ebene sonst nicht.
        var incomingSequenceFlowIds = processInstance.GetContainerFlowElements(token).OfType<SequenceFlow>()
            .Where(x => x.TargetRef.Id == token.CurrentBaseElement.Id)
            .Select(sf => sf.Id)
            .ToList();

        var tokensAtInput = new List<Token>();
        foreach (var tokenAtInput in incomingSequenceFlowIds
                     .Select(incomingSequenceFlowId => processInstance.Tokens
                         .FirstOrDefault(x =>
                             x.CurrentBaseElement.Id == token.CurrentBaseElement.Id &&
                             // Nur Tokens desselben Scopes: In einem mehrfach durchlaufenen
                             // Subprozess gibt es denselben Knoten sonst mehrmals.
                             x.ParentTokenId == token.ParentTokenId &&
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
        if (processInstance.GetContainerFlowElements(token)
            .OfType<SequenceFlow>()
            .Any(x => x.SourceRef == token.CurrentFlowNode && (x.FlowzerCondition is not null || x.FlowzerIsDefault is true)))
            throw new FlowzerRuntimeException("There is a SequenceFlow with a Condition or default for Parallel Gateway");
        
        return base.GenerateOutgoingTokens(config, processInstance, token);
    }
}