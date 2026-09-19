using BPMN.Infrastructure;
using BPMN.Process;
using Model;
using StorageSystem;

namespace WebApiEngine.BusinessLogic;

public partial class BpmnBusinessLogic
{
    /// <summary>
    /// BPMN-Fehler, die eine Call Activity an ihrem eigenen Knoten wirft. Sie sind Teil des
    /// Modellvertrags: Ein Error-Boundary an der Call Activity kann sie fangen.
    /// </summary>
    public static class CallActivityErrors
    {
        /// <summary>Kein deployter Workflow enthaelt einen Prozess mit dieser Kennung.</summary>
        public const string ProcessNotFound = "CALLED_PROCESS_NOT_FOUND";

        /// <summary>Der aufgerufene Vorgang wurde abgebrochen, statt zu enden.</summary>
        public const string ProcessCancelled = "CALLED_PROCESS_CANCELLED";

        /// <summary>Die Kette aufgerufener Prozesse ist zu tief; sehr wahrscheinlich eine Rekursion.</summary>
        public const string DepthExceeded = "CALLED_PROCESS_DEPTH_EXCEEDED";

        /// <summary>
        /// Der aufgerufene Vorgang ist gescheitert, ohne einen Fehlercode zu tragen — etwa an
        /// einem Error-End-Event ohne <c>errorRef</c>. Ein Fehler mit Code wird dagegen
        /// unveraendert weitergeworfen, wie BPMN 2.0 es vorsieht.
        /// </summary>
        public const string ProcessFailed = "CALLED_PROCESS_FAILED";
    }

    /// <summary>
    /// Wie tief Prozesse einander aufrufen duerfen. Die Grenze bricht eine Rekursion ab, statt
    /// die Transaktion und mit ihr den Server unbegrenzt weiterlaufen zu lassen.
    /// </summary>
    private const int MaximumCallActivityDepth = 10;

    /// <summary>
    /// Wie viele Aufrufe und Rueckmeldungen eine einzelne Mutation abarbeiten darf. Wie bei den
    /// Nachrichten bricht die Grenze den Vorgang ab, statt den Aufrufer haengen zu lassen.
    /// </summary>
    private const int MaximumCallActivityStepsPerMutation = 100;

    /// <summary>
    /// Fuehrt alles weiter, was durch das Speichern einer Instanz in Gang kommt: ausstehende
    /// Aufrufe starten, ausgehende Nachrichten zustellen und das Ende eines aufgerufenen
    /// Vorgangs an seinen Aufrufer melden. Alles laeuft in der Transaktion und unter der Sperre
    /// des Aufrufers; scheitert ein Schritt, scheitert die ganze Mutation.
    /// </summary>
    /// <returns>Die weitergelaufenen Instanzen ohne die Ausgangsinstanz.</returns>
    private async Task<IReadOnlyList<InstanceContext>> RunProcessExchange(
        ITransactionalStorage storageSystem,
        InstanceContext source)
    {
        var steps = 0;
        var live = new Dictionary<Guid, InstanceContext> { [source.Instance.InstanceId] = source };
        // Ein Ende wird genau einmal gemeldet. Ohne diese Spur meldete eine Instanz, die
        // mehrfach durch die Schlange laeuft, denselben Abschluss erneut an ihren Aufrufer.
        var reportedEndings = new HashSet<Guid>();
        var pending = new Queue<InstanceContext>([source]);

        while (pending.TryDequeue(out var current))
        {
            foreach (var call in current.Instance.TakePendingCallActivities())
            {
                RequireBudget(++steps);
                var child = await StartCalledProcess(storageSystem, current, call, live);
                if (child is null)
                {
                    // Der Aufruf ist mit einem BPMN-Fehler an seinem Knoten gescheitert; die
                    // aufrufende Instanz ist dadurch selbst weitergelaufen.
                    pending.Enqueue(current);
                    continue;
                }

                live[child.Instance.InstanceId] = child;
                pending.Enqueue(child);
            }

            // Nachrichten koennen einen Empfaenger beenden, und ein beendeter Empfaenger kann
            // selbst ein aufgerufener Vorgang sein. Deshalb laeuft die Zustellung in derselben
            // Schlange wie die Aufrufe und nicht daneben.
            var advanced = new List<InstanceContext>();
            await DeliverOutgoingMessages(storageSystem, current, advanced);
            foreach (var target in advanced)
            {
                live.TryAdd(target.Instance.InstanceId, target);
                pending.Enqueue(target);
            }

            if (current.Instance.IsFinished && reportedEndings.Add(current.Instance.InstanceId))
            {
                RequireBudget(++steps);
                var caller = await ContinueCallingInstance(storageSystem, current, live);
                if (caller is not null)
                {
                    live.TryAdd(caller.Instance.InstanceId, caller);
                    pending.Enqueue(caller);
                }
            }
        }

        var touched = live.Values
            .Where(context => context.Instance.InstanceId != source.Instance.InstanceId)
            .ToArray();
        foreach (var context in touched)
        {
            await PersistInstance(storageSystem, context.Instance, context.RelatedDefinitionId,
                context.DefinitionId, context.ProcessId);
        }

        return touched;
    }

