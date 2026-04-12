using core_engine.Exceptions;

namespace core_engine;

public partial class InstanceEngine: ICatchHandler
{
    public Guid InstanceId { get; set; } = Guid.NewGuid();

    public bool IsFinished
    {
        get
        {
            return State switch
            {
                ProcessInstanceState.Completed => true,
                ProcessInstanceState.Failed => true,
                ProcessInstanceState.Terminated => true,
                _ => false
            };
        }
    }

    //TODO: @ChristianMaaß wie lösen wir die diskrepant zwischen ProcessInstanceState und TokenState?
    public ProcessInstanceState State
    {
        get
        {
            return MasterToken.State switch
            {
                FlowNodeState.Active => ProcessInstanceState.Waiting,
                FlowNodeState.Completed => ProcessInstanceState.Completed,
                FlowNodeState.Failed => ProcessInstanceState.Failed,
                FlowNodeState.Terminated => ProcessInstanceState.Terminated,
                _ => throw new FlowzerRuntimeException("ProcessInstanceState nicht ermittelbar")
            };
        }
        
    }
    
    public InstanceEngine(List<Token> tokens, FlowzerConfig? flowzerConfig = null, bool allTokensIncluded = true)
    {
        if (tokens.SingleOrDefault(t => t.ParentTokenId == null) == null)
            throw new ArgumentException("Es muss mindestens ein (Master)Token vorhanden sein", nameof(tokens));
        Tokens = tokens;
        FlowzerConfig = flowzerConfig ?? FlowzerConfig.Default;
    }
    
    public List<Token> Tokens { get; }
    public FlowzerConfig FlowzerConfig { get; }

    public IEnumerable<Token> ActiveTokens => Tokens.Where(token => token.State == FlowNodeState.Active);
    
    public Token MasterToken => Tokens.Single(t => t.ParentTokenId == null);
    public Process Process => (Process)MasterToken.CurrentBaseElement;

    public ProcessInstanceState ProcessInstanceState => MasterToken.State switch
    {
        FlowNodeState.Active => ProcessInstanceState.Waiting,
        FlowNodeState.Failed => ProcessInstanceState.Failed,
        FlowNodeState.Completed => ProcessInstanceState.Completed,
        FlowNodeState.Terminated => ProcessInstanceState.Terminated,
        _ => throw new FlowzerRuntimeException("ProcessInstanceState nicht ermittelbar")
    };
    
    public System.Threading.Tasks.Task<IEnumerable<Escalation>> GetActiveEscalations()
    {
        return System.Threading.Tasks.Task.FromResult<IEnumerable<Escalation>>([]);
    }

    public IEnumerable<Token> GetActiveUserTasks() => Tokens
        .Where(token => token is { CurrentFlowNode: UserTask, State: FlowNodeState.Active });

    private Token GetToken(Guid tokenId)
    {
        return Tokens.Single(token => token.Id == tokenId);
    }

    public IEnumerable<Token> GetActiveServiceTasks() => Tokens
        .Where(token => token is { CurrentFlowNode: ServiceTask, State: FlowNodeState.Active });

    public IEnumerable<Token> GetActiveTasks() => Tokens
        .Where(token => token.State == FlowNodeState.Active);
    
    public void HandleEscalation(string escalationCode, string? code, object? escalationBody = null)
    {
        FailInstanceBestEffort();
    }

    public void HandleError(string name, string errorCode, string? errorMessage = null, object? errorBody = null)
    {
        FailInstanceBestEffort();
    }

    public void HandleTaskResult(Guid tokenId, Variables? data, Guid? userId = null)
    {
        var token = GetToken(tokenId);
        if (token.State != FlowNodeState.Active)
        {
            throw new Exception("Token ist nicht aktiv");
        }

        data ??= new Variables();
        data.TryAdd("UserId", userId);
        token.OutputData = data;
        token.State = FlowNodeState.Completing;

        Run();
    }

    /// <summary>
    /// Wird aufgerufen, wenn ein ServiceTask ein Ergebnis zurückliefert.
    /// </summary>
    /// <param name="taskType">Type (Implementation) des ServiceTasks. ACHTUNG: Funktioniert nur wenn genau ein ServiceTask mit dem Type wartet.</param>
    /// <param name="result">Ergebnis-Daten des ServiceTasks</param>
    /// <exception cref="NotImplementedException"></exception>
    public void HandleServiceTaskResult(string taskType, Variables? result = null)
    {
        var tokenId = Tokens.Single(token =>
            token.CurrentBaseElement.GetType() == typeof(ServiceTask) &&
            ((ServiceTask)token.CurrentBaseElement).Implementation == taskType &&
            token.State == FlowNodeState.Active).Id;
        HandleTaskResult(tokenId, result);
    }

    /// <summary>
    /// Bricht die Instanz best-effort ab, indem aktive bzw. wartende Tokens terminiert werden.
    /// Eine BPMN-Kompensation bereits ausgeführter Activities ist damit bewusst noch nicht verbunden.
    /// </summary>
    public void Cancel()
    {
        var tokensToTerminate = Tokens
            .Where(CanBeTerminatedByCancellation)
            .ToArray();

        foreach (var token in tokensToTerminate)
        {
            token.State = FlowNodeState.Terminating;
        }

        foreach (var token in Tokens.Where(token => token.State == FlowNodeState.Terminating))
        {
            token.State = FlowNodeState.Terminated;
        }
    }

