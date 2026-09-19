using BPMN.Common;
using BPMN.Foundation;
using core_engine.Extensions;

namespace core_engine;

/// <summary>
/// Hebt eine laufende Instanz auf eine andere Fassung desselben Prozesses. Jede Stelle, an der
/// die Instanz gerade wartet, muss es im Zielmodell unverändert geben — oder die Bedienung ordnet
/// ihr von Hand einen Zielknoten desselben Typs zu. Der Umzug tauscht daher nur das mitgeführte
/// Modell aus und rührt weder Variablen noch Historie an.
/// </summary>
public static class InstanceMigration
{
    /// <summary>
    /// Prüft, ob der Tokenbestand einer laufenden Instanz auf <paramref name="targetProcess"/>
    /// gehoben werden kann. Die Prüfung ist frei von Nebenwirkungen.
    /// </summary>
    /// <param name="flowNodeMapping">
    /// Zuordnung von der Kennung eines wartenden Quellknotens auf die Kennung seines Zielknotens.
    /// Ohne Eintrag gilt der Knoten mit derselben Kennung im Zielmodell.
    /// </param>
    public static InstanceMigrationPlan Plan(
        IReadOnlyList<Token> tokens,
        Process targetProcess,
        IReadOnlyDictionary<string, string>? flowNodeMapping = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(targetProcess);

        var masterTokens = tokens.Where(token => token.ParentTokenId == null).ToArray();
        if (masterTokens.Length != 1
            || masterTokens[0].CurrentBaseElement is not Process sourceProcess
            || masterTokens[0].State != FlowNodeState.Active)
        {
            // Ohne laufende Instanz sagen weitere Befunde nichts aus, deshalb bleibt es bei diesem einen.
            return new InstanceMigrationPlan(
                tokens,
                targetProcess,
                [],
                new Dictionary<Guid, FlowNode>(),
                [],
                [new InstanceMigrationProblem(
                    InstanceMigrationProblemCode.InstanceNotRunning,
                    null,
                    "The instance has no single active master token carrying a process model.")]);
        }

        var masterToken = masterTokens[0];
        var waitingTokens = tokens
            .Where(token => token.ParentTokenId != null && token.IsAlive())
            .ToArray();

        var problems = new List<InstanceMigrationProblem>();
        var targetFlowNodes = new Dictionary<Guid, FlowNode>();
        var flowNodeIdsNeedingMapping = new List<string>();

        if (sourceProcess.Id != targetProcess.Id)
        {
            problems.Add(new InstanceMigrationProblem(
                InstanceMigrationProblemCode.ProcessChanged,
                null,
                $"The instance runs process '{sourceProcess.Id}', but the target process is '{targetProcess.Id}'."));
        }

        // Nur die oberste Ebene: Ein gleichnamiger FlowNode in einem Subprozess ist ein anderer Knoten.
        var targetFlowNodesById = targetProcess.FlowElements
            .OfType<FlowNode>()
            .GroupBy(flowNode => flowNode.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        foreach (var token in waitingTokens)
        {
            CheckWaitingToken(
                token,
                tokens,
                masterToken,
                targetFlowNodesById,
                flowNodeMapping,
                problems,
                targetFlowNodes,
                flowNodeIdsNeedingMapping);
            CheckBoundaryEvents(token, sourceProcess, problems);
        }

        return new InstanceMigrationPlan(
            tokens,
            targetProcess,
            waitingTokens,
            targetFlowNodes,
            // Nach Kennung sortiert und nicht in Tokenreihenfolge: Dieselbe Zuordnung gilt für alle
            // Instanzen einer Anfrage, und deren Tokens stehen in beliebiger Reihenfolge.
            [.. flowNodeIdsNeedingMapping.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal)],
            problems);
    }

    /// <summary>
    /// Baut den umgezogenen Tokenbestand. Reihenfolge, Anzahl und Token-Kennungen bleiben gleich;
    /// abgeschlossene Tokens werden als dieselben Objekte übernommen, weil sie Historie sind.
    /// </summary>
    public static List<Token> Apply(InstanceMigrationPlan plan, FlowzerConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (!plan.IsMigratable)
        {
            throw new InvalidOperationException(
                "The migration plan reports problems and must not be applied: "
                + string.Join(" ", plan.Problems.Select(problem => problem.Message)));
        }

        var effectiveConfig = config ?? FlowzerConfig.Default;
        var masterToken = plan.SourceTokens.Single(token => token.ParentTokenId == null);

        // Die Engine löst Ausdrücke eines frisch erzeugten Tokens gegen die Variablen des
        // Prozess-Tokens auf; bei einer flachen Instanz ist das der Master-Token.
        var processVariables = masterToken.Variables ?? new Variables();

        var migratedTokens = new List<Token>(plan.SourceTokens.Count);
        foreach (var token in plan.SourceTokens)
        {
            if (token.ParentTokenId == null)
            {
                migratedTokens.Add(CopyToken(token, plan.TargetProcess, [.. token.ActiveBoundaryEvents]));
                continue;
            }

            if (!token.IsAlive())
            {
                migratedTokens.Add(token);
                continue;
            }

            var targetFlowNode = plan.TargetFlowNodeOf(token.Id);
            migratedTokens.Add(CopyToken(
                token,
                targetFlowNode.ApplyResolveExpression<FlowNode>(
                    effectiveConfig.ExpressionHandler.ResolveString,
                    processVariables),
                ResolveBoundaryEvents(plan.TargetProcess, targetFlowNode, effectiveConfig, processVariables)));
        }

        return migratedTokens;
    }

