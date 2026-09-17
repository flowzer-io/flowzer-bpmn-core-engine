using BPMN.Common;
using BPMN.Flowzer.Events;
using BPMN.Foundation;
using BPMN.HumanInteraction;
using BPMN.Infrastructure;
using BPMN.Process;
using Microsoft.Extensions.Logging.Abstractions;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Hebt laufende Instanzen bewusst auf die aktuell deployte Version desselben Workflows.
/// Siehe <c>docs/INSTANCE-MIGRATION.md</c>: nur deckungsgleiche Instanzen, nur nach vorn,
/// nur mit Betriebsrecht — und jede Instanz fuer sich.
/// </summary>
public partial class BpmnBusinessLogic
{
    /// <summary>
    /// Obergrenze je Anfrage. Der Umzug laeuft unter der Engine-Sperre; eine unbegrenzte Liste
    /// hielte alle uebrigen Schreibvorgaenge beliebig lange auf.
    /// </summary>
    private const int MaxMigrationInstances = 200;

    /// <summary>
    /// Trockenlauf: Was wuerde der Umzug tun, und was ginge dabei verloren? Veraendert nichts
    /// und nimmt deshalb weder die Engine-Sperre noch eine Instanzsperre.
    /// </summary>
    public async Task<InstanceMigrationPreview> PreviewInstanceMigration(IReadOnlyCollection<Guid> instanceIds)
    {
        ArgumentNullException.ThrowIfNull(instanceIds);

        using var storage = storageProvider.GetTransactionalStorage();
        var (status, message, resolved) = await ResolveMigrationRequest(storage, instanceIds);
        if (resolved is null) return InstanceMigrationPreview.Rejected(status, message!);

        var items = new List<InstanceMigrationPreviewItem>(resolved.Instances.Count);
        foreach (var instance in resolved.Instances)
        {
            var evaluation = await EvaluateInstance(storage, resolved, instance);
            var draftsReadable = !evaluation.Problems.Any(problem =>
                problem.Code == InstanceMigrationCodes.DraftStorageNotSupported);
            items.Add(new InstanceMigrationPreviewItem(
                instance.InstanceId,
                evaluation.Migratable,
                evaluation.Problems,
                await CollectNotices(storage, resolved, instance, draftsReadable)));
        }

        return new InstanceMigrationPreview(
            InstanceMigrationRequestStatus.Accepted,
            null,
            resolved.RelatedDefinitionId,
            resolved.SourceDefinitionId,
            resolved.SourceDefinition?.Version,
            resolved.Target.Id,
            resolved.Target.Version,
            items);
    }

    /// <summary>
    /// Migriert die genannten Instanzen auf <paramref name="targetDefinitionId"/>. Jede Instanz
    /// bekommt ihre eigene Storage-Transaktion: Scheitert eine, bleiben die uebrigen Ergebnisse
    /// bestehen. Der Plan wird innerhalb der Transaktion neu gefasst; der Trockenlauf ist eine
    /// Auskunft, keine Zusage.
    /// </summary>
    public async Task<InstanceMigrationOutcome> MigrateInstances(
        IReadOnlyCollection<Guid> instanceIds,
        Guid targetDefinitionId,
        Guid migratedByUserId)
    {
        ArgumentNullException.ThrowIfNull(instanceIds);

        await _engineMutationLock.WaitAsync();
        try
        {
            ResolvedMigration resolved;
            using (var storage = storageProvider.GetTransactionalStorage())
            {
                var request = await ResolveMigrationRequest(storage, instanceIds);
                if (request.Resolved is null)
                    return InstanceMigrationOutcome.Rejected(request.Status, request.Message!);

                // Die Zielversion steht im Auftrag; ist inzwischen eine andere deployt, meint
                // der Aufrufer etwas anderes als die API taete. Dann lieber nichts tun.
                if (request.Resolved.Target.Id != targetDefinitionId)
                    return InstanceMigrationOutcome.Rejected(
                        InstanceMigrationRequestStatus.TargetVersionChanged,
                        $"The deployed version of workflow \"{request.Resolved.RelatedDefinitionId}\" is no longer "
                        + $"\"{targetDefinitionId}\". Repeat the preview and confirm the current version.");

                resolved = request.Resolved;
            }

            var results = new List<InstanceMigrationResultItem>(resolved.Instances.Count);
            foreach (var instance in resolved.Instances)
                results.Add(await MigrateSingleInstance(resolved, instance.InstanceId, migratedByUserId));

            return new InstanceMigrationOutcome(
                InstanceMigrationRequestStatus.Accepted,
                null,
                resolved.Target.Id,
                resolved.Target.Version,
                results);
        }
        finally
        {
            _engineMutationLock.Release();
        }
    }

