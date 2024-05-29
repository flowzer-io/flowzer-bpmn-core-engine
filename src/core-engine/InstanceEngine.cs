using BPMN.Activities;
using BPMN.Common;
using BPMN.Events;
using BPMN.Flowzer;
using BPMN.Flowzer.Events;
using BPMN.Gateways;
using BPMN.HumanInteraction;
using core_engine.Handler;
using Model;
using Newtonsoft.Json;
using Task = BPMN.Activities.Task;

namespace core_engine;

public class InstanceEngine(ProcessInstance instance)
{
    public ProcessInstance Instance { get; set; } = instance;
    
    public FlowzerConfig FlowzerConfig { get; set; } = FlowzerConfig.Default;
    
    public IEnumerable<Token> ActiveTokens => Instance.Tokens.Where(token => token.State <= FlowNodeState.Ready);
    
    public void Start(Variables? data)
    {
        CreateInitialTokens(data);
        if (instance.Tokens.Count == 0) throw new Exception("No tokens created");
        Run();
    }

    internal void Run()
    {
        var loopDetection = 200;
        while (Instance.Tokens.Any(token => token.State is FlowNodeState.Ready or FlowNodeState.Completing))
        {
            if (Instance.State != ProcessInstanceState.Running)
                Instance.State = ProcessInstanceState.Running;
            
            if (loopDetection-- == 0)
            {
                throw new EndlessLoopException();
            }
            
            foreach (var token in Instance.Tokens.Where(token => token.State is FlowNodeState.Ready))
            {
                PrepareInputData(token);
                token.State = FlowNodeState.Active;
            }

            foreach (var token in Instance.Tokens.Where(token => token.State is FlowNodeState.Active))
            {
                if (!FlowNodeHandlers.ContainsKey(token.CurrentFlowNode.GetType())) continue;
                FlowNodeHandlers[token.CurrentFlowNode.GetType()].Execute(Instance, token);
                // ToDo: TryCatch?
                token.State = FlowNodeState.Completing;
            }

            foreach (var token in Instance.Tokens.Where(token => token.State is FlowNodeState.Completing).ToArray())
            {
                PrepareOutputData(token);
                token.State = FlowNodeState.Completed;
                GoToNextFlowNode(token);
            }
        }
        
        if (Instance.Tokens.Any(x=>x.State == FlowNodeState.Active))
        {
            Instance.State = ProcessInstanceState.Waiting;
        }
        else
        {
            Instance.State = ProcessInstanceState.Completed;
        }
    }