    /// <summary>
    /// Führt fällige Intermediate-Timer-Catch-Events weiter.
    /// </summary>
    public void HandleTime(DateTime time)
    {
        HandleDueIntermediateTimerCatchEvents(time);
    }

    /// <summary>
    /// Führt fällige Timer-Start- oder Intermediate-Timer-Catch-Events weiter.
    /// Diese Überladung ist absichtlich nicht öffentlich, damit Start-Timer nur
    /// über Engine-internen Code ausgelöst werden können.
    /// </summary>
    internal void HandleTime(DateTime time, FlowzerTimerStartEvent? startEvent)
    {
        if (TryStartByTimerStartEvent(startEvent))
        {
            return;
        }

        HandleDueIntermediateTimerCatchEvents(time);
    }

    private void HandleDueIntermediateTimerCatchEvents(DateTime time)
    {
        var dueTimerTokens = ActiveTokens
            .Where(token => token.CurrentFlowNode is FlowzerIntermediateTimerCatchEvent)
            .Where(token => GetTimerDueDate(token) <= time)
            .ToArray();

        foreach (var token in dueTimerTokens)
        {
            token.State = FlowNodeState.Completing;
        }

        if (dueTimerTokens.Length > 0)
        {
            Run();
        }
    }

    /// <summary>
    /// Gibt den aktuellen (Sub-)Prozess-Token zurück.
    /// </summary>
    /// <param name="token"></param>
    /// <returns></returns>
    public Token GetProcessToken(Token token)
    {
        var processToken = token;
        while(processToken.CurrentBaseElement is not BPMN.Process.Process or SubProcess || processToken.ParentTokenId != null)
        {
            processToken = Tokens.Single(t => t.Id == processToken.ParentTokenId);
        }

        return processToken;
    }
    
    List<DateTime> ICatchHandler.ActiveTimers => Tokens
        .Where(token => token.State == FlowNodeState.Active)
        .SelectMany(GetActiveTimerDates)
        .Distinct()
        .ToList();

    List<TimerSubscriptionDescriptor> ICatchHandler.ActiveTimerSubscriptions => Tokens
        .Where(token => token.State == FlowNodeState.Active)
        .SelectMany(GetActiveTimerSubscriptionDescriptors)
        .ToList();

    private static IEnumerable<DateTime> GetActiveTimerDates(Token token)
    {
        if (token.CurrentFlowNode is FlowzerIntermediateTimerCatchEvent timerCatchEvent)
        {
            yield return GetTimerDueDate(token, timerCatchEvent);
        }
    }

    private static IEnumerable<TimerSubscriptionDescriptor> GetActiveTimerSubscriptionDescriptors(Token token)
    {
        if (token.CurrentFlowNode is FlowzerIntermediateTimerCatchEvent timerCatchEvent)
        {
            yield return new TimerSubscriptionDescriptor(
                GetTimerDueDate(token, timerCatchEvent),
                timerCatchEvent.Id,
                TimerSubscriptionKind.IntermediateCatchEvent,
                token.Id);
        }
    }

    private static bool CanBeTerminatedByCancellation(Token token)
    {
        return token.State is
            FlowNodeState.Ready or
            FlowNodeState.Active or
            FlowNodeState.Completing or
            FlowNodeState.WaitingForLoopEnd or
            FlowNodeState.Failing or
            FlowNodeState.Terminating;
    }

    private void FailInstanceBestEffort()
    {
        if (IsFinished)
        {
            return;
        }

        foreach (var token in Tokens.Where(CanBeFailedBestEffort))
        {
            token.State = FlowNodeState.Failed;
        }
    }

    private static bool CanBeFailedBestEffort(Token token) => CanBeTerminatedByCancellation(token);

    private bool TryStartByTimerStartEvent(FlowzerTimerStartEvent? startEvent)
    {
        if (startEvent == null || Tokens.Count != 1)
        {
            return false;
        }

        Tokens.Add(new Token
        {
            CurrentBaseElement = startEvent,
            ActiveBoundaryEvents = [],
            OutputData = new Variables(),
            State = FlowNodeState.Completing,
            ParentTokenId = MasterToken.Id,
            ProcessInstanceId = MasterToken.ProcessInstanceId,
        });
        Run();

        return true;
    }

    private static DateTime GetTimerDueDate(Token token)
    {
        return token.CurrentFlowNode is FlowzerIntermediateTimerCatchEvent timerCatchEvent
            ? GetTimerDueDate(token, timerCatchEvent)
            : throw new FlowzerRuntimeException($"Token {token.Id} wartet nicht auf ein Timer-Catch-Event.");
    }

    private static DateTime GetTimerDueDate(Token token, FlowzerIntermediateTimerCatchEvent timerCatchEvent)
    {
        return TimerDueDateCalculator.GetDueDate(
            token.LastStateChangeTime,
            timerCatchEvent.TimerDefinition,
            timerCatchEvent);
    }

    public List<Token> ActiveUserTasks()
    {
        return GetActiveUserTasks().ToList();
    }
}