    private async Task<InstanceMigrationResultItem> MigrateSingleInstance(
        ResolvedMigration resolved,
        Guid instanceId,
        Guid migratedByUserId)
    {
        try
        {
            using var storage = storageProvider.GetTransactionalStorage();
            await storage.InstanceStorage.LockForMutation(instanceId);
            var instance = await storage.InstanceStorage.GetProcessInstance(instanceId);

            // Die Engine-Sperre gilt nur in diesem Prozess. Ein zweiter API-Prozess kann seit
            // der Pruefung des Auftrags deployt haben; die Zielversion wird deshalb in der
            // Transaktion dieser Instanz erneut gelesen und muss die bestaetigte sein.
            var deployed = await storage.DefinitionStorage.GetDeployedDefinition(instance.metaDefinitionId);
            if (deployed?.Id != resolved.Target.Id)
                return new InstanceMigrationResultItem(instanceId, false, [
                    new InstanceMigrationFinding(
                        InstanceMigrationCodes.TargetVersionChanged,
                        null,
                        "Another version of this workflow was deployed in the meantime; "
                        + "the instance was left unchanged.")
                ]);

            var evaluation = await EvaluateInstance(storage, resolved, instance);
            if (evaluation.Plan is null || !evaluation.Migratable)
                // Ohne Commit bleibt die Instanz genau so liegen, wie sie war.
                return new InstanceMigrationResultItem(instanceId, false, evaluation.Problems);

            var sourceDefinitionId = instance.DefinitionId;
            var engine = new InstanceEngine(InstanceMigration.Apply(evaluation.Plan))
            {
                InstanceId = instance.InstanceId
            };

            await RebindUserTasks(storage, resolved, instance, evaluation.Plan);
            await RebindServiceTaskJobs(storage, resolved, instance);

            var migrations = new List<InstanceMigrationRecord>(instance.Migrations)
            {
                new(sourceDefinitionId, resolved.Target.Id, DateTimeOffset.UtcNow, migratedByUserId)
            };
            await SaveInstance(
                storage, engine, instance.metaDefinitionId, resolved.Target.Id, instance.ProcessId, migrations);
            storage.CommitChanges();

            (logger ?? NullLogger<BpmnBusinessLogic>.Instance).LogInformation(
                "Migrated process instance {InstanceId} of workflow {RelatedDefinitionId} from definition "
                + "{SourceDefinitionId} to {TargetDefinitionId} by user {UserId}.",
                instanceId, instance.metaDefinitionId, sourceDefinitionId, resolved.Target.Id, migratedByUserId);

            return new InstanceMigrationResultItem(instanceId, true, []);
        }
        catch (Exception exception)
        {
            // Der Grund gehoert ins Protokoll, nicht in die Antwort: Er nennt Modell- und
            // Ablagedetails. Die Instanz ist unveraendert, weil ihre Transaktion offen blieb.
            (logger ?? NullLogger<BpmnBusinessLogic>.Instance).LogError(exception,
                "Migrating process instance {InstanceId} to definition {TargetDefinitionId} failed.",
                instanceId, resolved.Target.Id);
            return new InstanceMigrationResultItem(instanceId, false, [
                new InstanceMigrationFinding(
                    InstanceMigrationCodes.MigrationFailed,
                    null,
                    "The migration of this instance failed; the instance was left unchanged.")
            ]);
        }
    }

