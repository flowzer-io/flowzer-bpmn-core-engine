using core_engine.Extensions;

namespace core_engine;

/// <summary>
/// Event-Subprozesse sind die Ereignisfaenger eines Scopes: Solange der Prozess oder der
/// Subprozess laeuft, in dem sie stehen, ist ihr Startereignis scharf — genau wie ein
/// Boundary-Event an einer Aktivitaet. Deshalb tauchen ihre Nachrichten, Signale und Timer in
/// denselben Subscription-Listen auf wie die der Boundary-Events.
/// </summary>
public partial class InstanceEngine
{
    /// <summary>
    /// Ein scharfes Startereignis eines Event-Subprozesses samt dem Scope, an dem es haengt.
    /// </summary>
    internal sealed record ArmedEventSubProcessStart(
        Token ScopeToken,
        SubProcess EventSubProcess,
        StartEvent StartEvent);

    /// <summary>
    /// Alle derzeit scharfen Startereignisse von Event-Subprozessen.
    ///
    /// Nicht mehr scharf ist ein unterbrechendes Startereignis, sobald sein Event-Subprozess
    /// einmal gelaufen ist — es hat seinen Scope bereits unterbrochen. Dasselbe gilt in dieser
    /// Stufe fuer einen Timer-Start: Ein <c>timeCycle</c> an einem nicht unterbrechenden
    /// Event-Subprozess wuerde sonst bei jedem Schedulerlauf erneut ausloesen.
    /// </summary>
    internal IEnumerable<ArmedEventSubProcessStart> GetArmedEventSubProcessStarts()
    {
        foreach (var scopeToken in Tokens
                     .Where(token => token.State == FlowNodeState.Active
                                     && token.CurrentBaseElement is IFlowElementContainer)
                     .ToArray())
        {
            var container = (IFlowElementContainer)scopeToken.CurrentBaseElement;
            foreach (var eventSubProcess in container.GetEventSubProcesses())
            {
                var startEvent = eventSubProcess.GetEventSubProcessStartEvent();
                if (startEvent is null) continue;

                var hasRun = Tokens.Any(token => token.ParentTokenId == scopeToken.Id
                                                 && token.CurrentBaseElement.Id == eventSubProcess.Id);
                if (hasRun && (startEvent.FlowzerIsInterrupting || startEvent is FlowzerTimerStartEvent))
                {
                    continue;
                }

                yield return new ArmedEventSubProcessStart(scopeToken, eventSubProcess, startEvent);
            }
        }
    }

    /// <summary>
    /// Startet einen Event-Subprozess in seinem Scope.
    ///
    /// Unterbrechend heisst: Alles, was im Scope noch laeuft, wird zurueckgezogen — der Scope
    /// selbst bleibt bestehen, denn er beherbergt jetzt den Event-Subprozess. Nicht unterbrechend
    /// heisst: Der Event-Subprozess laeuft zusaetzlich, der Scope einfach weiter.
    ///
    /// Bewusst ohne <c>Run()</c>: Der Aufrufer entscheidet, ob er bereits im Lauf steckt.
    /// </summary>
    private void StartEventSubProcess(ArmedEventSubProcessStart armedStart, Variables? eventData)
    {
        if (armedStart.StartEvent.FlowzerIsInterrupting)
        {
            WithdrawScopeContents(armedStart.ScopeToken);
        }

        ApplyEventPayload(armedStart.ScopeToken, eventData);

        Tokens.Add(new Token
        {
            CurrentBaseElement = armedStart.EventSubProcess,
            ActiveBoundaryEvents = [],
            OutputData = new Variables(),
            State = FlowNodeState.Ready,
            ParentTokenId = armedStart.ScopeToken.Id,
            ProcessInstanceId = armedStart.ScopeToken.ProcessInstanceId,
        });
    }

    /// <summary>
    /// Zieht alles zurueck, was in einem Scope noch laeuft — den Scope-Token selbst aber nicht.
    /// Genau das unterscheidet ein unterbrechendes Startereignis von einem Error-Boundary: Dort
    /// verschwindet der ganze Subprozess, hier bleibt er als Gastgeber des Event-Subprozesses
    /// aktiv.
    /// </summary>
    private void WithdrawScopeContents(Token scopeToken)
    {
        var pending = new Queue<Guid>([scopeToken.Id]);
        while (pending.TryDequeue(out var parentTokenId))
        {
            foreach (var child in Tokens.Where(token => token.ParentTokenId == parentTokenId).ToArray())
            {
                pending.Enqueue(child.Id);
                if (CanBeWithdrawn(child))
                {
                    child.State = FlowNodeState.Withdrawn;
                }
            }
        }
    }

