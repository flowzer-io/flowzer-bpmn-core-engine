using BPMN.Foundation;
using core_engine.Extensions;

namespace core_engine;

/// <summary>
/// Setzt eine laufende Instanz innerhalb <em>derselben</em> Version an eine andere Stelle und
/// korrigiert ihre Variablen — der Eingriff fuer den Betrieb, wenn ein Schritt versehentlich
/// abgeschlossen wurde oder ein Worker an einem Knoten haengt, der uebersprungen werden soll.
///
/// Anders als <see cref="InstanceMigration"/> tauscht das kein Modell aus: Der Quell-Token wird
/// zurueckgezogen wie beim Fangen eines BPMN-Fehlers, und am Ziel beginnt ein <em>neuer</em>
/// Token. Aufgaben und Auftraege der verlassenen Stelle verschwinden deshalb samt ihren
/// Kennungen; am Ziel entstehen neue. Siehe <c>docs/INSTANCE-MODIFICATION.md</c>.
/// </summary>
public static class InstanceModification
{
    /// <summary>
    /// Prueft, ob der Eingriff an <paramref name="instance"/> moeglich ist, und loest dabei die
    /// Zielknoten auf. Die Pruefung ist frei von Nebenwirkungen.
    /// </summary>
    public static InstanceModificationPlan Plan(InstanceEngine instance, InstanceModificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(request);

        var masterTokens = instance.Tokens.Where(token => token.ParentTokenId == null).ToArray();
        if (masterTokens.Length != 1
            || masterTokens[0].CurrentBaseElement is not Process process
            || masterTokens[0].State != FlowNodeState.Active)
        {
            // Ohne laufende Instanz sagen weitere Befunde nichts aus, deshalb bleibt es bei diesem einen.
            return new InstanceModificationPlan(instance, request, [], [
                new InstanceModificationProblem(
                    InstanceModificationProblemCode.InstanceNotRunning,
                    null,
                    null,
                    "The instance has no single active master token carrying a process model.")
            ], []);
        }

        var masterToken = masterTokens[0];
        var problems = new List<InstanceModificationProblem>();
        var notices = new List<InstanceModificationNotice>();
        var moves = new List<InstanceModificationPlan.PlannedMove>();

        // Nur die oberste Ebene: Ein gleichnamiger FlowNode in einem Teilprozess ist ein anderer Knoten.
        var flowNodesById = process.FlowElements
            .OfType<FlowNode>()
            .GroupBy(flowNode => flowNode.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var seenTokenIds = new HashSet<Guid>();
        foreach (var move in request.Moves ?? [])
        {
            if (!seenTokenIds.Add(move.TokenId))
            {
                // Der Knoten wird nachgeschlagen, damit die Meldung den Schritt benennen kann,
                // den die Bedienung vor sich hat, und nicht nur eine Token-Kennung.
                problems.Add(new InstanceModificationProblem(
                    InstanceModificationProblemCode.DuplicateMove,
                    move.TokenId,
                    instance.Tokens.SingleOrDefault(token => token.Id == move.TokenId)?.CurrentBaseElement.Id,
                    $"Token {move.TokenId} is named more than once; a step can only go to one place."));
                continue;
            }

            if (CheckMove(instance, masterToken, flowNodesById, move, problems) is not { } target) continue;

            moves.Add(new InstanceModificationPlan.PlannedMove(instance.Tokens.Single(t => t.Id == move.TokenId), target));
        }

        foreach (var move in moves)
        {
            CollectMoveNotices(process, move, notices);
        }

        CheckVariables(masterToken, instance, request, problems, notices);

        return new InstanceModificationPlan(instance, request, moves, problems, notices);
    }

    /// <summary>
    /// Fuehrt den geprueften Eingriff aus und laesst die Engine danach weiterlaufen: Ein
    /// Benutzer-Task wartet am Ziel neu, ein Service-Task erzeugt einen neuen Auftrag, ein
    /// Gateway wird ausgewertet, ein End-Event beendet die Instanz.
    /// </summary>
    public static void Apply(InstanceModificationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.IsApplicable)
        {
            throw new InvalidOperationException(
                "The modification plan reports problems and must not be applied: "
                + string.Join(" ", plan.Problems.Select(problem => problem.Message)));
        }

        plan.Instance.ApplyModification(plan);
    }

