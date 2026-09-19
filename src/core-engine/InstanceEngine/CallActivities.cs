using core_engine.Exceptions;

namespace core_engine;

/// <summary>
/// Ein Aufruf, den die Engine bereitgestellt hat und der noch gestartet werden muss.
/// </summary>
/// <param name="TokenId">Das wartende Token der aufrufenden Instanz.</param>
/// <param name="ProcessId">Die <c>bpmn:process/@id</c> des Prozesses, der gestartet werden soll.</param>
/// <param name="Variables">Die Startvariablen der Kindinstanz, bereits nach Modell gebunden.</param>
public sealed record PendingCallActivity(Guid TokenId, string ProcessId, Variables Variables);

public partial class InstanceEngine
{
    private readonly List<PendingCallActivity> _pendingCallActivities = [];

    /// <summary>
    /// Die Tokens, für die dieser Engine-Lauf bereits einen Aufruf bereitgestellt hat — auch
    /// dann, wenn er inzwischen abgeholt wurde. Über Lauf und Neuladen hinweg trägt
    /// <see cref="Token.CalledInstanceId"/> dieselbe Aussage dauerhaft.
    /// </summary>
    private readonly HashSet<Guid> _requestedCallActivityTokenIds = [];

    /// <summary>
    /// Die Aufrufe, die diese Instanz seit dem letzten Abholen bereitgestellt hat.
    ///
    /// Wie bei den ausgehenden Nachrichten kennt die Engine keine anderen Instanzen: Sie legt
    /// den Aufruf nur bereit; starten muss ihn der Aufrufer (in der Web-API die Geschäftslogik,
    /// in derselben Transaktion wie das Speichern der Instanz). Die Liste gehört zu genau einem
    /// Engine-Lauf und wird nicht mit dem Tokenstand persistiert — was bereits gestartet wurde,
    /// steht am Token selbst (<see cref="Token.CalledInstanceId"/>).
    /// </summary>
    public IReadOnlyList<PendingCallActivity> PendingCallActivities => _pendingCallActivities;

    /// <summary>
    /// Gibt die ausstehenden Aufrufe heraus und leert die Liste, damit derselbe Aufruf nicht
    /// zweimal eine Kindinstanz erzeugt.
    /// </summary>
    public IReadOnlyList<PendingCallActivity> TakePendingCallActivities()
    {
        var calls = _pendingCallActivities.ToArray();
        _pendingCallActivities.Clear();

        return calls;
    }

    /// <summary>
    /// Die Tokens, die auf einen aufgerufenen Prozess warten. Sie warten wie ein Service-Task,
    /// sind aber kein Auftrag für einen Worker: Fortschritt bringt allein das Ende des Kindes.
    /// </summary>
    public IEnumerable<Token> GetWaitingCallActivities() => Tokens
        .Where(token => token.State == FlowNodeState.Active && token.CurrentFlowNode is CallActivity);

    /// <summary>
    /// Merkt sich am wartenden Token, welche Instanz dieser Schritt gestartet hat. Erst damit
    /// gilt der Aufruf als ausgeführt; ein erneuter Engine-Lauf fordert ihn dann nicht noch einmal an.
    /// </summary>
    public void BindCalledInstance(Guid tokenId, Guid calledInstanceId)
    {
        var token = GetToken(tokenId);
        RequireWaitingCallActivity(token);
        token.CalledInstanceId = calledInstanceId;
    }

    /// <summary>
    /// Schließt ein wartendes Call-Activity-Token mit dem Ergebnis des aufgerufenen Prozesses ab.
    ///
    /// Was davon in den Elternprozess übernommen wird, entscheidet allein das Modell: Mit
    /// <c>zeebe:ioMapping</c>-Ausgang genau die zugeordneten Werte, sonst bei
    /// <c>propagateAllChildVariables</c> der ganze Variablenstand des Kindes — und ohne beides
    /// bewusst nichts.
    /// </summary>
    public void CompleteCallActivity(Guid tokenId, Variables? childVariables)
    {
        var token = GetToken(tokenId);
        var callActivity = RequireWaitingCallActivity(token);

        var propagates = callActivity.FlowzerPropagateAllChildVariables
            || callActivity.OutputMappings?.Count > 0;
        token.OutputData = propagates ? childVariables ?? new Variables() : new Variables();
        token.State = FlowNodeState.Completing;

        Run();
    }

    /// <summary>
    /// Stellt den Aufruf eines erreichten Call-Activity-Tokens bereit.
    ///
    /// Die Startvariablen des Kindes folgen dem Modell: <c>propagateAllParentVariables</c> gibt
    /// den Prozesskontext weiter, ein <c>zeebe:ioMapping</c>-Eingang legt seine gebundenen Werte
    /// darüber. Ohne beides startet das Kind ohne Variablen, statt ungefragt den ganzen Vorgang
    /// mitzunehmen.
    /// </summary>
    internal void RequestCallActivity(Token token, CallActivity callActivity)
    {
        if (!_requestedCallActivityTokenIds.Add(token.Id))
        {
            return;
        }

        var variables = new Variables();
        var target = (IDictionary<string, object?>)variables;

        if (callActivity.FlowzerPropagateAllParentVariables)
        {
            foreach (var (key, value) in GetProcessToken(token).Variables ?? new Variables())
            {
                target[key] = value;
            }
        }

        // PrepareInputData hat die Eingangszuordnung beim Erreichen des Knotens bereits
        // ausgewertet; ohne Zuordnung bleibt Token.Variables null.
        foreach (var (key, value) in token.Variables ?? new Variables())
        {
            target[key] = value;
        }

        _pendingCallActivities.Add(new PendingCallActivity(
            token.Id, callActivity.FlowzerCalledElementProcessId, variables));
    }

    private static CallActivity RequireWaitingCallActivity(Token token)
    {
        if (token.State != FlowNodeState.Active || token.CurrentFlowNode is not CallActivity callActivity)
        {
            throw new FlowzerRuntimeException(
                $"Token {token.Id} wartet nicht an einer Call Activity.");
        }

        return callActivity;
    }
}