    /// <summary>
    /// Bindet die bestehenden Aufgaben-Subscriptions auf die Zielversion um und entscheidet je
    /// Aufgabe ueber ihre privaten Entwuerfe. Muss vor dem Speichern der Instanz laufen: Der
    /// Aufgabenabgleich lehnt eine Subscription ab, deren Version nicht zur Instanz passt.
    ///
    /// Jede Aufgabe wird vorher gesperrt — derselbe Aufgaben-Lock, den Entwurfsspeichern,
    /// Uebernahme und Abschluss nehmen. Sonst koennte ein Speichervorgang, der die alte Bindung
    /// gelesen hat, seinen Entwurf danach an die Quellversion haengen; der naechste Abruf
    /// scheiterte dann an der Bindungspruefung. Die Reihenfolge Instanz- vor Aufgabensperre ist
    /// dieselbe wie beim Abschluss einer Aufgabe; das Entwurfsspeichern nimmt nur die
    /// Aufgabensperre. Damit kann kein Zyklus entstehen.
    /// </summary>
    private async Task RebindUserTasks(
        ITransactionalStorage storage,
        ResolvedMigration resolved,
        ProcessInstanceInfo instance,
        InstanceMigrationPlan plan)
    {
        var tasks = (await storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).ToArray();
        foreach (var task in tasks)
        {
            // Eine Aufgabe, die es nicht mehr gibt, wird auch nicht umgebunden: Das Speichern
            // legte sie sonst ueber die Subscription wieder an.
            if (!await LockTaskIfSupported(storage, task.Id)) continue;

            var keepsDraft = plan.WaitingTokens.FirstOrDefault(token => token.Id == task.Token.Id) is { } token
                             && IsFormBindingIdentical(resolved, token, plan.TargetFlowNodeOf(token.Id));

            task.DefinitionId = resolved.Target.Id;
            await storage.SubscriptionStorage.AddUserTaskSubscription(task);
            await ApplyDraftDecision(storage, task.Id, resolved.Target.Id, keepsDraft);
        }
    }

    /// <summary>
    /// Wie im Entwurfsdienst: Eine Ablage ohne Aufgaben-Lebenszyklus laeuft im Einzelprozess
    /// und ist durch die Engine-Sperre geschuetzt.
    /// </summary>
    private static async Task<bool> LockTaskIfSupported(ITransactionalStorage storage, Guid userTaskId)
    {
        try { return await storage.UserTaskLifecycleStorage.LockTask(userTaskId); }
        catch (NotSupportedException) { return true; }
    }

    /// <summary>
    /// Kein Auffangen von <see cref="NotSupportedException"/>: Eine Ablage, die Entwuerfe fuehrt,
    /// aber diesen Vertrag nicht kennt, liesse sie sonst an der Quellversion zurueck — der
    /// naechste Abruf scheiterte dann an der Bindungspruefung. Der Fall ist vorher geprueft;
    /// kommt er hier dennoch an, scheitert die Instanz ohne Commit.
    /// </summary>
    private static async Task ApplyDraftDecision(
        ITransactionalStorage storage,
        Guid userTaskId,
        Guid targetDefinitionId,
        bool keepsDraft)
    {
        if (keepsDraft) await storage.UserTaskDraftStorage.RebindAllForTask(userTaskId, targetDefinitionId);
        else await storage.UserTaskDraftStorage.DeleteAllForTask(userTaskId);
    }

    /// <summary>
    /// Haengt die Auftraege der Instanz an die Zielversion. Kennung, Sperre und Versuche bleiben:
    /// Ein Worker, der gerade arbeitet, meldet sein Ergebnis unveraendert zurueck. Das erledigt
    /// die Ablage in einem Schritt; ein Lese-Aendern-Schreiben von hier aus wuerde eine
    /// zwischenzeitlich erteilte Lease ueberschreiben.
    /// </summary>
    private static Task RebindServiceTaskJobs(
        ITransactionalStorage storage,
        ResolvedMigration resolved,
        ProcessInstanceInfo instance) =>
        storage.ServiceTaskStorage.RebindJobsOfInstance(instance.InstanceId, resolved.Target.Id);

