namespace core_engine;

public class InstanceEngine(ProcessInstance instance)
{
    public ProcessInstance Instance { get; } = instance;

    private FlowzerConfig FlowzerConfig { get; } = FlowzerConfig.Default;

    public IEnumerable<Token> ActiveTokens => Instance.Tokens.Where(token => token.State == FlowNodeState.Active);

    public void Start(Variables? data)
    {
        CreateInitialTokens(data);
        if (Instance.Tokens.Count == 0) throw new Exception("No tokens created");
        Run();
    }

    internal void Run()
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
                x.State is FlowNodeState.Completed or FlowNodeState.Merged ||
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
                continue; // There is no handler for this flow node

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
                InputData = data ?? new Variables(),
                OutputData = data
            });
        }
    }

    /// <summary>
    /// Überträgt die Variablen des Prozesses in die Input-Daten des Tokens. Dabei wird auf die InputSet des
    /// FlowNodes geachtet. Gibt es keine, so werden alle Prozessvariablen übertragen.
    /// </summary>
    /// <param name="token">Der Token, in welchen der aktuelle Input Datensatz persistiert wird.</param>
    private void PrepareInputData(Token token)
    {
        if (token.CurrentFlowNode is not IFlowzerInputMapping mapping)
            return;

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

    public Task<IEnumerable<Escalation>> GetActiveEscalations()
    {
        throw new NotImplementedException();
    }

    public IEnumerable<Token> GetActiveUserTasks() => Instance.Tokens
        .Where(token => token is { CurrentFlowNode: UserTask, State: FlowNodeState.Ready });

    private Token GetToken(Guid tokenId)
    {
        return Instance.Tokens.Single(token => token.Id == tokenId);
    }

    public IEnumerable<Token> GetActiveServiceTasks() => Instance.Tokens
        .Where(token => token is { CurrentFlowNode: ServiceTask, State: FlowNodeState.Active });

    public Task<IEnumerable<MessageDefinition>> GetActiveThrowMessages()
    {
        throw new NotImplementedException();
    }

    public Task<IEnumerable<SignalDefinition>> GetActiveThrowSignals()
    {
        throw new NotImplementedException();
    }


    public void HandleEscalation(string escalationCode, string? code, object? escalationBody = null)
    {
        throw new NotImplementedException();
    }

    public void HandleError(string name, string errorCode, string? errorMessage = null, object? errorBody = null)
    {
        throw new NotImplementedException();
    }

    public void HandleUserTaskResponse(Guid tokenId, Variables response, string? userId = null)
    {
        var token = GetToken(tokenId);
        if (token.State != FlowNodeState.Active)
        {
            throw new Exception("Token ist nicht aktiv");
        }

        response.TryAdd("UserId", userId);
        token.OutputData = response;
        token.State = FlowNodeState.Completing;

        Run();
    }

    /// <summary>
    /// Wird aufgerufen, wenn ein ServiceTask ein Ergebnis zurückliefert.
    /// </summary>
    /// <param name="tokenId">ID des Tokens</param>
    /// <param name="result">Ergebnis-Daten des ServiceTasks</param>
    /// <exception cref="NotImplementedException"></exception>
    public void HandleServiceTaskResult(Guid tokenId, object? result = null)
    {
        var token = GetToken(tokenId);
        if (token.State != FlowNodeState.Active)
        {
            throw new FlowzerRuntimeException($"Token {tokenId} is not active");
        }

        if (result != null)
        {
            token.OutputData = (Variables?)result.ToDynamic();
        }

        token.State = FlowNodeState.Completing;
        Run();
    }

    /// <summary>
    /// Wird aufgerufen, wenn ein ServiceTask ein Ergebnis zurückliefert.
    /// </summary>
    /// <param name="taskType">Type (Implementation) des ServiceTasks. ACHTUNG: Funktioniert nur wenn genau ein ServiceTask mit dem Type wartet.</param>
    /// <param name="result">Ergebnis-Daten des ServiceTasks</param>
    /// <exception cref="NotImplementedException"></exception>
    public void HandleServiceTaskResult(string taskType, object? result = null)
    {
        var tokenId = Instance.Tokens.Single(token =>
            token.CurrentFlowNode.GetType() == typeof(ServiceTask) &&
            ((ServiceTask)token.CurrentFlowNode).Implementation == taskType &&
            token.State == FlowNodeState.Active).Id;
        HandleServiceTaskResult(tokenId, result);
    }

    /// <summary>
    /// Abbruch der Instanz. Dabei werden alle Tokens terminiert und die bereits durchlaufenen Activities kompensiert.
    /// Subprozesse werden ebenfalls abgebrochen.
    /// </summary>
    public void Cancel()
    {
        throw new NotImplementedException();
    }

    public List<TimerEventDefinition> ActiveTimers()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gibt eine Liste aktuell erwarteter Nachrichten zurück
    /// </summary>
    /// <returns>Liste von Nachrichten inkl. CorrelationKey</returns>
    /// <exception cref="NotImplementedException"></exception>
    public List<MessageDefinition> GetActiveCatchMessages()
    {
        var messageDefinitions = new List<MessageDefinition>();
        foreach (var token in Instance.Tokens)
        {
            if (token.CurrentFlowNode is IntermediateCatchEvent
                {
                    EventDefinition: MessageEventDefinition { MessageRef: not null } messageEventDefinition
                })
            {
                messageDefinitions.Add(new MessageDefinition()
                {
                    Name = messageEventDefinition.MessageRef.Name,
                    FlowzerCorrelationKey = messageEventDefinition.MessageRef.FlowzerCorrelationKey
                });
            }

            if (token.CurrentFlowNode is ReceiveTask
                {
                    MessageRef: not null
                } receiveTask)
            {
                messageDefinitions.Add(new MessageDefinition()
                {
                    Name = receiveTask.MessageRef.Name,
                    FlowzerCorrelationKey = receiveTask.MessageRef.FlowzerCorrelationKey
                });
            }

            messageDefinitions.AddRange(Instance.ProcessModel.FlowElements.OfType<FlowzerBoundaryMessageEvent>() // ToDo: hier durch Persistierte Token.BoundaryEvents ersetzen
                .Where(be => be.AttachedToRef == token.CurrentFlowNode)
                .Select(boundaryEvent => new MessageDefinition()
                {
                    Name = boundaryEvent.MessageDefinition.Name,
                    FlowzerCorrelationKey = "" // ToDo: Hier muss noch der CorrelationKey gesetzt werden
                }));
        }

        return messageDefinitions;
    }

    public List<SignalDefinition> GetActiveCatchSignals()
    {
        throw new NotImplementedException();
    }

    public Task<ProcessInstance> HandleTime(DateTime time)
    {
        throw new NotImplementedException();
    }

    public ProcessInstance HandleMessage(Message message)
    {
        throw new NotImplementedException();
    }

    public Task<ProcessInstance> HandleSignal(string signalName, object? signalData = null)
    {
        throw new NotImplementedException();
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
        // {typeof(ComplexGateway), new ComplexGatewayHandler()},
        // {typeof(EventBasedGateway), new EventBasedGatewayHandler()},
        // {typeof(IntermediateCatchEvent), new IntermediateCatchEventHandler()},
        // {typeof(IntermediateThrowEvent), new IntermediateThrowEventHandler()},
        // {typeof(BoundaryEvent), new BoundaryEventHandler()},
        // {typeof(SequenceFlow), new SequenceFlowHandler()},
        // {typeof(FlowNode), new FlowNodeHandler()}
    };
}