    private static void RequireBudget(int steps)
    {
        if (steps > MaximumCallActivityStepsPerMutation)
        {
            throw new InvalidOperationException(
                $"More than {MaximumCallActivityStepsPerMutation} call activity steps were processed in one mutation. "
                + "The processes involved most likely call each other without end.");
        }
    }

    /// <summary>
    /// Startet die Kindinstanz eines ausstehenden Aufrufs: aktuell deployte Version desjenigen
    /// Workflows, dessen Prozess die gesuchte Kennung traegt.
    /// </summary>
    /// <returns>
    /// Die gestartete Kindinstanz, oder <c>null</c>, wenn der Aufruf mit einem BPMN-Fehler an
    /// seinem eigenen Knoten gescheitert ist.
    /// </returns>
    private async Task<InstanceContext?> StartCalledProcess(
        ITransactionalStorage storageSystem,
        InstanceContext caller,
        PendingCallActivity call,
        IReadOnlyDictionary<Guid, InstanceContext> live)
    {
        if (await CallDepthOf(storageSystem, caller, live) >= MaximumCallActivityDepth)
        {
            caller.Instance.ThrowBpmnError(call.TokenId, CallActivityErrors.DepthExceeded,
                $"The chain of called processes is deeper than {MaximumCallActivityDepth} levels.");
            return null;
        }

        var called = await FindDeployedProcess(storageSystem, call.ProcessId);
        if (called is null)
        {
            caller.Instance.ThrowBpmnError(call.TokenId, CallActivityErrors.ProcessNotFound,
                $"No deployed workflow contains a process with the id \"{call.ProcessId}\".");
            return null;
        }

        var instance = new ProcessEngine(called.Process).StartProcess(call.Variables);
        // Herkunft und Rechte gehoeren an den Master-Token: Sie ueberleben damit jeden
        // Speicher- und Ladevorgang, ohne in den Prozessvariablen zu landen.
        instance.MasterToken.CallingInstanceId = caller.Instance.InstanceId;
        instance.MasterToken.CallingTokenId = call.TokenId;
        // Wer den Vorgang angestossen hat, soll ihn durchgaengig sehen koennen. Aufgaben im
        // Kind folgen davon unberuehrt ihrem eigenen Zuweisungsmodell.
        instance.MasterToken.Initiator = caller.Instance.MasterToken.Initiator;

        caller.Instance.BindCalledInstance(call.TokenId, instance.InstanceId);

        return new InstanceContext(instance, called.RelatedDefinitionId, called.DefinitionId, called.Process.Id);
    }