    /// <summary>
    /// Der Befund zu einer Instanz samt allem, was die Ablage dazu beitragen muss. Wird vor
    /// jedem Schreibvorgang ermittelt, damit eine Instanz entweder ganz oder gar nicht umzieht.
    /// </summary>
    private static async Task<InstanceEvaluation> EvaluateInstance(
        ITransactionalStorage storage,
        ResolvedMigration resolved,
        ProcessInstanceInfo instance)
    {
        var evaluation = EvaluatePlan(resolved, instance);
        if (await DraftStorageProblem(storage, instance) is not { } problem) return evaluation;
        return evaluation with { Migratable = false, Problems = [.. evaluation.Problems, problem] };
    }

    /// <summary>
    /// Fragt die Entwurfsablage, bevor irgendetwas geschrieben wird. Eine Ablage, die den
    /// Entwurfsvertrag nicht kennt, kann die Entwuerfe der Aufgaben nicht mitnehmen; die Instanz
    /// bliebe halb umgezogen zurueck. Der Trockenlauf darf sie deshalb auch nicht als migrierbar
    /// ausweisen.
    /// </summary>
    private static async Task<InstanceMigrationFinding?> DraftStorageProblem(
        ITransactionalStorage storage,
        ProcessInstanceInfo instance)
    {
        // Ohne offene Aufgabe gibt es keinen Entwurf, der mitziehen muesste.
        if ((await storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).FirstOrDefault() is not { } task)
            return null;

        try
        {
            await storage.UserTaskDraftStorage.CountForTask(task.Id);
            return null;
        }
        catch (NotSupportedException)
        {
            return new InstanceMigrationFinding(
                InstanceMigrationCodes.DraftStorageNotSupported,
                null,
                "The configured storage adapter cannot move user-task drafts to the deployed version.");
        }
    }

    private static InstanceEvaluation EvaluatePlan(ResolvedMigration resolved, ProcessInstanceInfo instance)
    {
        if (instance.DefinitionId == resolved.Target.Id)
            return new InstanceEvaluation(false, [
                new InstanceMigrationFinding(
                    InstanceMigrationCodes.AlreadyOnTargetVersion,
                    null,
                    "The instance already runs on the deployed version.")
            ], null);

        if (resolved.ProcessOf(instance.ProcessId) is not { } targetProcess)
            return new InstanceEvaluation(false, [
                new InstanceMigrationFinding(
                    nameof(InstanceMigrationProblemCode.ProcessChanged),
                    null,
                    $"The deployed version contains no process \"{instance.ProcessId}\".")
            ], null);

        var plan = InstanceMigration.Plan(instance.Tokens, targetProcess);
        return new InstanceEvaluation(plan.IsMigratable, [.. plan.Problems.Select(ToFinding)], plan);
    }

    private sealed record InstanceEvaluation(
        bool Migratable,
        IReadOnlyList<InstanceMigrationFinding> Problems,
        InstanceMigrationPlan? Plan);

    private static InstanceMigrationFinding ToFinding(InstanceMigrationProblem problem) =>
        new(problem.Code.ToString(), problem.FlowNodeId, problem.Message);

