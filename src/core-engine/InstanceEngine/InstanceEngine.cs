using core_engine.Exceptions;
using core_engine.Extensions;

namespace core_engine;

public partial class InstanceEngine
{
    public void Start(Variables? data)
    {
        CreateInitialTokens(data);
        if (Tokens.Count == 0) throw new Exception("No tokens created");
        Run();
    }

    private void Run()
    {
        var loopDetection = 200;
        while (Tokens.Any(token => token.State is FlowNodeState.Ready or FlowNodeState.Completing))
        {
            if (loopDetection-- == 0)
            {
                throw new EndlessLoopException();
            }
            RunSingleStep();
        }
    }

    private void RunSingleStep()
    {
        //generate new tokens for multi instance parallel activities
        foreach (var token in Tokens.Where(token => token.State is FlowNodeState.Ready).ToArray())
        {
            PrepareInputData(token);
            token.State = FlowNodeState.Active;

        }

        var activeTokens = Tokens.Where(token => token.State is FlowNodeState.Active).ToArray();
        
        //Execute all active Tokens, without MultiInstance
        foreach (var token in activeTokens.Where(t => 
                     t.CurrentBaseElement is 
                         not Activity { LoopCharacteristics: MultiInstanceLoopCharacteristics } and 
                         not BPMN.Process.Process and 
                         not SubProcess))
        {
            if (!FlowNodeHandlers.TryGetValue(token.CurrentBaseElement.GetType(), out var handler))
                throw new InvalidOperationException($"No handler found for {token.CurrentBaseElement.GetType()}");

            try
            {
                handler.Execute(this, token);
            }
            catch (Exception)
            {
                token.State = FlowNodeState.Failing;
                //TODO: Handle Exception
                throw;
            }
        }
     
        //Complete all Completing Tokens
        foreach (var token in Tokens.Where(token => token.State is FlowNodeState.Completing).ToArray())
        {
            token.State = FlowNodeState.Completed;
            PrepareOutputData(token);
            
            var parentToken = Tokens.SingleOrDefault(t => t.Id == token.ParentTokenId);
            if (parentToken?.CurrentFlowNode is Activity { LoopCharacteristics: MultiInstanceLoopCharacteristics })
            {
                // new MultiInstanceHandler().CompleteMultiInstance(this, parentToken);
                continue; // Don´t create Outgoing Tokens for MultiInstance Children Tokens
            } 

            if (!FlowNodeHandlers.TryGetValue(token.CurrentBaseElement.GetType(), out var handler))
                handler = new DefaultFlowNodeHandler(); // "There is no handler for this flow node";

            var newTokens = handler.GenerateOutgoingTokens(FlowzerConfig, this, token);
            if (newTokens != null)
                Tokens.AddRange(newTokens);
        }

        // Execute all MultiInstance Tokens
        foreach (var token in activeTokens.Where(t => t.CurrentBaseElement is Activity {
                     LoopCharacteristics: MultiInstanceLoopCharacteristics } ))
        {
            new MultiInstanceHandler().Execute(this, token);
        }
        
        // Execute all MultiInstance Tokens
        foreach (var token in activeTokens.Reverse().Where(t => t.CurrentBaseElement is BPMN.Process.Process or SubProcess))
        {
            new ProcessFlowNodeHandler().Execute(this, token);
            new DefaultFlowNodeHandler().GenerateOutgoingTokens(FlowzerConfig, this, token);
        }
        
        foreach (var token in Tokens.Where(token => token.State is FlowNodeState.Terminating))
        {
            // ToDo: Hier kann man noch Nachrichtenflüsse einbauen etc.
            token.State = FlowNodeState.Terminated;
        }
    }

    private void CreateInitialTokens(Variables? data)
    {
        foreach (var processStartFlowNode in Process.GetStartFlowNodes().Where(flowNode =>
                     flowNode.GetType() == typeof(StartEvent) || flowNode.GetType() == typeof(Activity)))
        {
            var token = CreateNewToken(data, processStartFlowNode, null);
            Tokens.Add(token);
        }
    }

    private Token CreateNewToken(Variables? data, FlowElement currentFlowNode, Token? previousToken)
    {
        var token = new Token
        {
            ParentTokenId = MasterToken.Id,
            CurrentBaseElement = 
                currentFlowNode.ApplyResolveExpression<FlowNode>(FlowzerConfig.ExpressionHandler.ResolveString,
                    MasterToken.OutputData!),
            ActiveBoundaryEvents = ((Process)MasterToken.CurrentBaseElement)
                .FlowElements
                .OfType<BoundaryEvent>()
                .Where(b => b.AttachedToRef == currentFlowNode)
                .Select(b => b.ApplyResolveExpression<BoundaryEvent>(FlowzerConfig.ExpressionHandler.ResolveString,
                    MasterToken.OutputData!)).ToList(),
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
            token.InputData = MasterToken.OutputData; // ToDo: Hier muss die Logik so umgeschrieben werden, das nur die Variablen im "Kontext" (Parent) beachtet werden.
            return;
        }

        token.InputData ??= new Variables();

        mapping.InputMappings?.ForEach(x =>
        {
            token.InputData.TryAdd(x.Target,
                FlowzerConfig.ExpressionHandler.GetValue(MasterToken.OutputData!, x.Source));
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
            ExpandoHelper.SetValue(MasterToken.OutputData!, x.Target, value);
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
        { typeof(Process), new DoNothingFlowNodeHandler() },
        { typeof(SubProcess), new ProcessFlowNodeHandler() },
        // {typeof(EventBasedGateway), new EventBasedGatewayHandler()},
        // {typeof(IntermediateCatchEvent), new IntermediateCatchEventHandler()},
        // {typeof(IntermediateThrowEvent), new IntermediateThrowEventHandler()},
        // {typeof(SequenceFlow), new SequenceFlowHandler()},
        // {typeof(FlowNode), new FlowNodeHandler()}
    };
}