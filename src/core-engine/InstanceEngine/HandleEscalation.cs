namespace core_engine;

/// <summary>
/// Eskalation ist der freundliche Bruder des Fehlers: Sie meldet nach aussen, dass etwas eine
/// Entscheidung auf hoeherer Ebene braucht — ohne den laufenden Pfad abzubrechen und ohne die
/// Instanz scheitern zu lassen. Findet sich kein Faenger, verfaellt sie.
/// </summary>
public partial class InstanceEngine
{
    private readonly List<UnhandledEscalation> _unhandledEscalations = [];

    /// <summary>
    /// Eine Eskalation, die keinen Faenger gefunden hat. Sie bricht nichts ab; der Eintrag ist
    /// der Hinweis fuer die Diagnose, dass ein Modell hier auf niemanden trifft.
    /// </summary>
    public sealed record UnhandledEscalation(string FlowNodeId, string? EscalationCode);

    /// <summary>Die Eskalationen dieser Instanz, die niemand gefangen hat.</summary>
    public IReadOnlyList<UnhandledEscalation> UnhandledEscalations => _unhandledEscalations;

    /// <summary>
    /// Die Eskalationen, die diese Instanz im Moment fangen kann — aus Boundary-Events an
    /// laufenden Subprozessen und aus den scharfen Startereignissen ihrer Event-Subprozesse.
    /// </summary>
    public IEnumerable<Escalation> ActiveCatchEscalations
    {
        get
        {
            var fromBoundaries = Tokens
                .Where(token => token.State == FlowNodeState.Active && token.CurrentBaseElement is SubProcess)
                .SelectMany(GetAttachedBoundaryEvents)
                .OfType<FlowzerBoundaryEscalationEvent>()
                .Select(boundaryEvent => boundaryEvent.Escalation);

            var fromEventSubProcesses = GetArmedEventSubProcessStarts()
                .Select(entry => entry.StartEvent)
                .OfType<FlowzerEscalationStartEvent>()
                .Select(startEvent => startEvent.Escalation);

            return fromBoundaries.Concat(fromEventSubProcesses)
                .Where(escalation => escalation is not null)
                .Select(escalation => escalation!)
                .DistinctBy(escalation => escalation.EscalationCode)
                .ToArray();
        }
    }

    /// <summary>
    /// Meldet eine Eskalation von aussen in diese Instanz — der Weg, auf dem eine
    /// Betriebsschnittstelle einen laufenden Vorgang eskalieren laesst. Gefangen wird sie wie
    /// jede andere: am naechsten Scope, der einen passenden Faenger hat.
    /// </summary>
    public void HandleEscalation(string escalationCode, Variables? escalationData = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(escalationCode);

        if (IsFinished) return;

        RaiseEscalation(MasterToken, escalationCode, escalationData);
        Run();
    }

    /// <summary>
    /// Traegt eine Eskalation von ihrem Ursprung nach aussen.
    ///
    /// An jedem Scope faengt zuerst sein eigener Event-Subprozess — er liegt <i>innerhalb</i> des
    /// Scopes und ist damit naeher am Ursprung als das Boundary-Event, das <i>aussen</i> am
    /// Subprozess haengt. Erst danach kommt das Boundary an die Reihe.
    ///
    /// Ohne Faenger verfaellt die Eskalation. Anders als beim Fehler scheitert die Instanz
    /// dadurch ausdruecklich <b>nicht</b>: Der Prozess laeuft weiter, und der Vorgang bleibt als
    /// <see cref="UnhandledEscalations"/> nachvollziehbar.
    /// </summary>
    internal void RaiseEscalation(Token originToken, string? escalationCode, Variables? escalationData = null)
    {
        var scopeToken = originToken;
        while (true)
        {
            if (scopeToken.CurrentBaseElement is BPMN.Process.Process or SubProcess
                && TryCatchEscalationByEventSubProcess(scopeToken, escalationCode, escalationData))
            {
                return;
            }

            // Am Prozess haengt kein Boundary-Event; dort endet die Kette.
            if (scopeToken.ParentTokenId is not { } parentTokenId) break;

            if (scopeToken.CurrentBaseElement is SubProcess
                && TryCatchEscalationByBoundary(scopeToken, escalationCode, escalationData))
            {
                return;
            }

            scopeToken = Tokens.Single(token => token.Id == parentTokenId);
        }

        _unhandledEscalations.Add(new UnhandledEscalation(
            originToken.CurrentBaseElement.Id,
            string.IsNullOrWhiteSpace(escalationCode) ? null : escalationCode));
    }

    /// <summary>
    /// Versucht, die Eskalation an einem Boundary-Event des Subprozesses zu fangen. Ein Boundary
    /// mit passendem Code hat Vorrang; eines ohne <c>escalationRef</c> faengt jede Eskalation.
    ///
    /// Unterbrechend zieht den Subprozess samt allem darin zurueck; nicht unterbrechend laesst
    /// ihn weiterlaufen und oeffnet den Eskalationspfad zusaetzlich.
    /// </summary>
    private bool TryCatchEscalationByBoundary(Token scopeToken, string? escalationCode, Variables? escalationData)
    {
        var candidates = GetAttachedBoundaryEvents(scopeToken).OfType<FlowzerBoundaryEscalationEvent>().ToArray();

        var boundaryEvent = candidates.FirstOrDefault(candidate =>
                                candidate.Escalation is { } escalation
                                && escalation.EscalationCode == escalationCode)
                            ?? candidates.FirstOrDefault(candidate => candidate.Escalation is null);

        if (boundaryEvent is null) return false;

        if (boundaryEvent.CancelActivity)
        {
            WithdrawScope(scopeToken);
        }

        Tokens.Add(new Token
        {
            CurrentBaseElement = boundaryEvent,
            ActiveBoundaryEvents = [],
            OutputData = escalationData ?? new Variables(),
            State = FlowNodeState.Completing,
            ParentTokenId = scopeToken.ParentTokenId,
            ProcessInstanceId = scopeToken.ProcessInstanceId,
        });

        return true;
    }
}