    private static void CheckWaitingToken(
        Token token,
        IReadOnlyList<Token> tokens,
        Token masterToken,
        IReadOnlyDictionary<string, FlowNode> targetFlowNodesById,
        IReadOnlyDictionary<string, string>? flowNodeMapping,
        List<InstanceMigrationProblem> problems,
        Dictionary<Guid, FlowNode> targetFlowNodes,
        List<string> flowNodeIdsNeedingMapping)
    {
        var flowNodeId = token.CurrentBaseElement.Id;

        if (token.State != FlowNodeState.Active)
        {
            problems.Add(new InstanceMigrationProblem(
                InstanceMigrationProblemCode.TokenNotAtRest,
                flowNodeId,
                $"Token at flow node '{flowNodeId}' is in state {token.State} and does not rest."));
        }

        if (IsMultiInstance(token, tokens))
        {
            problems.Add(new InstanceMigrationProblem(
                InstanceMigrationProblemCode.MultiInstanceNotSupported,
                flowNodeId,
                $"Flow node '{flowNodeId}' belongs to a multi instance activity, which is not supported yet."));
            return;
        }

        if (token.CurrentBaseElement is CallActivity)
        {
            problems.Add(new InstanceMigrationProblem(
                InstanceMigrationProblemCode.CallActivityWaiting,
                flowNodeId,
                $"Flow node '{flowNodeId}' waits for a called process, which cannot be moved along yet."));
            return;
        }

        if (token.CurrentBaseElement is SubProcess || token.ParentTokenId != masterToken.Id)
        {
            problems.Add(new InstanceMigrationProblem(
                InstanceMigrationProblemCode.SubProcessNotSupported,
                flowNodeId,
                $"Flow node '{flowNodeId}' runs in a nested scope, which is not supported yet."));
            return;
        }

        // Eine Zuordnung gilt für alle Instanzen einer Anfrage. Ein Eintrag für einen Knoten, an
        // dem diese Instanz nicht wartet, bleibt deshalb folgenlos und ist kein Hindernis.
        string? mappedFlowNodeId = null;
        if (flowNodeMapping?.TryGetValue(flowNodeId, out var mappedId) == true)
        {
            mappedFlowNodeId = mappedId;
        }

        if (!targetFlowNodesById.TryGetValue(mappedFlowNodeId ?? flowNodeId, out var targetFlowNode))
        {
            if (mappedFlowNodeId != null)
            {
                problems.Add(new InstanceMigrationProblem(
                    InstanceMigrationProblemCode.MappingTargetMissing,
                    flowNodeId,
                    $"Flow node '{flowNodeId}' is mapped to '{mappedFlowNodeId}', "
                    + "which does not exist in the target process."));
                return;
            }

            // Ohne Zuordnung ist der Knoten genau der Fall, zu dem die Oberfläche nachfragen muss.
            flowNodeIdsNeedingMapping.Add(flowNodeId);
            problems.Add(new InstanceMigrationProblem(
                InstanceMigrationProblemCode.FlowNodeMissing,
                flowNodeId,
                $"Flow node '{flowNodeId}' does not exist in the target process."));
            return;
        }

        var sourceType = token.CurrentBaseElement.GetType();
        if (sourceType != targetFlowNode.GetType())
        {
            problems.Add(new InstanceMigrationProblem(
                InstanceMigrationProblemCode.FlowNodeTypeChanged,
                flowNodeId,
                mappedFlowNodeId == null
                    ? $"Flow node '{flowNodeId}' is a {sourceType.Name} in the instance "
                      + $"but a {targetFlowNode.GetType().Name} in the target process."
                    : $"Flow node '{flowNodeId}' is a {sourceType.Name} in the instance, but its mapped "
                      + $"target '{mappedFlowNodeId}' is a {targetFlowNode.GetType().Name}."));
            return;
        }

        if (token.CurrentBaseElement is ServiceTask sourceServiceTask
            && targetFlowNode is ServiceTask targetServiceTask
            && !CheckServiceTask(sourceServiceTask, targetServiceTask, flowNodeId, problems))
        {
            return;
        }

        targetFlowNodes[token.Id] = targetFlowNode;
    }