    /// <summary>
    /// Prueft eine einzelne Verschiebung und gibt den Zielknoten zurueck, wenn sie zulaessig ist.
    /// </summary>
    private static FlowNode? CheckMove(
        InstanceEngine instance,
        Token masterToken,
        IReadOnlyDictionary<string, FlowNode> flowNodesById,
        InstanceModificationMove move,
        List<InstanceModificationProblem> problems)
    {
        var sourceToken = instance.Tokens.SingleOrDefault(token => token.Id == move.TokenId);
        if (sourceToken is null)
        {
            problems.Add(new InstanceModificationProblem(
                InstanceModificationProblemCode.TokenMissing,
                move.TokenId,
                null,
                $"Token {move.TokenId} does not belong to this instance."));
            return null;
        }

        var sourceFlowNodeId = sourceToken.CurrentBaseElement.Id;
        if (!IsMovable(sourceToken, masterToken, instance.Tokens))
        {
            problems.Add(new InstanceModificationProblem(
                InstanceModificationProblemCode.TokenNotMovable,
                sourceToken.Id,
                sourceFlowNodeId,
                $"Token {sourceToken.Id} at flow node '{sourceFlowNodeId}' is in state {sourceToken.State} "
                + "or does not rest on the top level of the process; only waiting top level steps can be moved."));
            return null;
        }

        if (!flowNodesById.TryGetValue(move.TargetFlowNodeId, out var target))
        {
            // Genannt wird der Zielknoten, nicht der Quellknoten: Beanstandet ist die Angabe,
            // die die Bedienung gemacht hat — wie bei TargetNotAllowed gleich darunter.
            problems.Add(new InstanceModificationProblem(
                InstanceModificationProblemCode.TargetMissing,
                sourceToken.Id,
                move.TargetFlowNodeId,
                $"Flow node '{move.TargetFlowNodeId}' does not exist on the top level of this process."));
            return null;
        }

        if (!IsAllowedTarget(target))
        {
            problems.Add(new InstanceModificationProblem(
                InstanceModificationProblemCode.TargetNotAllowed,
                sourceToken.Id,
                move.TargetFlowNodeId,
                $"Flow node '{move.TargetFlowNodeId}' is a {target.GetType().Name} and cannot be entered "
                + "by a sequence flow; start events and boundary events are no valid targets."));
            return null;
        }

        return target;
    }

    /// <summary>
    /// Ein Token laesst sich verschieben, wenn er wartet und unmittelbar am Prozess-Token
    /// haengt. Alles andere — Teilprozesse, Multi-Instance, laufende Schritte — bleibt dieser
    /// Stufe verschlossen.
    /// </summary>
    private static bool IsMovable(Token token, Token masterToken, IReadOnlyList<Token> tokens)
    {
        if (token.State != FlowNodeState.Active || !token.IsAlive()) return false;
        if (token.ParentTokenId != masterToken.Id) return false;
        if (token.CurrentBaseElement is SubProcess) return false;
        if (token.CurrentBaseElement is Activity { LoopCharacteristics: MultiInstanceLoopCharacteristics }) return false;

        // Die Kindtokens einer Multi-Instance-Aktivitaet tragen dasselbe Element ohne
        // LoopCharacteristics; erkennbar sind sie nur am Elterntoken.
        var parentToken = tokens.SingleOrDefault(candidate => candidate.Id == token.ParentTokenId);
        return parentToken?.CurrentBaseElement is not Activity { LoopCharacteristics: MultiInstanceLoopCharacteristics };
    }

    /// <summary>
    /// Die Knoten der obersten Ebene, die sich betreten lassen — die Auswahl, die eine
    /// Oberflaeche anbieten darf. Dieselbe Regel wie in der Pruefung: Was hier nicht steht,
    /// lehnt <see cref="Plan"/> als <see cref="InstanceModificationProblemCode.TargetNotAllowed"/>
    /// ab, damit Auskunft und Pruefung nicht auseinanderlaufen koennen.
    /// </summary>
    public static IReadOnlyList<FlowNode> AllowedTargets(InstanceEngine instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return
        [
            .. instance.Process.FlowElements
                .OfType<FlowNode>()
                .Where(IsAllowedTarget)
                .GroupBy(flowNode => flowNode.Id, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => group.First())
        ];
    }

