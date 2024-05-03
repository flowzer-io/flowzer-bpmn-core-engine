using BPMN.Activities;
using BPMN.Events;
using BPMN.HumanInteraction;
using BPMN.Process;
using core_engine.Handler;
using FlowzerBPMN;

namespace core_engine;

public class ProcessInstance : ICatchHandler
{
    /// <summary>
    /// Eindeutige Id der Instanz
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Das Model, das den BPMN-Prozess beschreibt
    /// </summary>
    public required Process ProcessModel { get; set; }

    /// <summary>
    /// Variablen des Prozesses
    /// </summary>
    public Variables ProcessVariables { get; set; } = "{}";

    /// <summary>
    /// Aktuelle Tokens
    /// </summary>
    public List<Token> Tokens { get; init; } = [];
    
    public void Run()
    {
        var loopDetection = 30;
        while (Tokens.Any(token => token.State is FlowNodeState.Ready or FlowNodeState.Completing))
        {
            if (loopDetection-- == 0)
            {
                throw new Exception("Endlosschleife erkannt");
            }
            
            foreach (var token in Tokens.Where(token => token.State == FlowNodeState.Ready))
            {
                PrepareInputData(token);
                token.State = FlowNodeState.Active;
            }

            foreach (var token in Tokens.Where(token => token.State is FlowNodeState.Active))
            {
                if (!FlowNodeHandlers.ContainsKey(token.CurrentFlowNode.GetType())) continue;
                FlowNodeHandlers[token.CurrentFlowNode.GetType()].Execute(token.InputData);
                token.State = FlowNodeState.Completing;
            }

            foreach (var token in Tokens.Where(token => token.State is FlowNodeState.Completing))
            {
                PrepareOutputData(token);
                token.State = FlowNodeState.Completed;
                GoToNextFlowNode(token);
            }
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
        throw new NotImplementedException();
    }

    /// <summary>
    /// Überträgt die Variablen des Prozesses in die Input-Daten des Tokens. Dabei wird auf die InputSet des
    /// FlowNodes geachtet. Gibt es keine, so werden alle Prozessvariablen übertragen.
    /// </summary>
    /// <param name="token">Der Token, in welchen der aktuelle Input Datensatz persistiert wird.</param>
    private void PrepareInputData(Token token)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Überträgt die Output-Variablen des Tokens in die Daten der Instanz. Dabei wird auf die OutputSet des
    /// FlowNodes geachtet. Gibt es keine, so werden alle Variablen übertragen.
    /// </summary>
    /// <param name="token">Der Token, in welchen der aktuelle Output Datensatz persistiert ist.</param>
    private void PrepareOutputData(Token token)
    {
        throw new NotImplementedException();
    }
    
    Task<IEnumerable<Escalation>> GetActiveEscalations()
    {
        throw new NotImplementedException();
    }

    public IEnumerable<string> GetActiveUserTasks()
    {
        return Tokens
            .Where(token => token is { CurrentFlowNode: UserTask, State: FlowNodeState.Ready })
            .Select(token => token.CurrentFlowNode.Id);
    }
    
    public Token GetToken(Guid tokenId)
    {
        return Tokens.Single(token => token.Id == tokenId);
    }
    
    public IEnumerable<Token> GetActiveServiceTasks()
    {
        return Tokens
            .Where(token => token is { CurrentFlowNode: ServiceTask, State: FlowNodeState.Ready });
    }

    Task<IEnumerable<MessageInfo>> GetActiveThrowMessages()
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
        throw new NotImplementedException();
    }
    
    public void HandleServiceTaskResult(Guid tokenId, string result)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Abbruch der Instanz
    /// </summary>
    public void Cancel()
    {
        throw new NotImplementedException();
    }

    public List<TimerDefinition> GetActiveTimers()
    {
        throw new NotImplementedException();
    }

    public List<MessageDefinition> GetActiveCatchMessages()
    {
        throw new NotImplementedException();
    }

    public List<SignalDefinition> GetActiveCatchSignals()
    {
        throw new NotImplementedException();
    }

    public Task<ProcessInstance> HandleTime(DateTime time)
    {
        throw new NotImplementedException();
    }

    public Task<ProcessInstance> HandleMessage(string messageName, string? correlationKey = null,
        object? messageData = null)
    {
        throw new NotImplementedException();
    }

    public Task<ProcessInstance> HandleSignal(string signalName, object? signalData = null)
    {
        throw new NotImplementedException();
    }
    
    private static readonly Dictionary<Type, IFlowNodeHandler> FlowNodeHandlers = new()
    {
        {typeof(StartEvent), new StartEventHandler()},
        // {typeof(EndEvent), new EndEventHandler()},
        // {typeof(Task), new TaskHandler()},
        // {typeof(ExclusiveGateway), new ExclusiveGatewayHandler()},
        // {typeof(ParallelGateway), new ParallelGatewayHandler()},
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