    /// <summary>
    /// Die Hinweise nennen, was der Umzug mitnimmt, ohne es zu verhindern. Sie werden auch fuer
    /// eine nicht migrierbare Instanz ermittelt, soweit der Zielknoten ueberhaupt bekannt ist.
    /// </summary>
    private static async Task<IReadOnlyList<InstanceMigrationFinding>> CollectNotices(
        ITransactionalStorage storage,
        ResolvedMigration resolved,
        ProcessInstanceInfo instance,
        bool draftsReadable)
    {
        if (resolved.ProcessOf(instance.ProcessId) is not { } targetProcess) return [];

        var notices = new List<InstanceMigrationFinding>();
        var tasks = (await storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).ToArray();
        var targetFlowNodes = TargetFlowNodesById(targetProcess);

        foreach (var token in WaitingTokens(instance))
        {
            if (token.CurrentFlowNode is not { } flowNode) continue;
            if (!targetFlowNodes.TryGetValue(flowNode.Id, out var targetFlowNode)) continue;

            // Der Umzug setzt den Knoten der Zielversion ein und schaltet dessen Boundary-Events
            // scharf, laesst aber den Zeitstempel des Tokens stehen. Eine seit Tagen wartende
            // Aufgabe kann dadurch sofort in einen Timer der Zielversion laufen.
            if (HasTimerInTarget(targetProcess, targetFlowNode))
                notices.Add(new InstanceMigrationFinding(
                    InstanceMigrationCodes.TimerRecalculated,
                    targetFlowNode.Id,
                    "Timers at this node are computed with the target version's duration from the "
                    + "original start of waiting and may be due immediately."));

            if (flowNode is not UserTask) continue;
            if (IsFormBindingIdentical(resolved, token, targetFlowNode)) continue;

            notices.Add(new InstanceMigrationFinding(
                InstanceMigrationCodes.UserTaskFormChanged,
                flowNode.Id,
                "The user task is bound to a different form in the deployed version."));

            // Ohne lesbare Entwurfsablage bleibt offen, ob es ueberhaupt einen Entwurf gibt; die
            // Instanz ist dann ohnehin schon als nicht migrierbar ausgewiesen.
            if (!draftsReadable) continue;
            if (tasks.FirstOrDefault(task => task.Token.Id == token.Id) is not { } task) continue;
            if (await storage.UserTaskDraftStorage.CountForTask(task.Id) == 0) continue;
            notices.Add(new InstanceMigrationFinding(
                InstanceMigrationCodes.UserTaskDraftDiscarded,
                flowNode.Id,
                "Private drafts of this user task will be discarded because its form changes."));
        }

        notices.AddRange(await LockedJobNotices(storage, instance));
        return notices;
    }

    private static async Task<IReadOnlyList<InstanceMigrationFinding>> LockedJobNotices(
        ITransactionalStorage storage,
        ProcessInstanceInfo instance)
    {
        var waitingServiceTokens = WaitingTokens(instance)
            .Where(token => token.CurrentFlowNode is BPMN.Activities.ServiceTask)
            .Select(token => token.Id)
            .ToHashSet();
        if (waitingServiceTokens.Count == 0) return [];

        var now = DateTime.UtcNow;
        return (await storage.ServiceTaskStorage.GetJobs())
            .Where(job => job.ProcessInstanceId == instance.InstanceId
                          && waitingServiceTokens.Contains(job.TokenId)
                          && job.LockedBy is not null
                          && job.LockedUntil > now)
            .Select(job => new InstanceMigrationFinding(
                InstanceMigrationCodes.ServiceTaskJobInProgress,
                job.FlowNodeId,
                "A worker currently holds the job of this service task."))
            .ToArray();
    }

    /// <summary>
    /// Ob am Zielknoten ein Timer haengt: als Timer-Catch-Event oder als angehefteter
    /// Boundary-Timer der Zielversion. Beide rechnen nach dem Umzug mit der Dauer der
    /// Zielversion ab dem unveraenderten Zeitstempel des wartenden Tokens.
    /// </summary>
    private static bool HasTimerInTarget(Process targetProcess, FlowNode targetFlowNode) =>
        targetFlowNode is FlowzerIntermediateTimerCatchEvent
        || targetProcess.FlowElements
            .OfType<FlowzerBoundaryTimerEvent>()
            .Any(boundaryEvent => string.Equals(
                boundaryEvent.AttachedToRef.Id, targetFlowNode.Id, StringComparison.Ordinal));

    private static bool IsFormBindingIdentical(ResolvedMigration resolved, Token token, FlowNode targetFlowNode) =>
        InstanceMigrationFormBinding.IsIdentical(
            resolved.SourceDefinition,
            resolved.Target,
            (token.CurrentFlowNode as UserTask)?.Implementation,
            (targetFlowNode as UserTask)?.Implementation);

    private static IEnumerable<Token> WaitingTokens(ProcessInstanceInfo instance) => instance.Tokens
        .Where(token => token.ParentTokenId != null && token.State == FlowNodeState.Active);

