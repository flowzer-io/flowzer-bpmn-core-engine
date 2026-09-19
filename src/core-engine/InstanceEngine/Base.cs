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
    
    public IEnumerable<Token> GetActiveUserTasks() => Tokens
        .Where(token => token is { CurrentFlowNode: UserTask, State: FlowNodeState.Active });

    private Token GetToken(Guid tokenId)
    {
        return Tokens.Single(token => token.Id == tokenId);
    }

    /// <summary>
    /// Die FlowElements des Containers, in dem dieses Token laeuft — des Prozesses oder des
    /// Subprozesses, dem es untersteht.
    ///
    /// Wer stattdessen <see cref="Process"/> liest, sieht nur die oberste Ebene: Ein Gateway
    /// innerhalb eines Subprozesses faende seine eigenen Sequenzfluesse dort nicht.
    /// </summary>
    public FlowzerList<FlowElement> GetContainerFlowElements(Token token)
    {
        var current = token;
        while (current.ParentTokenId is { } parentTokenId)
        {
            current = Tokens.Single(candidate => candidate.Id == parentTokenId);
            if (current.CurrentBaseElement is IFlowElementContainer container)
            {
                return container.FlowElements;
            }
        }

        return Process.FlowElements;
    }

    /// <summary>
    /// Tokens, die auf einen externen Worker warten. Seit Fähigkeitsvertrag 6 sind das nicht
    /// mehr nur Service-Tasks: Ein Send-Task oder ein sendendes Nachrichtenereignis mit
    /// Auftragstyp wartet genauso. Ein sendendes Element ohne Auftragstyp korreliert die
    /// Engine selbst und steht hier deshalb nie.
    /// </summary>
    public IEnumerable<Token> GetActiveServiceTasks() => Tokens
        .Where(token => token.State == FlowNodeState.Active
            && token.CurrentFlowNode is IFlowzerWorkerTask { Implementation.Length: > 0 });

    public IEnumerable<Token> GetActiveTasks() => Tokens
        .Where(token => token.State == FlowNodeState.Active);
    
    public void HandleError(string name, string errorCode, string? errorMessage = null, object? errorBody = null)
    {
        FailInstanceBestEffort();
    }

    public void HandleTaskResult(Guid tokenId, Variables? data, Guid? userId = null)
    {
        var token = GetToken(tokenId);
        if (token.State != FlowNodeState.Active)
        {
            throw new FlowzerRuntimeException("Token ist nicht aktiv");
        }

        // Eingaben können aus einem Formular oder einem Worker stammen. Weder dürfen sie
        // den Akteur bestimmen noch darf das Ergänzen des Legacy-Feldes die Eingabe ändern
        // (sie kann dieselbe Referenz wie ein anderer Variablenscope besitzen).
        Variables outputData = new();
        var outputValues = (IDictionary<string, object?>)outputData;
        foreach (var entry in (IDictionary<string, object?>?)data ?? new Dictionary<string, object?>())
        {
            outputValues[entry.Key] = entry.Value;
        }

        // Kompatibilität für bestehende BPMN-Mappings; maßgeblich ist CompletedByUserId.
        outputValues["UserId"] = userId;
        token.CompletedByUserId = userId;
        token.OutputData = outputData;
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
    /// <summary>
    /// Ob diese Instanz durch einen Abbruch von aussen geendet hat. Ein Terminate-End-Event
    /// hinterlaesst denselben Zustand, ist fachlich aber ein regulaeres Ende — eine aufrufende
    /// Instanz behandelt beide Faelle deshalb unterschiedlich. Gilt fuer den laufenden
    /// Engine-Vorgang; der Abbruch wird in derselben Transaktion weitergereicht.
    /// </summary>
    public bool WasCancelled { get; private set; }

    public void Cancel()
    {
        WasCancelled = true;
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
    /// Führt fällige Intermediate-Timer-Catch-Events und Boundary-Timer weiter.
    /// </summary>
    public void HandleTime(DateTime time)
    {
        HandleDueTimers(time);
    }

    /// <summary>
    /// Führt fällige Timer-Start-, Intermediate-Timer-Catch- oder Boundary-Timer-Events weiter.
    /// Diese Überladung ist absichtlich nicht öffentlich, damit Start-Timer nur
    /// über Engine-internen Code ausgelöst werden können.
    /// </summary>
    internal void HandleTime(DateTime time, FlowzerTimerStartEvent? startEvent)
    {
        if (TryStartByTimerStartEvent(startEvent))
        {
            return;
        }

        HandleDueTimers(time);
    }

    private void HandleDueTimers(DateTime time)
    {
        var requiresRun = false;

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
            requiresRun = true;
        }

        var dueBoundaryTimers = ActiveTokens
            .Select(token => new
            {
                Token = token,
                DueBoundaryEvents = token.ActiveBoundaryEvents
                    .OfType<FlowzerBoundaryTimerEvent>()
                    .Where(boundaryTimer => GetBoundaryTimerDueDate(token, boundaryTimer) <= time)
                    .ToArray()
            })
            .Where(entry => entry.DueBoundaryEvents.Length > 0)
            .ToArray();

        foreach (var dueBoundaryTimer in dueBoundaryTimers)
        {
            foreach (var boundaryTimerEvent in dueBoundaryTimer.DueBoundaryEvents)
            {
                dueBoundaryTimer.Token.ActiveBoundaryEvents.Remove(boundaryTimerEvent);

                if (boundaryTimerEvent.CancelActivity)
                {
                    dueBoundaryTimer.Token.State = FlowNodeState.Withdrawn;
                }

                Tokens.Add(new Token
                {
                    CurrentBaseElement = boundaryTimerEvent,
                    ActiveBoundaryEvents = [],
                    OutputData = new Variables(),
                    State = FlowNodeState.Completing,
                    ParentTokenId = dueBoundaryTimer.Token.ParentTokenId,
                    ProcessInstanceId = dueBoundaryTimer.Token.ProcessInstanceId,
                });
                requiresRun = true;

                if (boundaryTimerEvent.CancelActivity)
                {
                    break;
                }
            }
        }

        // Ein Timer-Start eines Event-Subprozesses laeuft ab dem Beginn seines Scopes. Er
        // verhaelt sich wie ein Boundary-Timer am Scope und wird hier auch so ausgeloest.
        var dueEventSubProcessTimers = GetArmedEventSubProcessStarts()
            .Where(entry => entry.StartEvent is FlowzerTimerStartEvent)
            .Where(entry => GetEventSubProcessTimerDueDate(entry) <= time)
            .ToArray();

        foreach (var dueEventSubProcessTimer in dueEventSubProcessTimers)
        {
            StartEventSubProcess(dueEventSubProcessTimer, null);
            requiresRun = true;
        }

        if (requiresRun)
        {
            Run();
        }
    }

    private static DateTime GetEventSubProcessTimerDueDate(ArmedEventSubProcessStart armedStart)
    {
        var timerStartEvent = (FlowzerTimerStartEvent)armedStart.StartEvent;

        return TimerDueDateCalculator.GetDueDate(
            armedStart.ScopeToken.LastStateChangeTime,
            timerStartEvent.TimerDefinition,
            timerStartEvent);
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
        .Concat(GetEventSubProcessTimerSubscriptionDescriptors().Select(descriptor => descriptor.DueAt))
        .Distinct()
        .ToList();

    List<TimerSubscriptionDescriptor> ICatchHandler.ActiveTimerSubscriptions => Tokens
        .Where(token => token.State == FlowNodeState.Active)
        .SelectMany(GetActiveTimerSubscriptionDescriptors)
        .Concat(GetEventSubProcessTimerSubscriptionDescriptors())
        .ToList();

    /// <summary>
    /// Die Timer-Startereignisse scharfer Event-Subprozesse. Sie erscheinen bewusst als
    /// <see cref="TimerSubscriptionKind.BoundaryEvent"/>: Fachlich sind sie genau das — ein
    /// Ereignis, das an einem laufenden Scope haengt und ihn unterbrechen kann.
    /// </summary>
    private IEnumerable<TimerSubscriptionDescriptor> GetEventSubProcessTimerSubscriptionDescriptors() =>
        GetArmedEventSubProcessStarts()
            .Where(entry => entry.StartEvent is FlowzerTimerStartEvent)
            .Select(entry => new TimerSubscriptionDescriptor(
                GetEventSubProcessTimerDueDate(entry),
                entry.StartEvent.Id,
                TimerSubscriptionKind.BoundaryEvent,
                entry.ScopeToken.Id));

    private static IEnumerable<DateTime> GetActiveTimerDates(Token token)
    {
        if (token.CurrentFlowNode is FlowzerIntermediateTimerCatchEvent timerCatchEvent)
        {
            yield return GetTimerDueDate(token, timerCatchEvent);
        }

        foreach (var boundaryTimerEvent in token.ActiveBoundaryEvents.OfType<FlowzerBoundaryTimerEvent>())
        {
            yield return GetBoundaryTimerDueDate(token, boundaryTimerEvent);
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

        foreach (var boundaryTimerEvent in token.ActiveBoundaryEvents.OfType<FlowzerBoundaryTimerEvent>())
        {
            yield return new TimerSubscriptionDescriptor(
                GetBoundaryTimerDueDate(token, boundaryTimerEvent),
                boundaryTimerEvent.Id,
                TimerSubscriptionKind.BoundaryEvent,
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

    private static DateTime GetBoundaryTimerDueDate(Token token, FlowzerBoundaryTimerEvent boundaryTimerEvent)
    {
        return TimerDueDateCalculator.GetDueDate(
            token.LastStateChangeTime,
            boundaryTimerEvent.TimerDefinition,
            boundaryTimerEvent);
    }

    public List<Token> ActiveUserTasks()
    {
        return GetActiveUserTasks().ToList();
    }

    public List<Token> ActiveServiceTasks()
    {
        return GetActiveServiceTasks().ToList();
    }
}
