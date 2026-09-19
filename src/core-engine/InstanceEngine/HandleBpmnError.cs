using core_engine.Exceptions;

namespace core_engine;

public partial class InstanceEngine
{
    /// <summary>
    /// Begruendung, warum diese Instanz fachlich gescheitert ist. Sie wird genau dann gesetzt,
    /// wenn ein BPMN-Fehler bis zur Prozessebene durchschlaegt und dort niemand ihn faengt.
    /// Null bei jeder anderen Instanz.
    /// </summary>
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Der Code des ungefangenen BPMN-Fehlers, an dem diese Instanz gescheitert ist. Eine
    /// aufrufende Instanz wirft genau diesen Code an ihrer Call Activity erneut — so, wie es
    /// BPMN 2.0 für einen gescheiterten aufgerufenen Prozess vorsieht. Null bei jeder anderen
    /// Instanz und bei einem Fehler, der ohne Code geworfen wurde.
    /// </summary>
    public string? FailureErrorCode { get; private set; }

    /// <summary>
    /// Loest einen BPMN-Fehler am Token einer wartenden Aktivitaet aus — der Weg, auf dem ein
    /// externer Worker einen fachlichen Fehler statt eines Ergebnisses meldet.
    ///
    /// Aufgeloest wird wie bei jedem anderen BPMN-Fehler: zuerst ein Error-Boundary an der
    /// Aktivitaet selbst, dann an den umschliessenden Scopes, sonst scheitert die Instanz.
    /// </summary>
    public void ThrowBpmnError(Guid tokenId, string errorCode, string? errorMessage = null, Variables? errorData = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);

        var token = GetToken(tokenId);
        if (token.State != FlowNodeState.Active)
        {
            throw new FlowzerRuntimeException("Token ist nicht aktiv");
        }

