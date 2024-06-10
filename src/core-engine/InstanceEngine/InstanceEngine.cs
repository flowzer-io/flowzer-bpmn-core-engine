using System.Collections;
using core_engine.Exceptions;
using core_engine.Extensions;

namespace core_engine;

public partial class InstanceEngine
{
        public void Start(Variables? data)
    {
        CreateInitialTokens(data);
        if (Instance.Tokens.Count == 0) throw new Exception("No tokens created");
        Run();
    }

    private void Run()
    {
        var loopDetection = 200;
        while (Instance.Tokens.Any(token => token.State is FlowNodeState.Ready or FlowNodeState.Completing))
        {
            if (loopDetection-- == 0)
            {
                throw new EndlessLoopException();
            }

            if (Instance.State != ProcessInstanceState.Running)
                Instance.State = ProcessInstanceState.Running;

            RunSingleStep();
        }

        if (Instance.Tokens.Any(x => x.State is FlowNodeState.Active))
            Instance.State = ProcessInstanceState.Waiting;
        if (Instance.Tokens.All(x =>
                x.State is FlowNodeState.Completed or FlowNodeState.Merged or FlowNodeState.Withdrawn ||
                x.CurrentFlowNode is ParallelGateway or ComplexGateway
            ))
            Instance.State = ProcessInstanceState.Completed;
    }