    private void CreateInitialTokens(Variables? data)
    {
        foreach (var processStartFlowNode in Instance.ProcessModel.StartFlowNodes.Where(flowNode => flowNode.GetType() == typeof(StartEvent) || flowNode.GetType() == typeof(Activity)))
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
    /// Geht vom aktuellen Token aus entsprechend der Sequenzflüsse zu den nächsten FlowNodes und legt entsprechnd
    /// neue Tokens an. Dabei wird auf die Bedingungen der Sequenzflüsse geachtet.
    /// </summary>
    /// <param name="token">Aktueller Token, von dem aus weitergelaufen werden soll</param>
    /// <exception cref="NotImplementedException"></exception>
    private void GoToNextFlowNode(Token token)
    {
        // 1. Finde alle ausgehenden Sequenzflüsse des aktuellen FlowNodes
        var outgoingSequenceFlows = Instance.ProcessModel.FlowElements
            .OfType<SequenceFlow>()
            .Where(x => x.SourceRef == token.CurrentFlowNode);
        
        // 2. Filtere die Sequenzflüsse, die Bedingungen haben und deren Bedingungen erfüllt sind
        outgoingSequenceFlows = outgoingSequenceFlows.Where(x => FlowzerConfig.ExpressionHandler.MatchExpression(Instance, x.ConditionExpression));
        
        // 3. Erzeuge für jeden Sequenzfluss ein neues Token
        // 3.1 Setze den neuen FlowNode des Tokens auf den FlowNode des Sequenzflusses
        // 3.2 Setze den State des Tokens auf Ready
        // 3.3 Füge das Token der Liste der Tokens hinzu
        foreach (var outgoingSequenceFlow in outgoingSequenceFlows)
        {
            Instance.Tokens.Add(new Token
                {
                    ProcessInstance = Instance,
                    ProcessInstanceId = Instance.Id,
                    CurrentFlowNode = outgoingSequenceFlow.TargetRef,
                    State = FlowNodeState.Ready
                }
            );
        }
        
    }

    /// <summary>
    /// Überträgt die Variablen des Prozesses in die Input-Daten des Tokens. Dabei wird auf die InputSet des
    /// FlowNode
    /// ,s geachtet. Gibt es keine, so werden alle Prozessvariablen übertragen.
    /// </summary>
    /// <param name="token">Der Token, in welchen der aktuelle Input Datensatz persistiert wird.</param>
    private void PrepareInputData(Token token)
    {
        if (token.CurrentFlowNode is not IFlowzerInputMapping mapping)
            return;

        mapping.InputMappings?.ForEach(x =>
        {
            token.InputData.TryAdd(x.Target, FlowzerConfig.ExpressionHandler.GetValue(Instance.ProcessVariables, x.Source));
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
        
        if (mapping.OutputMappings == null) return;
        
        mapping.OutputMappings.ForEach(x =>
        {
            
            ((IDictionary<string,object>)Instance.ProcessVariables)[x.Target] = FlowzerConfig.ExpressionHandler.GetValue(token.OutputData as dynamic, x.Source);
        });
    }
    
    Task<IEnumerable<Escalation>> GetActiveEscalations()
    {
        throw new NotImplementedException();
    }

    public IEnumerable<Token> GetActiveUserTasks() => Instance.Tokens
        .Where(token => token is { CurrentFlowNode: UserTask, State: FlowNodeState.Ready });
    
    public Token GetToken(Guid tokenId)
    {
        return Instance.Tokens.Single(token => token.Id == tokenId);
    }
    
    public IEnumerable<Token> GetActiveServiceTasks() => Instance.Tokens
        .Where(token => token is { CurrentFlowNode: ServiceTask, State: FlowNodeState.Active });
    
    Task<IEnumerable<MessageDefinition>> GetActiveThrowMessages()
    {
        throw new NotImplementedException();
    }

    Task<IEnumerable<SignalInfo>> GetActiveThrowSignals()
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

    public void HandleUserTaskResponse(Guid tokenId, string response, string? userId = null)
    {
        var token = GetToken(tokenId);
        if (token.State != FlowNodeState.Active)
        {
            throw new Exception("Token ist nicht aktiv");
        }
        token.OutputData = JsonConvert.DeserializeObject<Variables>(response); // ToDo: Hier sollte noch die UserID gesetzt werden
        token.State = FlowNodeState.Completing;
    }
    
    /// <summary>
    /// Wird aufgerufen, wenn ein ServiceTask ein Ergebnis zurückliefert.
    /// </summary>
    /// <param name="tokenId">ID des Tokens</param>
    /// <param name="result">Ergebnis-Daten des ServiceTasks</param>
    /// <exception cref="NotImplementedException"></exception>
    public void HandleServiceTaskResult(Guid tokenId, Variables result)
    {
        var token = GetToken(tokenId);
        if (token.State != FlowNodeState.Active)
        {
            throw new FlowzerRuntimeException($"Token {tokenId} is not active");
        }
        token.OutputData = result;
        token.State = FlowNodeState.Completing;
        
        Run();
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
    public List<Message> GetActiveCatchMessages()
    {
        var messageDefinitions = new List<Message>();
        foreach (var token in Instance.Tokens)
        {
            if (token.CurrentFlowNode is not IntermediateCatchEvent
                {
                    EventDefinition: MessageEventDefinition messageEventDefinition
                }) continue;
            if (messageEventDefinition.MessageRef is null) continue;
            
            messageDefinitions.Add(new Message
            {
                Name = messageEventDefinition.MessageRef.Name,
                CorrelationKey = "" // ToDo: Hier muss noch der CorrelationKey gesetzt werden
            });
            
            // ToDo: Mögliche BoundaryEvents und ReceiveTasks berücksichtigen
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
        {typeof(StartEvent), new TuNichtsHandler()},
        {typeof(FlowzerMessageStartEvent), new TuNichtsHandler()},
        {typeof(EndEvent), new TuNichtsHandler()},
        {typeof(Task), new TuNichtsHandler()},
        {typeof(ExclusiveGateway), new TuNichtsHandler()},
        {typeof(ParallelGateway), new TuNichtsHandler()},
        // {typeof(InclusiveGateway), new InclusiveGatewayHandler()},
        // {typeof(ComplexGateway), new ComplexGatewayHandler()},
        // {typeof(EventBasedGateway), new EventBasedGatewayHandler()},
        // {typeof(IntermediateCatchEvent), new IntermediateCatchEventHandler()},
        // {typeof(IntermediateThrowEvent), new IntermediateThrowEventHandler()},
        // {typeof(BoundaryEvent), new BoundaryEventHandler()},
        // {typeof(SequenceFlow), new SequenceFlowHandler()},
        // {typeof(FlowNode), new FlowNodeHandler()}
    };

    
   
}