        RaiseBpmnError(token, errorCode, errorMessage, catchAtOriginActivity: true, errorData);
        Run();
    }

    /// <summary>
    /// Traegt einen BPMN-Fehler von seinem Ursprung nach aussen, bis ihn ein Error-Boundary
    /// faengt. <paramref name="catchAtOriginActivity"/> ist nur fuer Fehler wahr, die in einer
    /// Aktivitaet entstehen: Ein Error-End-Event kann nicht von einem Boundary an sich selbst
    /// gefangen werden, ein Service-Task dagegen sehr wohl.
    /// </summary>
    internal void RaiseBpmnError(Token originToken, string? errorCode, string? errorMessage, bool catchAtOriginActivity,
        Variables? errorData = null)
    {
        if (catchAtOriginActivity && TryCatchErrorAt(originToken, errorCode, errorData))
        {
            return;
        }

        var scopeToken = originToken;
        while (scopeToken.ParentTokenId is { } parentTokenId)
        {
            scopeToken = Tokens.Single(token => token.Id == parentTokenId);

            // Am Master-Token haengt kein Boundary: Ein Fehler, der die Prozessebene erreicht,
            // ist per BPMN nicht mehr fangbar.
            if (scopeToken.ParentTokenId is null)
            {
                break;
            }

            if (TryCatchErrorAt(scopeToken, errorCode, errorData))
            {
                return;
            }
        }

        FailureErrorCode = string.IsNullOrWhiteSpace(errorCode) ? null : errorCode;
        FailureReason = BuildUnhandledErrorReason(originToken, errorCode, errorMessage);
        FailInstanceBestEffort();
    }

    /// <summary>
    /// Versucht, den Fehler an genau einem Scope zu fangen. Gelingt es, wird der ganze Scope
    /// unterbrochen und das Boundary-Token folgt seinem ausgehenden Sequenzfluss.
    /// </summary>
    private bool TryCatchErrorAt(Token scopeToken, string? errorCode, Variables? errorData)
    {
        var boundaryEvent = FindErrorBoundaryEvent(scopeToken, errorCode);
        if (boundaryEvent is null)
        {
            return false;
        }

        WithdrawScope(scopeToken);

        // Bewusst ohne Run(): Der Aufrufer entscheidet, ob er bereits im Lauf steckt. Die
        // Lauf-Schleife nimmt ein Completing-Token beim naechsten Schritt ohnehin auf.
        Tokens.Add(new Token
        {
            CurrentBaseElement = boundaryEvent,
            ActiveBoundaryEvents = [],
            // Die Daten des Fehlers gehoeren auf den Fehlerpfad: Beim Abschluss des
            // Boundary-Tokens wandern sie wie jedes andere Ergebnis in den Prozesskontext.
            OutputData = errorData ?? new Variables(),
            State = FlowNodeState.Completing,
            ParentTokenId = scopeToken.ParentTokenId,
            ProcessInstanceId = scopeToken.ProcessInstanceId,
        });

        return true;
    }

    /// <summary>
    /// Ein Boundary mit passendem Fehlercode hat Vorrang; eines ohne <c>errorRef</c> faengt
    /// jeden Fehler und dient damit als Auffangpfad.
    /// </summary>
    private FlowzerBoundaryErrorEvent? FindErrorBoundaryEvent(Token activityToken, string? errorCode)
    {
        var candidates = GetAttachedBoundaryEvents(activityToken).OfType<FlowzerBoundaryErrorEvent>().ToArray();

        return candidates.FirstOrDefault(candidate =>
                   candidate.Error is not null && candidate.Error.ErrorCode == errorCode)
               ?? candidates.FirstOrDefault(candidate => candidate.Error is null);
    }

    /// <summary>
    /// Die Boundary-Events einer Aktivitaet. <see cref="Token.ActiveBoundaryEvents"/> fuehrt sie
    /// nur fuer Aktivitaeten der obersten Prozessebene; das Modell des umschliessenden Containers
    /// ist deshalb die verlaessliche Quelle und wird ergaenzend gelesen.
    /// </summary>
    private IEnumerable<BoundaryEvent> GetAttachedBoundaryEvents(Token activityToken)
    {
        var nodeId = activityToken.CurrentBaseElement.Id;
        var container = GetScopeToken(activityToken)?.CurrentBaseElement as IFlowElementContainer;
        var fromModel = container?.FlowElements
            .OfType<BoundaryEvent>()
            .Where(boundaryEvent => boundaryEvent.AttachedToRef.Id == nodeId) ?? [];

        return activityToken.ActiveBoundaryEvents.Concat(fromModel).DistinctBy(boundaryEvent => boundaryEvent.Id);
    }

    /// <summary>
    /// Der naechste umschliessende (Sub-)Prozess-Token. Null fuer das Master-Token, das selbst
    /// die aeusserste Ebene ist.
    /// </summary>
    private Token? GetScopeToken(Token token)
    {
        var current = token;
        while (current.ParentTokenId is { } parentTokenId)
        {
            current = Tokens.Single(candidate => candidate.Id == parentTokenId);
            if (current.CurrentBaseElement is BPMN.Process.Process or SubProcess)
            {
                return current;
            }
        }

        return null;
    }

    /// <summary>
    /// Zieht einen unterbrochenen Scope samt allem, was darin noch laeuft, zurueck. Damit
    /// verschwinden auch dessen Message-, Signal- und Timer-Subscriptions aus dem Tokenstand,
    /// weil diese ausschliesslich an aktiven Tokens haengen.
    /// </summary>
    private void WithdrawScope(Token scopeToken)
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

        if (CanBeWithdrawn(scopeToken))
        {
            scopeToken.State = FlowNodeState.Withdrawn;
        }
    }

    private static bool CanBeWithdrawn(Token token) => token.State is
        FlowNodeState.Ready or
        FlowNodeState.Active or
        FlowNodeState.Completing or
        FlowNodeState.WaitingForLoopEnd or
        FlowNodeState.Failing;

    private static string BuildUnhandledErrorReason(Token originToken, string? errorCode, string? errorMessage)
    {
        var code = string.IsNullOrWhiteSpace(errorCode) ? "(without error code)" : $"'{errorCode}'";
        var reason = $"Unhandled BPMN error {code} at '{originToken.CurrentBaseElement.Id}'.";

        return string.IsNullOrWhiteSpace(errorMessage) ? reason : $"{reason} {errorMessage}";
    }
}