    /// <summary>
    /// Meldet das Ende eines aufgerufenen Vorgangs an die wartende Call Activity seines
    /// Aufrufers. Ein regulaeres Ende liefert die Ausgabevariablen; ein Abbruch und ein
    /// gescheiterter Vorgang werden zu einem BPMN-Fehler an der Call Activity.
    /// </summary>
    /// <returns>Die weitergelaufene aufrufende Instanz, sonst <c>null</c>.</returns>
    private async Task<InstanceContext?> ContinueCallingInstance(
        ITransactionalStorage storageSystem,
        InstanceContext child,
        IReadOnlyDictionary<Guid, InstanceContext> live)
    {
        var master = child.Instance.MasterToken;
        if (master.CallingInstanceId is not { } callerInstanceId || master.CallingTokenId is not { } callerTokenId)
        {
            return null;
        }

        var caller = await LoadForMutation(storageSystem, callerInstanceId, live);
        // Der Aufrufer kann inzwischen selbst abgebrochen oder gescheitert sein — etwa weil er
        // gerade diesen Kindvorgang mit abgebrochen hat. Dann wartet niemand mehr.
        if (caller is null || !caller.Instance.GetWaitingCallActivities().Any(token => token.Id == callerTokenId))
        {
            return null;
        }

        switch (child.Instance.State)
        {
            case ProcessInstanceState.Completed:
                caller.Instance.CompleteCallActivity(callerTokenId, master.Variables);
                break;

            // Ein Terminate-End-Event beendet den aufgerufenen Prozess regulaer; nur ein
            // Abbruch von aussen ist ein Fehlerfall fuer den Aufrufer.
            case ProcessInstanceState.Terminated when !child.Instance.WasCancelled:
                caller.Instance.CompleteCallActivity(callerTokenId, master.Variables);
                break;

            case ProcessInstanceState.Terminated:
                caller.Instance.ThrowBpmnError(callerTokenId, CallActivityErrors.ProcessCancelled,
                    $"The called process instance {child.Instance.InstanceId} was cancelled.");
                break;

            case ProcessInstanceState.Failed:
                caller.Instance.ThrowBpmnError(callerTokenId,
                    child.Instance.FailureErrorCode ?? CallActivityErrors.ProcessFailed,
                    child.Instance.FailureReason);
                break;

            default:
                return null;
        }

        return caller;
    }

    /// <summary>
    /// Bricht die noch laufenden Kindinstanzen eines abgebrochenen Vorgangs mit ab — rekursiv
    /// und best effort, in derselben Transaktion. Ein Kind, das weiterliefe, arbeitete fuer
    /// einen Vorgang, den es nicht mehr gibt.
    /// </summary>
    private async Task CancelCalledInstances(ITransactionalStorage storageSystem, Guid instanceId)
    {
        var all = (await storageSystem.InstanceStorage.GetAllInstances()).ToArray();
        var pending = new Queue<Guid>([instanceId]);
        var cancelled = new HashSet<Guid> { instanceId };

        while (pending.TryDequeue(out var currentId))
        {
            foreach (var child in all.Where(candidate => candidate.ParentInstanceId == currentId))
            {
                if (!cancelled.Add(child.InstanceId) || child.IsFinished)
                {
                    continue;
                }

                await storageSystem.InstanceStorage.LockForMutation(child.InstanceId);
                var instance = new InstanceEngine(child.Tokens) { InstanceId = child.InstanceId };
                instance.Cancel();
                await PersistInstance(storageSystem, instance, child.metaDefinitionId, child.DefinitionId,
                    child.ProcessId);
                pending.Enqueue(child.InstanceId);
            }
        }
    }

