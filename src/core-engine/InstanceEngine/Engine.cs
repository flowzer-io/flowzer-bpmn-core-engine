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
        foreach (var token in Instance.Tokens.Where(token => token.State is FlowNodeState.Ready))
        {
            PrepareInputData(token);
            token.State = FlowNodeState.Active;
        }

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
                instance.State = ProcessInstanceState.Failing;
                //TODO: Handle Exception
                throw;
            }
        }

        foreach (var token in Instance.Tokens.Where(token => token.State is FlowNodeState.Completing).ToArray())
        {
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
            instance.State = ProcessInstanceState.Terminated;
        }
    }

    private void CreateInitialTokens(Variables? data)
    {
        foreach (var processStartFlowNode in Instance.ProcessModel.StartFlowNodes.Where(flowNode =>
                     flowNode.GetType() == typeof(StartEvent) || flowNode.GetType() == typeof(Activity)))
        {
            Instance.Tokens.Add(new Token
            {
                ProcessInstance = Instance,
                ProcessInstanceId = Instance.Id,
                CurrentFlowNode = processStartFlowNode,
                ActiveBoundaryEvents = Instance.ProcessModel
                    .FlowElements
                    .OfType<BoundaryEvent>()
                    .Where(b => b.AttachedToRef == processStartFlowNode)
                    .Select(b => b.ApplyResolveExpression<BoundaryEvent>(FlowzerConfig.ExpressionHandler.ResolveString,
                        Instance.ProcessVariables)).ToList(),
                InputData = data ?? new Variables(),
                OutputData = data
            });
        }
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
        // {typeof(ComplexGateway), new ComplexGatewayHandler()},
        // {typeof(EventBasedGateway), new EventBasedGatewayHandler()},
        // {typeof(IntermediateCatchEvent), new IntermediateCatchEventHandler()},
        // {typeof(IntermediateThrowEvent), new IntermediateThrowEventHandler()},
        // {typeof(SequenceFlow), new SequenceFlowHandler()},
        // {typeof(FlowNode), new FlowNodeHandler()}
    };
}