    // Nur die oberste Ebene, wie im Plan der Engine: Ein gleichnamiger Knoten in einem
    // Teilprozess ist ein anderer Knoten.
    private static Dictionary<string, FlowNode> TargetFlowNodesById(Process targetProcess) => targetProcess.FlowElements
        .OfType<FlowNode>()
        .GroupBy(flowNode => flowNode.Id, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    /// <summary>
    /// Prueft die Anfrage als Ganzes und laedt, was beide Wege brauchen: die Instanzen, ihre
    /// gemeinsame Quellversion und das Modell der deployten Zielversion.
    /// </summary>
    private static async Task<(InstanceMigrationRequestStatus Status, string? Message, ResolvedMigration? Resolved)>
        ResolveMigrationRequest(ITransactionalStorage storage, IReadOnlyCollection<Guid> instanceIds)
    {
        if (instanceIds.Count == 0)
            return (InstanceMigrationRequestStatus.NoInstances, "No process instance was named.", null);
        if (instanceIds.Count > MaxMigrationInstances)
            return (InstanceMigrationRequestStatus.TooManyInstances,
                $"At most {MaxMigrationInstances} process instances can be migrated in one request.", null);
        if (instanceIds.Distinct().Count() != instanceIds.Count)
            return (InstanceMigrationRequestStatus.DuplicateInstances,
                "The same process instance was named more than once.", null);

        var instances = new List<ProcessInstanceInfo>(instanceIds.Count);
        foreach (var instanceId in instanceIds)
        {
            try { instances.Add(await storage.InstanceStorage.GetProcessInstance(instanceId)); }
            catch (Exception exception) when (exception is FileNotFoundException or KeyNotFoundException)
            {
                return (InstanceMigrationRequestStatus.UnknownInstance,
                    $"The process instance \"{instanceId}\" was not found.", null);
            }
        }

        if (instances.Select(instance => instance.metaDefinitionId).Distinct(StringComparer.Ordinal).Count() != 1)
            return (InstanceMigrationRequestStatus.MixedWorkflows,
                "All process instances of one migration must belong to the same workflow.", null);
        if (instances.Select(instance => instance.DefinitionId).Distinct().Count() != 1)
            return (InstanceMigrationRequestStatus.MixedSourceVersions,
                "All process instances of one migration must run on the same source version.", null);

        var relatedDefinitionId = instances[0].metaDefinitionId;
        var target = await storage.DefinitionStorage.GetDeployedDefinition(relatedDefinitionId);
        if (target is null)
            return (InstanceMigrationRequestStatus.NoDeployedVersion,
                $"The workflow \"{relatedDefinitionId}\" has no deployed version.", null);

        var sourceDefinitionId = instances[0].DefinitionId;
        var sourceDefinition = sourceDefinitionId == target.Id
            ? target
            : await FindDefinition(storage, sourceDefinitionId);
        var targetModel = ModelParser.ParseModel(await storage.DefinitionStorage.GetBinary(target.Id));

        return (InstanceMigrationRequestStatus.Accepted, null, new ResolvedMigration(
            relatedDefinitionId, sourceDefinitionId, sourceDefinition, target, targetModel, instances));
    }

    /// <summary>Die Quellversion darf fehlen (Altbestand, geloeschte Version); dann wird nichts geraten.</summary>
    private static async Task<BpmnDefinition?> FindDefinition(ITransactionalStorage storage, Guid definitionId)
    {
        try { return await storage.DefinitionStorage.GetDefinitionById(definitionId); }
        catch (FileNotFoundException) { return null; }
    }

    /// <summary>Alles, was fuer eine Anfrage einmal geladen wird und fuer jede Instanz gilt.</summary>
    private sealed record ResolvedMigration(
        string RelatedDefinitionId,
        Guid SourceDefinitionId,
        BpmnDefinition? SourceDefinition,
        BpmnDefinition Target,
        Definitions TargetModel,
        IReadOnlyList<ProcessInstanceInfo> Instances)
    {
        /// <summary>
        /// Der gleichnamige Prozess der Zielversion. Fehlt er, ist die Instanz nicht migrierbar:
        /// Ein anderer Prozess ist ein anderes Modell, kein neuer Stand desselben.
        /// </summary>
        internal Process? ProcessOf(string processId) => TargetModel
            .GetProcesses()
            .FirstOrDefault(process => string.Equals(process.Id, processId, StringComparison.Ordinal));
    }
}
