using System.Dynamic;
using BPMN.Common;
using BPMN.Foundation;
using Microsoft.Extensions.Logging.Abstractions;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Setzt eine laufende Instanz innerhalb derselben Version an eine andere Stelle und korrigiert
/// ihre Variablen. Siehe <c>docs/INSTANCE-MODIFICATION.md</c>: nur Betriebsrecht, nur bewusst,
/// nur eine Instanz je Anfrage.
///
/// Der Unterschied zur Migration steckt in einem Satz: Der Eingriff zieht den Token zurueck und
/// legt am Ziel einen neuen an. Aufgaben und Auftraege der verlassenen Stelle verschwinden
/// deshalb samt Kennung — das Speichern erledigt das von selbst, weil
/// <see cref="SaveUserTasks"/> und <see cref="SaveServiceTasks"/> sich am Tokenstand
/// ausrichten. Eine eigene Umbindelogik gibt es hier bewusst nicht.
/// </summary>
public partial class BpmnBusinessLogic
{
    /// <summary>
    /// Obergrenze je Anfrage. Der Eingriff laeuft unter der Engine-Sperre; eine unbegrenzte
    /// Liste hielte alle uebrigen Schreibvorgaenge beliebig lange auf.
    /// </summary>
    private const int MaxModificationMoves = 100;

    /// <summary>Obergrenze der Variablenangaben einer Anfrage, aus demselben Grund.</summary>
    private const int MaxModificationVariables = 200;

    /// <summary>
    /// Trockenlauf: Was wuerde der Eingriff tun, und was ginge dabei verloren? Veraendert nichts
    /// und nimmt deshalb weder die Engine-Sperre noch eine Instanzsperre.
    ///
    /// Eine leere Anfrage ist ausdruecklich erlaubt: Sie beantwortet, welche Schritte warten und
    /// welche Knoten als Ziel in Frage kommen — genau das, was die Oberflaeche braucht, bevor
    /// jemand etwas auswaehlt.
    /// </summary>
    public async Task<InstanceModificationPreview> PreviewInstanceModification(
        Guid instanceId,
        InstanceModificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (RequestProblem(request) is { } message)
            return InstanceModificationPreview.Rejected(
                InstanceModificationRequestStatus.RequestTooLarge, instanceId, message);

        using var storage = storageProvider.GetTransactionalStorage();
        var instance = await FindInstance(storage, instanceId);
        if (instance is null)
            return InstanceModificationPreview.Rejected(
                InstanceModificationRequestStatus.UnknownInstance,
                instanceId,
                $"The process instance \"{instanceId}\" was not found.");

        if (instance.IsFinished)
            return InstanceModificationPreview.Rejected(
                InstanceModificationRequestStatus.InstanceNotRunning,
                instanceId,
                $"The process instance \"{instanceId}\" is already finished and cannot be modified.");

        // Der Plan arbeitet auf einer eigenen Engine ueber demselben Tokenstand. Der Trockenlauf
        // veraendert daran nichts; die Engine ist hier nur der Weg zum Modell und zu den Tokens.
        var engine = new InstanceEngine(instance.Tokens) { InstanceId = instance.InstanceId };
        var plan = InstanceModification.Plan(engine, request);
        var notices = await CollectStorageNotices(storage, instance, plan);

        return new InstanceModificationPreview(
            InstanceModificationRequestStatus.Accepted,
            null,
            instanceId,
            plan.IsApplicable,
            [.. plan.Problems.Select(ToFinding)],
            [.. plan.Notices.Select(ToFinding), .. notices],
            DescribeSteps(engine),
            DescribeTargets(engine));
    }

    /// <summary>
    /// Fuehrt den Eingriff aus — unter der Engine-Sperre, der Instanzsperre und in einer
    /// Transaktion. Der Plan wird innerhalb der Transaktion neu gefasst; der Trockenlauf ist
    /// eine Auskunft, keine Zusage.
    /// </summary>
    public async Task<InstanceModificationOutcome> ModifyInstance(
        Guid instanceId,
        InstanceModificationRequest request,
        Guid modifiedByUserId)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Vor der Engine-Sperre: Eine unbrauchbare Anfrage haelt keinen anderen Schreibvorgang auf.
        if (RequestProblem(request) is { } message)
            return InstanceModificationOutcome.Rejected(
                InstanceModificationRequestStatus.RequestTooLarge, instanceId, message);

        if (request.IsEmpty)
            return InstanceModificationOutcome.Rejected(
                InstanceModificationRequestStatus.NothingToDo,
                instanceId,
                "The request names neither a move nor a variable change.");

