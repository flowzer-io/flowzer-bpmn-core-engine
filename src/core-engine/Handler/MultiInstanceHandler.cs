using System.Collections;
using core_engine.Exceptions;
using core_engine.Extensions;

namespace core_engine.Handler;

public class MultiInstanceHandler : DefaultFlowNodeHandler
{
    public override void Execute(InstanceEngine processInstance, Token token)
    {
        var multiInstanceType = GetMultiInstanceType((Activity)token.CurrentBaseElement);
        var childTokens = processInstance.Tokens.Where(t => t.ParentTokenId == token.Id).ToList();

        var multiInstanceTokens =
            GenerateMultiInstanceTokens(token, multiInstanceType, processInstance);

        switch (multiInstanceType)
        {
            case MultiInstanceType.Parallel:
                if (childTokens.Count == 0)
                {
                    processInstance.Tokens.AddRange(
                        GenerateMultiInstanceTokens(token, multiInstanceType, processInstance));
                    return;
                }

                break;

            case MultiInstanceType.Sequential:
                if (childTokens.Count < multiInstanceTokens.Count &&
                    childTokens.All(t => t.State == FlowNodeState.Completed))
                {
                    processInstance.Tokens.Add(multiInstanceTokens[childTokens.Count]);
                    return;
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(token));
        }

        if (childTokens.Count != multiInstanceTokens.Count ||
            childTokens.Any(t => t.State != FlowNodeState.Completed)) return;
        
        var loopActivity = (Activity)token.CurrentFlowNode!;
        var loopCharacteristics = ((MultiInstanceLoopCharacteristics)loopActivity.LoopCharacteristics!)
            .FlowzerLoopCharacteristics!;
        var outCollection = new List<object?>();
        foreach (var completedToken in childTokens)
        {
            if (!string.IsNullOrEmpty(loopCharacteristics.OutputElement) && completedToken.OutputData != null)
            {
                outCollection.Add(FlowzerConfig.Default.ExpressionHandler.GetValue(completedToken.OutputData,
                    loopCharacteristics.OutputElement));
            }
            else
            {
                if (completedToken.OutputData != null)
                    outCollection.Add(completedToken.OutputData);
            }
        }

        if (loopCharacteristics.OutputCollection != null)
            processInstance.ProcessVariables.SetValue(loopCharacteristics.OutputCollection, outCollection);
        
        token.State = FlowNodeState.Completing;
    }

    private static MultiInstanceType GetMultiInstanceType(Activity targetFlowNode)
    {
        if (targetFlowNode.LoopCharacteristics is not MultiInstanceLoopCharacteristics loopCharacteristics)
            return MultiInstanceType.None;

        return loopCharacteristics.IsSequential ? MultiInstanceType.Sequential : MultiInstanceType.Parallel;
    }

    private List<Token> GenerateMultiInstanceTokens(Token token, MultiInstanceType sequenceType,
        InstanceEngine instance)
    {
        var currentFlowNode = (Activity)token.CurrentFlowNode!;
        var multiInstanceLoopCharacteristics = ((MultiInstanceLoopCharacteristics)currentFlowNode.LoopCharacteristics!);
        var flowzerLoopCharacteristics = multiInstanceLoopCharacteristics.FlowzerLoopCharacteristics!;
        var data = flowzerLoopCharacteristics.InputCollection;

        if (data is not IEnumerable enumerableList)
            throw new FlowzerRuntimeException("InputCollection is not an IEnumerable");

        var ret = new List<Token>();
        var loopCounter = 1;

        foreach (var item in enumerableList)
        {
            Variables dataObj;
            if (string.IsNullOrEmpty(flowzerLoopCharacteristics.InputElement))
            {
                var expandoObject = (Variables?)item?.ToDynamic(true);
                dataObj = expandoObject ?? new Variables();
            }
            else
            {
                var expandoObj = new Variables();
                expandoObj.TryAdd(flowzerLoopCharacteristics.InputElement, item.ToDynamic());
                dataObj = expandoObj;
            }

            dataObj.SetValue("loopCounter", loopCounter++);
            var flowNodeWithoutLoop = currentFlowNode with { LoopCharacteristics = null };
            var newToken =
                new Token
                {
                    CurrentBaseElement =
                        flowNodeWithoutLoop.ApplyResolveExpression<FlowNode>(
                            FlowzerConfig.Default.ExpressionHandler.ResolveString,
                            instance.ProcessVariables),
                    ActiveBoundaryEvents = instance.Process
                        .FlowElements
                        .OfType<BoundaryEvent>()
                        .Where(b => b.AttachedToRef.Id == flowNodeWithoutLoop.Id)
                        .Select(b => b.ApplyResolveExpression<BoundaryEvent>(
                            FlowzerConfig.Default.ExpressionHandler.ResolveString,
                            instance.ProcessVariables)).ToList(),
                    InputData = dataObj,
                    OutputData = dataObj,
                    ParentTokenId = token.Id
                };
            ret.Add(newToken);

            if (multiInstanceLoopCharacteristics.CompletionCondition?.Body != null)
            {
                var completionCondition = multiInstanceLoopCharacteristics.CompletionCondition.Body;
                var completionConditionValue =
                    FlowzerConfig.Default.ExpressionHandler.MatchExpression(dataObj, completionCondition);
                if (completionConditionValue)
                    break;
            }
        }

        return ret;
    }
}