    /// <summary>
    /// Ein Start-Event beginnt eine Instanz, ein Boundary-Event haengt an einer Aktivitaet:
    /// Beide werden nie von einem Sequenzfluss erreicht und waeren als Ziel ein Zustand, den das
    /// Modell nicht kennt.
    /// </summary>
    private static bool IsAllowedTarget(FlowNode flowNode) =>
        flowNode is not StartEvent and not BoundaryEvent;

    /// <summary>
    /// Was diese Verschiebung kostet: die Aufgabe beziehungsweise den Auftrag der verlassenen
    /// Stelle — und einen Timer, der am Ziel von vorn zu laufen beginnt.
    /// </summary>
    private static void CollectMoveNotices(
        Process process,
        InstanceModificationPlan.PlannedMove move,
        List<InstanceModificationNotice> notices)
    {
        var sourceFlowNodeId = move.SourceToken.CurrentBaseElement.Id;

        if (move.SourceToken.CurrentBaseElement is UserTask)
            notices.Add(new InstanceModificationNotice(
                InstanceModificationNoticeCode.UserTaskCancelled,
                move.SourceToken.Id,
                sourceFlowNodeId,
                "The user task waiting here disappears with its id, claim and deadlines."));

        if (move.SourceToken.CurrentBaseElement is ServiceTask)
            notices.Add(new InstanceModificationNotice(
                InstanceModificationNoticeCode.ServiceTaskJobCancelled,
                move.SourceToken.Id,
                sourceFlowNodeId,
                "The worker job of this service task is cancelled."));

        if (HasTimer(process, move.TargetFlowNode))
            notices.Add(new InstanceModificationNotice(
                InstanceModificationNoticeCode.TimerRecalculated,
                move.SourceToken.Id,
                move.TargetFlowNode.Id,
                "A timer at the target starts over from the moment of the modification."));
    }

    /// <summary>Ob am Knoten ein Timer haengt: als Timer-Catch-Event oder als Boundary-Timer.</summary>
    private static bool HasTimer(Process process, FlowNode flowNode) =>
        flowNode is FlowzerIntermediateTimerCatchEvent
        || process.FlowElements
            .OfType<FlowzerBoundaryTimerEvent>()
            .Any(boundaryEvent => string.Equals(
                boundaryEvent.AttachedToRef.Id, flowNode.Id, StringComparison.Ordinal));

    /// <summary>
    /// Prueft die Variablenangaben. Ein Name mit Punkt oder Klammer benennt einen Pfad; diese
    /// Stufe schreibt und entfernt nur ganze Variablen und rueckt das nicht zurecht.
    /// </summary>
    private static void CheckVariables(
        Token masterToken,
        InstanceEngine instance,
        InstanceModificationRequest request,
        List<InstanceModificationProblem> problems,
        List<InstanceModificationNotice> notices)
    {
        foreach (var name in (request.VariablesToSet?.Keys ?? []).Concat(request.VariablesToRemove ?? []))
        {
            if (IsPlainVariableName(name)) continue;

            problems.Add(new InstanceModificationProblem(
                InstanceModificationProblemCode.VariableNameInvalid,
                null,
                null,
                $"\"{name}\" is not a plain variable name; paths and indexes are not supported here."));
        }

        foreach (var name in request.VariablesToRemove ?? [])
        {
            if (!IsPlainVariableName(name)) continue;
            if (ProcessScopeTokens(masterToken, instance).Any(token => token.Variables.HasProperty(name))) continue;

            notices.Add(new InstanceModificationNotice(
                InstanceModificationNoticeCode.VariableNotFound,
                null,
                null,
                $"The instance has no variable \"{name}\"; nothing is removed for it."));
        }
    }

    private static bool IsPlainVariableName(string name) =>
        !string.IsNullOrWhiteSpace(name) && !name.Contains('.') && !name.Contains('[');

    /// <summary>
    /// Die Tokens, an denen Variablen der Prozessebene liegen koennen: der Prozess-Token selbst
    /// und die wartenden Schritte unmittelbar darunter. Beide Wege — Schreiben und Entfernen —
    /// teilen sich diese Sicht, damit der Hinweis dasselbe sieht wie der Eingriff.
    /// </summary>
    internal static IEnumerable<Token> ProcessScopeTokens(Token masterToken, InstanceEngine instance)
    {
        yield return masterToken;

        foreach (var token in instance.Tokens.Where(token =>
                     token.ParentTokenId == masterToken.Id && token.IsAlive()))
        {
            yield return token;
        }
    }
}