    /// <summary>
    /// Legt die Nutzdaten des ausloesenden Ereignisses in den Prozesskontext — auf demselben Weg,
    /// auf dem auch ein Boundary-Event seine Daten abliefert.
    /// </summary>
    private void ApplyEventPayload(Token scopeToken, Variables? eventData)
    {
        if (eventData is null) return;

        var variablesToken = GetProcessToken(scopeToken);
        variablesToken.Variables ??= new Variables();
        foreach (var (key, value) in eventData)
        {
            variablesToken.Variables.SetValue(key, value);
        }
    }

    /// <summary>
    /// Versucht, eine eintreffende Nachricht an ein Message-Start-Event eines Event-Subprozesses
    /// zuzustellen.
    /// </summary>
    private bool TryStartEventSubProcessByMessage(Message message, Variables data)
    {
        var armedStart = GetArmedEventSubProcessStarts()
            .Where(entry => entry.StartEvent is FlowzerMessageStartEvent)
            .FirstOrDefault(entry =>
                ((FlowzerMessageStartEvent)entry.StartEvent).MessageDefinition.Name == message.Name);

        if (armedStart is null) return false;

        StartEventSubProcess(armedStart, data);
        return true;
    }

    /// <summary>
    /// Versucht, ein eintreffendes Signal an ein Signal-Start-Event eines Event-Subprozesses
    /// zuzustellen.
    /// </summary>
    private bool TryStartEventSubProcessBySignal(string signalName, Variables data)
    {
        var armedStart = GetArmedEventSubProcessStarts()
            .Where(entry => entry.StartEvent is FlowzerSignalStartEvent)
            .FirstOrDefault(entry => ((FlowzerSignalStartEvent)entry.StartEvent).Signal.Name == signalName);

        if (armedStart is null) return false;

        StartEventSubProcess(armedStart, data);
        return true;
    }

    /// <summary>
    /// Versucht, einen BPMN-Fehler an ein Error-Start-Event eines Event-Subprozesses dieses
    /// Scopes zuzustellen. Ein Fehlerstart ohne <c>errorRef</c> faengt jeden Code; ein Start mit
    /// passendem Code hat Vorrang.
    /// </summary>
    private bool TryCatchErrorByEventSubProcess(Token scopeToken, string? errorCode, Variables? errorData)
    {
        var candidates = GetArmedEventSubProcessStarts()
            .Where(entry => entry.ScopeToken.Id == scopeToken.Id && entry.StartEvent is FlowzerErrorStartEvent)
            .ToArray();

        var armedStart = candidates.FirstOrDefault(entry =>
            ((FlowzerErrorStartEvent)entry.StartEvent).Error?.ErrorCode == errorCode
            && ((FlowzerErrorStartEvent)entry.StartEvent).Error is not null);

        if (armedStart is null)
        {
            armedStart = candidates.FirstOrDefault(entry =>
                ((FlowzerErrorStartEvent)entry.StartEvent).Error is null);
        }

        if (armedStart is null) return false;

        StartEventSubProcess(armedStart, errorData);
        return true;
    }

    /// <summary>
    /// Versucht, eine Eskalation an ein Escalation-Start-Event eines Event-Subprozesses dieses
    /// Scopes zuzustellen.
    /// </summary>
    private bool TryCatchEscalationByEventSubProcess(Token scopeToken, string? escalationCode,
        Variables? escalationData)
    {
        var candidates = GetArmedEventSubProcessStarts()
            .Where(entry => entry.ScopeToken.Id == scopeToken.Id && entry.StartEvent is FlowzerEscalationStartEvent)
            .ToArray();

        var armedStart = candidates.FirstOrDefault(entry =>
            ((FlowzerEscalationStartEvent)entry.StartEvent).Escalation is { } escalation
            && escalation.EscalationCode == escalationCode);

        if (armedStart is null)
        {
            armedStart = candidates.FirstOrDefault(entry =>
                ((FlowzerEscalationStartEvent)entry.StartEvent).Escalation is null);
        }

        if (armedStart is null) return false;

        StartEventSubProcess(armedStart, escalationData);
        return true;
    }
}
