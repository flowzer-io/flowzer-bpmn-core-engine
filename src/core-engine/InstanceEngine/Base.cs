using core_engine.Exceptions;

namespace core_engine;

public partial class InstanceEngine(ProcessInstance instance)
{
    public ProcessInstance Instance { get; } = instance;

    private FlowzerConfig FlowzerConfig { get; } = FlowzerConfig.Default;

    public IEnumerable<Token> ActiveTokens => Instance.Tokens.Where(token => token.State == FlowNodeState.Active);

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



    public List<SignalDefinition> GetActiveCatchSignals()
    {
        throw new NotImplementedException();
    }

    public Task<ProcessInstance> HandleTime(DateTime time)
    {
        throw new NotImplementedException();
    }

    public Task<ProcessInstance> HandleSignal(string signalName, object? signalData = null)
    {
        throw new NotImplementedException();
    }


}