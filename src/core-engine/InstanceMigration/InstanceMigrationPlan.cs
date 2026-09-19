using BPMN.Common;

namespace core_engine;

/// <summary>
/// Das Prüfergebnis eines geplanten Instanzumzugs. Der Plan ist rein beschreibend: Er hält die
/// Quelltokens, das Zielmodell und die bereits aufgelöste Zuordnung wartender Tokens auf ihren
/// Ziel-FlowNode fest, damit die aufrufende Schicht altes und neues Element vergleichen kann
/// (etwa den Formularschlüssel einer Aufgabe), ohne die Zuordnung erneut zu erraten.
/// </summary>
public sealed record InstanceMigrationPlan
{
    private readonly IReadOnlyDictionary<Guid, FlowNode> _targetFlowNodes;

    internal InstanceMigrationPlan(
        IReadOnlyList<Token> sourceTokens,
        Process targetProcess,
        IReadOnlyList<Token> waitingTokens,
        IReadOnlyDictionary<Guid, FlowNode> targetFlowNodes,
        IReadOnlyList<string> flowNodeIdsNeedingMapping,
        IReadOnlyList<InstanceMigrationProblem> problems)
    {
        SourceTokens = sourceTokens;
        TargetProcess = targetProcess;
        WaitingTokens = waitingTokens;
        FlowNodeIdsNeedingMapping = flowNodeIdsNeedingMapping;
        Problems = problems;
        _targetFlowNodes = targetFlowNodes;
    }

    /// <summary>Der geprüfte Tokenbestand in unveränderter Reihenfolge.</summary>
    public IReadOnlyList<Token> SourceTokens { get; }

    /// <summary>Das Modell, auf das die Instanz umziehen soll.</summary>
    public Process TargetProcess { get; }

    /// <summary>Die noch lebenden Nicht-Master-Tokens, also die Stellen, an denen die Instanz wartet.</summary>
    public IReadOnlyList<Token> WaitingTokens { get; }

    /// <summary>Wartende Knoten, die es in der Zielversion nicht gibt und denen kein Ziel zugeordnet wurde.</summary>
    public IReadOnlyList<string> FlowNodeIdsNeedingMapping { get; }

    /// <summary>Alle Hindernisse; die Oberfläche listet sie vollständig auf.</summary>
    public IReadOnlyList<InstanceMigrationProblem> Problems { get; }

    public bool IsMigratable => Problems.Count == 0;

    /// <summary>
    /// Der Ziel-FlowNode eines wartenden Tokens, so wie er im Zielmodell steht (ohne aufgelöste
    /// Ausdrücke). Für einen Token ohne Zuordnung gibt es keinen sinnvollen Rückgabewert.
    /// </summary>
    public FlowNode TargetFlowNodeOf(Guid tokenId)
    {
        if (!_targetFlowNodes.TryGetValue(tokenId, out var targetFlowNode))
        {
            throw new InvalidOperationException(
                $"Token {tokenId} has no matching flow node in the target process.");
        }

        return targetFlowNode;
    }
}

/// <summary>
/// Ein einzelnes Hindernis. Die Meldung ist für Protokoll und API-Detail gedacht; die
/// Oberfläche übersetzt den Code.
/// </summary>
public sealed record InstanceMigrationProblem(
    InstanceMigrationProblemCode Code,
    string? FlowNodeId,
    string Message);

public enum InstanceMigrationProblemCode
{
    /// <summary>Kein eindeutiger, aktiver Master-Token mit einem Prozessmodell.</summary>
    InstanceNotRunning,

    /// <summary>Das Zielmodell beschreibt einen anderen Prozess.</summary>
    ProcessChanged,

    /// <summary>Ein lebender Token ruht nicht; gespeicherte Instanzen warten nur im Zustand Active.</summary>
    TokenNotAtRest,

    /// <summary>Den FlowNode gibt es im Zielmodell nicht mehr.</summary>
    FlowNodeMissing,

    /// <summary>Den FlowNode gibt es noch, aber als anderer Elementtyp.</summary>
    FlowNodeTypeChanged,

    /// <summary>Verschachtelte Scopes bleiben der ersten Stufe verschlossen.</summary>
    SubProcessNotSupported,

    /// <summary>Multi-Instance-Aktivitäten bleiben der ersten Stufe verschlossen.</summary>
    MultiInstanceNotSupported,

    /// <summary>Ein eingereihter Auftrag trüge nach dem Umzug den falschen Typ.</summary>
    ServiceTaskTypeChanged,

    /// <summary>KI-Aufgaben tragen einen eigenen Vertrag und bleiben vorerst ausgenommen.</summary>
    AiTaskNotSupported,

    /// <summary>
    /// Ein Boundary-Event des wartenden Knotens hat bereits ausgelöst; der Umzug schaltete es
    /// erneut scharf, und es liefe ein zweites Mal.
    /// </summary>
    BoundaryEventAlreadyTriggered,

    /// <summary>Den von Hand zugeordneten Zielknoten gibt es im Zielmodell nicht.</summary>
    MappingTargetMissing
}
