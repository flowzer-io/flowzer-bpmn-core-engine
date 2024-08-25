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
    
    public InstanceEngine(List<Token> tokens, bool allTokensIncluded = true)
    {
        if (tokens.SingleOrDefault(t => t.ParentTokenId == null) == null)
            throw new ArgumentException("Es muss mindestens ein (Master)Token vorhanden sein", nameof(tokens));
        Tokens = tokens;
    }
    
    public List<Token> Tokens { get; }
    public FlowzerConfig FlowzerConfig { get; } = FlowzerConfig.Default;

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
    
    public Task<IEnumerable<Escalation>> GetActiveEscalations()
    {
        throw new NotImplementedException();
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
        throw new NotImplementedException();
    }

    public void HandleError(string name, string errorCode, string? errorMessage = null, object? errorBody = null)
    {
        throw new NotImplementedException();
    }

    public void HandleTaskResult(Guid tokenId, Variables? data, string? userId = null)
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
    /// Abbruch der Instanz. Dabei werden alle Tokens terminiert und die bereits durchlaufenen Activities kompensiert.
    /// Subprozesse werden ebenfalls abgebrochen.
    /// </summary>
    public void Cancel()
    {
        throw new NotImplementedException();
    }

    // public Task<ProcessInstance> HandleTime(DateTime time)
    // {
    //     throw new NotImplementedException();
    // }

    List<DateTime> ICatchHandler.ActiveTimers => throw new NotImplementedException();
    
}