    private void RunSingleStep()
    {
        //generate new tokens for multi instance parallel activities
        foreach (var token in Instance.Tokens.Where(token => token.State is FlowNodeState.Ready).ToArray())
        {
            if (token.CurrentFlowNode is Activity activity && IsMultiInstanceParallelTarget(activity))
            {
                token.State = FlowNodeState.Completed ; // destroy original tokens
                Instance.Tokens.AddRange(GenerateMultiInstanceParallelTokens(FlowzerConfig, Instance, token, activity));
            }
        }
        
        //process all Ready tokens
        foreach (var token in Instance.Tokens.Where(token => token.State is FlowNodeState.Ready).ToArray())
        {
            PrepareInputData(token);
            token.State = FlowNodeState.Active;    
        }

        //Execute all Actice Tokens
        foreach (var token in Instance.Tokens.Where(token => token.State is FlowNodeState.Active))
        {
            if (!FlowNodeHandlers.TryGetValue(token.CurrentFlowNode.GetType(), out var handler))
                throw new InvalidOperationException($"No handler found for {token.CurrentFlowNode.GetType()}");

            try
            {
                handler.Execute(Instance, token);
            }
            catch (Exception)
            {
                token.State = FlowNodeState.Failing;
                Instance.State = ProcessInstanceState.Failing;
                //TODO: Handle Exception
                throw;
            }
        }

        //Complete all Completing Tokens
        foreach (var token in Instance.Tokens.Where(token => token.State is FlowNodeState.Completing).ToArray())
        {
            
            if (token.PreviousToken?.CurrentFlowNode is Activity activity && IsMultiInstanceParallelTarget(activity))
            {
                //the tokens comes from a multi instance parallel activity
                //are all tokens with the same previosnode waiting for loop end?
                if (Instance.Tokens.Any(x =>
                        x.Id != token.Id && 
                        x.PreviousToken?.Id == token.PreviousToken?.Id &&
                        x.State != FlowNodeState.WaitingForLoopEnd))
                {
                    token.State = FlowNodeState.WaitingForLoopEnd;
                    continue;
                }
                
                //all tokens are completing
                var completedTokens = instance.Tokens.Where(x => x.PreviousToken?.Id == token.PreviousToken.Id).ToArray();
                var loopCharacteristics = ((MultiInstanceLoopCharacteristics)activity!.LoopCharacteristics!).FlowzerLoopCharacteristics!;
                var outCollection = new List<object?>();
                foreach (var completedToken in completedTokens)
                {
                    completedToken.State = FlowNodeState.Completed;

                    if (!string.IsNullOrEmpty(loopCharacteristics.OutputElement) && completedToken.OutputData != null)
                    {
                        outCollection.Add(FlowzerConfig.ExpressionHandler.GetValue(completedToken.OutputData, loopCharacteristics.OutputElement));
                    }
                    else
                    {
                        if (completedToken.OutputData != null)
                            outCollection.Add(completedToken.OutputData);
                    }
                }
                
                if (loopCharacteristics.OutputCollection != null)
                    Instance.ProcessVariables.SetValue(loopCharacteristics.OutputCollection, outCollection);                

            }
            
            PrepareOutputData(token);
            token.State = FlowNodeState.Completed;

            if (!FlowNodeHandlers.TryGetValue(token.CurrentFlowNode.GetType(), out var handler))
                handler = new DefaultFlowNodeHandler(); // "There is no handler for this flow node";

            var newTokens = handler.GenerateOutgoingTokens(FlowzerConfig, Instance, token);
            if (newTokens != null)
                Instance.Tokens.AddRange(newTokens);
        }

        
        foreach (var token in Instance.Tokens.Where(token => token.State is FlowNodeState.Terminating))
        {
            // ToDo: Hier kann man noch Nachrichtenflüsse einbauen etc.
            token.State = FlowNodeState.Terminated;
            Instance.State = ProcessInstanceState.Terminated;
        }
    }

    
    private IEnumerable<Token> GenerateMultiInstanceParallelTokens(FlowzerConfig config, ProcessInstance processInstance, Token token, Activity activity)
    {
        var currentFlowNode = (Activity)token.CurrentFlowNode;
        var multiInstanceLoopCharacteristics = ((MultiInstanceLoopCharacteristics)currentFlowNode.LoopCharacteristics!);
        var flowzerLoopCharacteristics = multiInstanceLoopCharacteristics
            .FlowzerLoopCharacteristics!;
        var data = flowzerLoopCharacteristics.InputCollection;
        
        if (data is not IEnumerable enumerableList)
            throw new FlowzerRuntimeException("InputCollection is not an IEnumerable");

        var ret = new List<Token>();
        int loopCounter = 1;
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
            var newToken = CreateNewToken(dataObj, currentFlowNode with {LoopCharacteristics = null}, token); //the new node is not a loop node anymore!
            ret.Add(newToken);
            
            if (multiInstanceLoopCharacteristics.CompletionCondition?.Body != null)
            {
                var completionCondition = multiInstanceLoopCharacteristics.CompletionCondition.Body;
                var completionConditionValue = FlowzerConfig.ExpressionHandler.MatchExpression(dataObj, completionCondition);
                if (completionConditionValue)
                    break;
            }
        }
        return ret;
    }

    private bool IsMultiInstanceParallelTarget(Activity targetFlowNode)
    {
        if (targetFlowNode.LoopCharacteristics != null && targetFlowNode.LoopCharacteristics is MultiInstanceLoopCharacteristics loopCharacteristics)
        {
            return loopCharacteristics.IsSequential == false;
        }

        return false;
    }
    
    private void CreateInitialTokens(Variables? data)
    {
        foreach (var processStartFlowNode in Instance.ProcessModel.StartFlowNodes.Where(flowNode =>
                     flowNode.GetType() == typeof(StartEvent) || flowNode.GetType() == typeof(Activity)))
        {
            var token = CreateNewToken(data, processStartFlowNode, null);
            Instance.Tokens.Add(token);
        }
    }

    private Token CreateNewToken(Variables? data, FlowNode currentFlowNode, Token? previousToken)
    {
        var token = new Token
        {
            ProcessInstance = Instance,
            ProcessInstanceId = Instance.Id,
            CurrentFlowNode = currentFlowNode.ApplyResolveExpression<FlowNode>(FlowzerConfig.ExpressionHandler.ResolveString, Instance.ProcessVariables),
            ActiveBoundaryEvents = Instance.ProcessModel
                .FlowElements
                .OfType<BoundaryEvent>()
                .Where(b => b.AttachedToRef == currentFlowNode)
                .Select(b => b.ApplyResolveExpression<BoundaryEvent>(FlowzerConfig.ExpressionHandler.ResolveString,
                    Instance.ProcessVariables)).ToList(),
            InputData = data ?? new Variables(),
            OutputData = data,
            PreviousToken = previousToken
        };
        return token;
    }

    /// <summary>
    /// Überträgt die Variablen des Prozesses in die Input-Daten des Tokens. Dabei wird auf die InputSet des
    /// FlowNodes geachtet. Gibt es keine, so werden alle Prozessvariablen übertragen.
    /// </summary>
    /// <param name="token">Der Token, in welchen der aktuelle Input Datensatz persistiert wird.</param>
    private void PrepareInputData(Token token)
    {
        if (token.CurrentFlowNode is not IFlowzerInputMapping mapping)
        {
            token.InputData = Instance.ProcessVariables;
            return;
        }

        token.InputData ??= new Variables();
        
        mapping.InputMappings?.ForEach(x =>
        {
            token.InputData.TryAdd(x.Target,
                FlowzerConfig.ExpressionHandler.GetValue(Instance.ProcessVariables, x.Source));
        });
    }

    /// <summary>
    /// Überträgt die Output-Variablen des Tokens in die Daten der Instanz. Dabei wird auf die OutputSet des
    /// FlowNodes geachtet. Gibt es keine, so werden alle Variablen übertragen.
    /// </summary>
    /// <param name="token">Der Token, in welchen der aktuelle Output Datensatz persistiert ist.</param>
    private void PrepareOutputData(Token token)
    {
        if (token.CurrentFlowNode is not IFlowzerOutputMapping mapping)
            return;

        mapping.OutputMappings?.ForEach(x =>
        {
            var value = FlowzerConfig.ExpressionHandler.GetValue(token.OutputData as dynamic, x.Source);
            ExpandoHelper.SetValue(Instance.ProcessVariables, x.Target, value);
        });
    }
    
    private static readonly Dictionary<Type, IFlowNodeHandler> FlowNodeHandlers = new()
    {
        { typeof(StartEvent), new DefaultFlowNodeHandler() },
        { typeof(FlowzerMessageStartEvent), new DefaultFlowNodeHandler() },
        { typeof(EndEvent), new DefaultFlowNodeHandler() },
        { typeof(BPMN.Activities.Task), new DefaultFlowNodeHandler() },
        { typeof(ExclusiveGateway), new ExclusiveGatewayHandler() },
        { typeof(ParallelGateway), new ParallelGatewayHandler() },
        { typeof(ServiceTask), new DoNothingFlowNodeHandler() },
        { typeof(InclusiveGateway), new DefaultFlowNodeHandler() },
        { typeof(FlowzerTerminateEvent), new TerminateEndEventHandler() },
        { typeof(UserTask), new DoNothingFlowNodeHandler() },
        { typeof(ReceiveTask), new DoNothingFlowNodeHandler() },
        { typeof(FlowzerIntermediateMessageCatchEvent), new DoNothingFlowNodeHandler() },
        { typeof(FlowzerIntermediateSignalCatchEvent), new DoNothingFlowNodeHandler() },
        // {typeof(ComplexGateway), new ComplexGatewayHandler()},
        // {typeof(EventBasedGateway), new EventBasedGatewayHandler()},
        // {typeof(IntermediateCatchEvent), new IntermediateCatchEventHandler()},
        // {typeof(IntermediateThrowEvent), new IntermediateThrowEventHandler()},
        // {typeof(SequenceFlow), new SequenceFlowHandler()},
        // {typeof(FlowNode), new FlowNodeHandler()}
    };
}