    /// <summary>
    /// Die Engine nimmt ein ausgelöstes Boundary-Event vom Token. Der Umzug bestimmt die
    /// Boundary-Events aus dem Zielmodell neu und schaltete es damit wieder scharf — ein nicht
    /// unterbrechender Timer liefe ein zweites Mal. Trägt der Token weniger Boundary-Events,
    /// als sein Knoten im Quellmodell besitzt, hat bereits eines ausgelöst.
    /// </summary>
    private static void CheckBoundaryEvents(
        Token token,
        Process sourceProcess,
        List<InstanceMigrationProblem> problems)
    {
        var flowNodeId = token.CurrentBaseElement.Id;
        var attachedInSourceProcess = sourceProcess.FlowElements
            .OfType<BoundaryEvent>()
            .Count(boundaryEvent => boundaryEvent.AttachedToRef?.Id == flowNodeId);

        if (token.ActiveBoundaryEvents.Count >= attachedInSourceProcess)
        {
            return;
        }

        problems.Add(new InstanceMigrationProblem(
            InstanceMigrationProblemCode.BoundaryEventAlreadyTriggered,
            flowNodeId,
            $"A boundary event of flow node '{flowNodeId}' has already triggered and would be armed again."));
    }

    /// <summary>
    /// Meldet die Hindernisse eines ServiceTasks und sagt, ob der Umzug für ihn noch offen ist.
    /// </summary>
    private static bool CheckServiceTask(
        ServiceTask sourceServiceTask,
        ServiceTask targetServiceTask,
        string flowNodeId,
        List<InstanceMigrationProblem> problems)
    {
        var isMigratable = true;

        if (sourceServiceTask.FlowzerAiTask != null || targetServiceTask.FlowzerAiTask != null)
        {
            problems.Add(new InstanceMigrationProblem(
                InstanceMigrationProblemCode.AiTaskNotSupported,
                flowNodeId,
                $"Service task '{flowNodeId}' carries an AI task contract, which is not supported yet."));
            isMigratable = false;
        }

        // Ein bereits eingereihter Auftrag trägt den alten Typ; nach dem Umzug holte ihn der falsche Worker ab.
        if (sourceServiceTask.Implementation != targetServiceTask.Implementation)
        {
            problems.Add(new InstanceMigrationProblem(
                InstanceMigrationProblemCode.ServiceTaskTypeChanged,
                flowNodeId,
                $"Service task '{flowNodeId}' changes its implementation from "
                + $"'{sourceServiceTask.Implementation}' to '{targetServiceTask.Implementation}'."));
            isMigratable = false;
        }

        return isMigratable;
    }

    private static bool IsMultiInstance(Token token, IReadOnlyList<Token> tokens)
    {
        if (token.CurrentBaseElement is Activity { LoopCharacteristics: MultiInstanceLoopCharacteristics })
        {
            return true;
        }

        // Die Kindtokens einer Multi-Instance-Aktivität tragen dasselbe Element ohne
        // LoopCharacteristics; erkennbar sind sie nur am Elterntoken.
        var parentToken = tokens.SingleOrDefault(candidate => candidate.Id == token.ParentTokenId);
        return parentToken?.CurrentBaseElement is Activity { LoopCharacteristics: MultiInstanceLoopCharacteristics };
    }

    private static List<BoundaryEvent> ResolveBoundaryEvents(
        Process targetProcess,
        FlowNode targetFlowNode,
        FlowzerConfig config,
        Variables processVariables)
    {
        return targetProcess.FlowElements
            .OfType<BoundaryEvent>()
            .Where(boundaryEvent => boundaryEvent.AttachedToRef == targetFlowNode)
            .Select(boundaryEvent => boundaryEvent.ApplyResolveExpression<BoundaryEvent>(
                config.ExpressionHandler.ResolveString,
                processVariables))
            .ToList();
    }

    private static Token CopyToken(
        Token sourceToken,
        IBaseElement currentBaseElement,
        List<BoundaryEvent> activeBoundaryEvents)
    {
        var migratedToken = new Token
        {
            Id = sourceToken.Id,
            ProcessInstanceId = sourceToken.ProcessInstanceId,
            ParentTokenId = sourceToken.ParentTokenId,
            CurrentBaseElement = currentBaseElement,
            ActiveBoundaryEvents = activeBoundaryEvents,
            State = sourceToken.State,
            StartTime = sourceToken.StartTime,
            PreviousToken = sourceToken.PreviousToken,
            LastSequenceFlow = sourceToken.LastSequenceFlow,
            Variables = sourceToken.Variables,
            OutputData = sourceToken.OutputData,
            CompletedByUserId = sourceToken.CompletedByUserId,
            Initiator = sourceToken.Initiator
        };

        // Der State-Setter schreibt LastStateChangeTime auf "jetzt"; der echte Zeitstempel muss
        // deshalb danach gesetzt werden, sonst verlöre der Umzug die Wartezeit eines Tokens.
        migratedToken.LastStateChangeTime = sourceToken.LastStateChangeTime;

        return migratedToken;
    }
}