        await _engineMutationLock.WaitAsync();
        try
        {
            return await ModifyLockedInstance(instanceId, request, modifiedByUserId);
        }
        catch (Exception exception)
        {
            // Der Grund gehoert ins Protokoll, nicht in die Antwort: Er nennt Modell- und
            // Ablagedetails. Die Instanz ist unveraendert, weil ihre Transaktion offen blieb.
            (logger ?? NullLogger<BpmnBusinessLogic>.Instance).LogError(exception,
                "Modifying process instance {InstanceId} failed.", instanceId);
            return InstanceModificationOutcome.Rejected(
                InstanceModificationRequestStatus.ModificationFailed,
                instanceId,
                "The modification failed; the instance was left unchanged.",
                [new InstanceModificationFinding(
                    InstanceModificationCodes.ModificationFailed,
                    null,
                    null,
                    "The modification of this instance failed unexpectedly; it was left unchanged.")]);
        }
        finally
        {
            _engineMutationLock.Release();
        }
    }

    private async Task<InstanceModificationOutcome> ModifyLockedInstance(
        Guid instanceId,
        InstanceModificationRequest request,
        Guid modifiedByUserId)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        await storage.InstanceStorage.LockForMutation(instanceId);

        var instance = await FindInstance(storage, instanceId);
        if (instance is null)
            return InstanceModificationOutcome.Rejected(
                InstanceModificationRequestStatus.UnknownInstance,
                instanceId,
                $"The process instance \"{instanceId}\" was not found.");

        if (instance.IsFinished)
            return InstanceModificationOutcome.Rejected(
                InstanceModificationRequestStatus.InstanceNotRunning,
                instanceId,
                $"The process instance \"{instanceId}\" is already finished and cannot be modified.");

        var engine = new InstanceEngine(instance.Tokens) { InstanceId = instance.InstanceId };
        var plan = InstanceModification.Plan(engine, request);
        if (!plan.IsApplicable)
            // Ohne Commit bleibt die Instanz genau so liegen, wie sie war.
            return InstanceModificationOutcome.Rejected(
                InstanceModificationRequestStatus.NotApplicable,
                instanceId,
                "The modification cannot be applied to this instance.",
                [.. plan.Problems.Select(ToFinding)]);

        var notices = await CollectStorageNotices(storage, instance, plan);

        // Die Spur wird vor dem Eingriff gebaut: Danach traegt der Quell-Token zwar noch seinen
        // Knoten, aber die Zuordnung Quelle/Ziel ist nur hier vollstaendig beisammen.
        var record = DescribeModification(plan, modifiedByUserId);

        InstanceModification.Apply(plan);

        var modifications = new List<InstanceModificationRecord>(instance.Modifications) { record };
        await SaveInstance(
            storage, engine, instance.metaDefinitionId, instance.DefinitionId, instance.ProcessId,
            instance.Migrations, modifications: modifications);
        storage.CommitChanges();

        (logger ?? NullLogger<BpmnBusinessLogic>.Instance).LogInformation(
            "Modified process instance {InstanceId} of workflow {RelatedDefinitionId}: moved {MoveCount} step(s), "
            + "set {SetCount} and removed {RemovedCount} variable(s) by user {UserId}.",
            instanceId, instance.metaDefinitionId, record.Moves.Count,
            record.VariablesSet.Count, record.VariablesRemoved.Count, modifiedByUserId);

        return new InstanceModificationOutcome(
            InstanceModificationRequestStatus.Accepted,
            null,
            instanceId,
            true,
            [],
            [.. plan.Notices.Select(ToFinding), .. notices]);
    }

    /// <summary>
    /// Die Spur des Eingriffs — ohne Werte. Sie sagt, welcher Schritt von wo nach wo ging und
    /// welche Variablen angefasst wurden; was drinstand, steht in den Variablen selbst.
    /// </summary>
    private static InstanceModificationRecord DescribeModification(
        InstanceModificationPlan plan,
        Guid modifiedByUserId) => new(
        DateTimeOffset.UtcNow,
        modifiedByUserId,
        [.. plan.Moves.Select(move => new InstanceModificationMoveRecord(
            move.SourceToken.Id,
            move.SourceToken.CurrentBaseElement.Id,
            move.TargetFlowNode.Id))],
        [.. (plan.Request.VariablesToSet?.Keys ?? []).Order(StringComparer.Ordinal)],
        [.. (plan.Request.VariablesToRemove ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);

    /// <summary>
    /// Die Hinweise, die nur die Ablage kennt: ein privater Entwurf, der mit der verschwindenden
    /// Aufgabe verloren geht, und ein Auftrag, an dem gerade ein Worker arbeitet. Dass eine
    /// Aufgabe oder ein Auftrag ueberhaupt verfaellt, sagt bereits der Plan.
    /// </summary>
    private static async Task<IReadOnlyList<InstanceModificationFinding>> CollectStorageNotices(
        ITransactionalStorage storage,
        ProcessInstanceInfo instance,
        InstanceModificationPlan plan)
    {
        if (plan.Moves.Count == 0) return [];

        var notices = new List<InstanceModificationFinding>();
        var movedTokenIds = plan.Moves.Select(move => move.SourceToken.Id).ToHashSet();

        var tasks = (await storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId))
            .Where(task => movedTokenIds.Contains(task.Token.Id))
            .ToArray();
        foreach (var task in tasks)
        {
            // Eine Ablage ohne Entwurfsvertrag kann keinen Entwurf verlieren, den es nie gab.
            int drafts;
            try { drafts = await storage.UserTaskDraftStorage.CountForTask(task.Id); }
            catch (NotSupportedException) { continue; }
            if (drafts == 0) continue;

            notices.Add(new InstanceModificationFinding(
                InstanceModificationCodes.UserTaskDraftDiscarded,
                task.Token.Id,
                task.Token.CurrentFlowNode?.Id,
                "Private drafts of this user task are discarded together with the task."));
        }

        var now = DateTime.UtcNow;
        notices.AddRange((await storage.ServiceTaskStorage.GetJobs())
            .Where(job => job.ProcessInstanceId == instance.InstanceId
                          && movedTokenIds.Contains(job.TokenId)
                          && job.LockedBy is not null
                          && job.LockedUntil > now)
            .Select(job => new InstanceModificationFinding(
                InstanceModificationCodes.ServiceTaskJobInProgress,
                job.TokenId,
                job.FlowNodeId,
                "A worker currently holds the job of this service task; its result will be rejected.")));

        return notices;
    }

    /// <summary>Die wartenden Schritte der obersten Ebene, so wie die Bedienung sie vor sich hat.</summary>
    private static IReadOnlyList<InstanceModificationStep> DescribeSteps(InstanceEngine engine) =>
    [
        .. engine.Tokens
            .Where(token => token.ParentTokenId == engine.MasterToken.Id
                            && token.State == FlowNodeState.Active
                            && token.CurrentFlowNode is not null)
            .Select(token => new InstanceModificationStep(
                token.Id,
                token.CurrentFlowNode!.Id,
                Name(token.CurrentFlowNode),
                token.CurrentFlowNode.GetType().Name))
            .OrderBy(step => step.Name ?? step.FlowNodeId, StringComparer.Ordinal)
            .ThenBy(step => step.TokenId)
    ];

    /// <summary>
    /// Die angebotene Auswahl. Welche Knoten das sind, entscheidet die Engine — was nicht
    /// waehlbar ist, soll gar nicht erst zur Wahl stehen, und die Regel dafuer gibt es nur einmal.
    /// </summary>
    private static IReadOnlyList<InstanceModificationFlowNode> DescribeTargets(InstanceEngine engine) =>
    [
        .. InstanceModification.AllowedTargets(engine)
            .Select(flowNode => new InstanceModificationFlowNode(
                flowNode.Id, Name(flowNode), flowNode.GetType().Name))
    ];

    /// <summary>Ein Knoten ohne Namen traegt im Modell die leere Zeichenkette; nach aussen ist das nichts.</summary>
    private static string? Name(FlowNode flowNode) =>
        string.IsNullOrWhiteSpace(flowNode.Name) ? null : flowNode.Name;

    private static InstanceModificationFinding ToFinding(InstanceModificationProblem problem) =>
        new(problem.Code.ToString(), problem.TokenId, problem.FlowNodeId, problem.Message);

    private static InstanceModificationFinding ToFinding(InstanceModificationNotice notice) =>
        new(notice.Code.ToString(), notice.TokenId, notice.FlowNodeId, notice.Message);

    /// <summary>
    /// Prueft die Anfrage fuer sich allein und nennt den Grund, aus dem sie unbrauchbar ist.
    /// Inhaltliche Befunde — unbekannter Knoten, nicht verschiebbarer Schritt — gehoeren in den
    /// Plan und nicht hierher.
    /// </summary>
    private static string? RequestProblem(InstanceModificationRequest request)
    {
        if (request.Moves?.Count > MaxModificationMoves)
            return $"At most {MaxModificationMoves} steps can be moved in one request.";

        var variableCount = (request.VariablesToSet?.Count ?? 0) + (request.VariablesToRemove?.Count ?? 0);
        return variableCount > MaxModificationVariables
            ? $"At most {MaxModificationVariables} variables can be changed in one request."
            : null;
    }

    private static async Task<ProcessInstanceInfo?> FindInstance(ITransactionalStorage storage, Guid instanceId)
    {
        try { return await storage.InstanceStorage.GetProcessInstance(instanceId); }
        catch (Exception exception) when (exception is FileNotFoundException or KeyNotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Stabile Codes, die nicht aus der Engine stammen, sondern an dieser Schicht entstehen.</summary>
public static class InstanceModificationCodes
{
    /// <summary>Der Eingriff ist unerwartet gescheitert; die Instanz blieb unveraendert.</summary>
    public const string ModificationFailed = "ModificationFailed";

    /// <summary>Ein privater Entwurf der verschwindenden Aufgabe geht verloren.</summary>
    public const string UserTaskDraftDiscarded = "UserTaskDraftDiscarded";

    /// <summary>
    /// Ein Worker arbeitet gerade an einem Auftrag, der gleich verfaellt. Er meldet sein
    /// Ergebnis danach ins Leere; den Auftrag gibt es dann nicht mehr.
    /// </summary>
    public const string ServiceTaskJobInProgress = "ServiceTaskJobInProgress";
}
