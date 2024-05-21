using System.Security.Cryptography;
using BPMN.Activities;
using BPMN.Common;
using BPMN.Events;
using BPMN.Flowzer;
using BPMN.Gateways;
using BPMN.HumanInteraction;
using BPMN.Process;
using core_engine.Handler;
using FlowzerBPMN;
using Newtonsoft.Json.Linq;
using Task = BPMN.Activities.Task;

namespace core_engine;

public class ProcessInstance : ICatchHandler
{

    public FrozerConfig FrozerConfig { get; set; } = FrozerConfig.Default;
    
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
    public Variables ProcessVariables { get; set; } = new();

    /// <summary>
    /// Aktuelle Tokens
    /// </summary>
    public List<Token> Tokens { get; init; } = [];
    
    /// <summary>
    /// Status der Instanz
    /// </summary>
    public ProcessInstanceState State {private set; get;}
    
    public IEnumerable<Token> ActiveTokens => Tokens.Where(token => token.State <= FlowNodeState.Ready);
    
    public void Start(Variables? data)
    {
        CreateInitialTokens(data);  
        Run();
    }

    private void Run()
    {
        var loopDetection = 200;
        while (Tokens.Any(token => token.State is FlowNodeState.Ready or FlowNodeState.Completing))
        {
            if (State != ProcessInstanceState.Running)
                State = ProcessInstanceState.Running;
            
            if (loopDetection-- == 0)
            {
                throw new EndlessLoopException();
            }
            
            foreach (var token in Tokens.Where(token => token.State is FlowNodeState.Ready))
            {
                PrepareInputData(token);
                token.State = FlowNodeState.Active;
            }

            foreach (var token in Tokens.Where(token => token.State is FlowNodeState.Active))
            {
                if (!FlowNodeHandlers.ContainsKey(token.CurrentFlowNode.GetType())) continue;
                FlowNodeHandlers[token.CurrentFlowNode.GetType()].Execute(this, token);
                // ToDo: TryCatch?
                token.State = FlowNodeState.Completing;
            }

            foreach (var token in Tokens.Where(token => token.State is FlowNodeState.Completing).ToArray())
            {
                PrepareOutputData(token);
                token.State = FlowNodeState.Completed;
                GoToNextFlowNode(token);
            }
        }
        
        if (Tokens.Any(x=>x.State == FlowNodeState.Active))
        {
            State = ProcessInstanceState.Waiting;
        }
        else
        {
            State = ProcessInstanceState.Completed;
        }
    }

    private void CreateInitialTokens(Variables? data)
    {
        foreach (var processStartFlowNode in ProcessModel.StartFlowNodes)
        {
            Tokens.Add(new Token
            {
                ProcessInstance = this,
                ProcessInstanceId = this.Id,
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
        var outgoingSequenceFlows = this.ProcessModel.FlowElements
            .OfType<SequenceFlow>()
            .Where(x => x.SourceRef == token.CurrentFlowNode);
        
        // 2. Filtere die Sequenzflüsse, die Bedingungen haben und deren Bedingungen erfüllt sind
        outgoingSequenceFlows = outgoingSequenceFlows.Where(x => FrozerConfig.ExpresssionHandler.MatchExpression(this, x.ConditionExpression));
        
        // 3. Erzeuge für jeden Sequenzfluss ein neues Token
        // 3.1 Setze den neuen FlowNode des Tokens auf den FlowNode des Sequenzflusses
        // 3.2 Setze den State des Tokens auf Ready
        // 3.3 Füge das Token der Liste der Tokens hinzu
        foreach (var outgoingSequenceFlow in outgoingSequenceFlows)
        {
            Tokens.Add(new Token
                {
                    ProcessInstance = this,
                    ProcessInstanceId = this.Id,
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
        if (!(token.CurrentFlowNode is IFlowzerInputMapping mapping))
            return;
        
        if (mapping.InputMappings == null) return;
        
        mapping.InputMappings.ForEach(x =>
        {
             token.InputData.Add(x.Target, FrozerConfig.ExpresssionHandler.GetValue(this, x.Source));
        });
    }

    /// <summary>
    /// Überträgt die Output-Variablen des Tokens in die Daten der Instanz. Dabei wird auf die OutputSet des
    /// FlowNodes geachtet. Gibt es keine, so werden alle Variablen übertragen.
    /// </summary>
    /// <param name="token">Der Token, in welchen der aktuelle Output Datensatz persistiert ist.</param>
    private void PrepareOutputData(Token token)
    {
        if (!(token.CurrentFlowNode is IFlowzerOutputMapping mapping))
            return;
        
        if (mapping.OutputMappings == null) return;
        
        mapping.OutputMappings.ForEach(x =>
        {
            ProcessVariables[x.Target] = FrozerConfig.ExpresssionHandler.GetValue(token.OutputData as dynamic, x.Source);
        });
    }
    
    Task<IEnumerable<Escalation>> GetActiveEscalations()
    {
        throw new NotImplementedException();
    }

    public IEnumerable<Token> GetActiveUserTasks() => Tokens
            .Where(token => token is { CurrentFlowNode: UserTask, State: FlowNodeState.Ready });
    
    public Token GetToken(Guid tokenId)
    {
        return Tokens.Single(token => token.Id == tokenId);
    }
    
    public IEnumerable<Token> GetActiveServiceTasks() => Tokens
        .Where(token => token is { CurrentFlowNode: ServiceTask, State: FlowNodeState.Active });
    
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
        var jResponse = Variables.Parse(response);
        var token = GetToken(tokenId);
        if (token.State != FlowNodeState.Active)
        {
            throw new Exception("Token ist nicht aktiv");
        }
        token.OutputData = jResponse; // ToDo: Hier sollte noch die UserID gesetzt werden
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

    public List<TimerEventDefinition> GetActiveTimers()
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
        foreach (var token in Tokens)
        {
            if (token.CurrentFlowNode is not IntermediateCatchEvent
                {
                    EventDefinition: MessageEventDefinition messageEventDefinition
                }) continue;
            if (messageEventDefinition.MessageRef is null) continue;
            
            messageDefinitions.Add(new MessageDefinition
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
        {typeof(StartEvent), new TuNichtsHandler()},
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

public class FrozerConfig
{
    public IExpresssionHandler ExpresssionHandler { get; set; }

    public static FrozerConfig Default => new FrozerConfig()
    {
        ExpresssionHandler = new JavaScriptV8ExpressionHandler()
    };
}

public interface IExpresssionHandler
{
    /// <summary>
    /// Get the value by expression from the process variables 
    /// </summary>
    JToken GetValue(ProcessInstance processInstance, string expression);
    
    /// <summary>
    /// Checks if the expression matches the conditions of the sequence flow
    /// </summary>
    bool MatchExpression(ProcessInstance processInstance, Expression? expression);
}