    /// <summary>
    /// Wie viele Ebenen aufgerufener Prozesse ueber dieser Instanz liegen. Die aeusserste
    /// Instanz hat die Tiefe 1; die Kette wird hoechstens bis zur Grenze verfolgt.
    /// </summary>
    private static async Task<int> CallDepthOf(
        ITransactionalStorage storageSystem,
        InstanceContext instance,
        IReadOnlyDictionary<Guid, InstanceContext> live)
    {
        var depth = 1;
        var callerId = instance.Instance.MasterToken.CallingInstanceId;
        var seen = new HashSet<Guid> { instance.Instance.InstanceId };

        while (callerId is { } currentId && depth < MaximumCallActivityDepth && seen.Add(currentId))
        {
            depth++;
            callerId = live.TryGetValue(currentId, out var known)
                ? known.Instance.MasterToken.CallingInstanceId
                : (await TryGetInstance(storageSystem, currentId))?.ParentInstanceId;
        }

        return depth;
    }

    /// <summary>
    /// Die laufende Instanz aus diesem Vorgang, sonst frisch geladen und gesperrt. Ein erneutes
    /// Laden aus der Ablage verwuerfe den Fortschritt, den sie in dieser Mutation schon hat.
    /// </summary>
    private static async Task<InstanceContext?> LoadForMutation(
        ITransactionalStorage storageSystem,
        Guid instanceId,
        IReadOnlyDictionary<Guid, InstanceContext> live)
    {
        if (live.TryGetValue(instanceId, out var known))
        {
            return known;
        }

        await storageSystem.InstanceStorage.LockForMutation(instanceId);
        var stored = await TryGetInstance(storageSystem, instanceId);
        if (stored is null)
        {
            return null;
        }

        var instance = new InstanceEngine(stored.Tokens) { InstanceId = stored.InstanceId };

        return new InstanceContext(instance, stored.metaDefinitionId, stored.DefinitionId, stored.ProcessId);
    }

    private static async Task<ProcessInstanceInfo?> TryGetInstance(
        ITransactionalStorage storageSystem, Guid instanceId)
    {
        try { return await storageSystem.InstanceStorage.GetProcessInstance(instanceId); }
        catch (Exception exception) when (exception is FileNotFoundException or KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Der deployte Prozess mit einer bestimmten <c>bpmn:process/@id</c>.</summary>
    /// <param name="Process">Der Prozess, wie er in der deployten Version steht.</param>
    private sealed record CalledProcess(Process Process, string RelatedDefinitionId, Guid DefinitionId);

    /// <summary>
    /// Sucht den aufzurufenden Prozess in der jeweils <b>aktuell deployten</b> Version jedes
    /// Workflows. Der Zielprozess wird absichtlich erst hier gesucht und nicht beim Deployment
    /// des Aufrufers: Er darf spaeter entstehen, und welche Version gilt, entscheidet der
    /// Zeitpunkt des Aufrufs.
    /// </summary>
    private static async Task<CalledProcess?> FindDeployedProcess(
        ITransactionalStorage storageSystem, string processId)
    {
        var metaDefinitions = await storageSystem.DefinitionStorage.GetAllMetaDefinitions();
        foreach (var metaDefinition in metaDefinitions.OrderBy(entry => entry.DefinitionId, StringComparer.Ordinal))
        {
            if (metaDefinition.DeployedId is not { } deployedId)
            {
                continue;
            }

            Definitions model;
            try { model = ModelParser.ParseModel(await storageSystem.DefinitionStorage.GetBinary(deployedId)); }
            // Eine nicht mehr lesbare Version darf die Suche nicht abbrechen; ein anderer
            // Workflow kann denselben Prozess weiterhin sauber anbieten.
            catch (Exception exception) when (exception is FileNotFoundException or KeyNotFoundException)
            {
                continue;
            }

            var process = model.GetProcesses()
                .FirstOrDefault(candidate => candidate.IsExecutable && candidate.Id == processId);
            if (process is not null)
            {
                return new CalledProcess(process, metaDefinition.DefinitionId, deployedId);
            }
        }

        return null;
    }
}
