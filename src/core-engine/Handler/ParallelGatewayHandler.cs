using BPMN.Common;
using core_engine.Handler;
using Model;

namespace core_engine;

internal class ParallelGatewayHandler : IFlowNodeHandler
{
    public void Execute(ProcessInstance processInstance, Token token)
    {
        var targetFlowNodeIds = processInstance.ProcessModel.FlowElements.OfType<SequenceFlow>()
            .Where(x => x.TargetRef.Id == token.CurrentFlowNode.Id)
            .Select(sf => sf.TargetRef.Id)
            .ToList();

        var actualActiveTokens = processInstance.Tokens
            .Where(t => t.CurrentFlowNode.Id == token.CurrentFlowNode.Id && t.State == FlowNodeState.Active)
            .ToList();

        targetFlowNodeIds.RemoveAll(fn => actualActiveTokens.Any(f => f.CurrentFlowNode.Id == fn));

        if (targetFlowNodeIds.Count != 0)
            return;

        actualActiveTokens[0].State = FlowNodeState.Completing;
        foreach (var activeToken in actualActiveTokens[1..])
        {
            activeToken.State = FlowNodeState.Merged;
        }
        
        // Dictionary<FlowNode, List<Token>> receivedTokens;
        // if (processInstance.ContextData.TryGetValue("ParallelGatewayHandler", out var value))
        // {
        //     receivedTokens = (Dictionary<FlowNode, List<Token>>)value!;
        // }
        // else
        // {
        //     receivedTokens = new Dictionary<FlowNode, List<Token>>();
        //     processInstance.ContextData.Add("ParallelGatewayHandler", receivedTokens);
        // }
        //
        // List<Token>? tokensOfNode;
        // if(!receivedTokens.TryGetValue(token.CurrentFlowNode, out tokensOfNode))
        // {
        //     tokensOfNode = new List<Token>();
        //     if (token.LastSequenceFlow == null)
        //         throw new InvalidOperationException("Token must have a last sequence flow. on parallelgateway handler. ");
        //     receivedTokens.Add(token.CurrentFlowNode, tokensOfNode);
        // }
        // tokensOfNode.Add(token);
        //
        // // Check if all incoming tokens have arrived
        // var allIncommingSequenceFlow =  processInstance.ProcessModel.FlowElements.OfType<SequenceFlow>()
        //     .Where(x => x.TargetRef.Id == token.CurrentFlowNode.Id);
        //
        // var usedTokensCandidates = new List<Token>();
        // foreach (var sequenceFlow in allIncommingSequenceFlow)
        // {
        //     var candidate = tokensOfNode.SingleOrDefault(x => x.LastSequenceFlow!.Id == sequenceFlow.Id);
        //     if (candidate == null)
        //     {
        //         //not all inputs have arrived, so set the current token as destroyed, so that not
        //         //outout token will be created
        //         token.IsDistroyed = true;
        //         return;
        //     }
        //     usedTokensCandidates.Add(candidate);
        // }
        //
        // //if this point is reached all incoming tokens have arrived!
        // //the current token will not be distroyed, so that the engine will create a new token for each outgoing sequence flow
        // token.IsDistroyed = false;
        //
        // foreach (var tokenToRemove in usedTokensCandidates)
        //     tokensOfNode.Remove(tokenToRemove);
        //
        // // If there are no more tokens for the current node, remove it from the dictionary
        // if (!tokensOfNode.Any())
        //     receivedTokens.Remove(token.CurrentFlowNode);
    }
}