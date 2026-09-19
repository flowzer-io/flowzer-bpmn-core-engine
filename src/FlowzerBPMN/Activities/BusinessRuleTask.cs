using System.ComponentModel.DataAnnotations;

namespace BPMN.Activities;

/// <summary>
/// Eine Aufgabe, die eine Entscheidung trifft. Zwei Auspraegungen, wie in Camunda 8:
/// mit <c>zeebe:calledDecision</c> rechnet die Entscheidungstabelle lokal, mit
/// <c>zeebe:taskDefinition</c> ist es ein gewoehnlicher Auftrag an einen Worker.
/// </summary>
/// <remarks>
/// Deshalb traegt der Business-Rule-Task auch <see cref="IFlowzerWorkerTask"/>: Ein Task mit
/// Auftragstyp erscheint dadurch von selbst in <c>GetActiveServiceTasks()</c> und wird wie
/// ein Service-Task abgearbeitet — genau so ist es gewollt. Ohne Auftragstyp bleibt
/// <see cref="Implementation"/> leer; dann wartet der Token auf die Entscheidung.
/// </remarks>
public record BusinessRuleTask : Task, IFlowzerInputMapping, IFlowzerOutputMapping, IFlowzerWorkerTask
{
    [Required] public string Implementation { get; init; } = "";

    public int FlowzerRetries { get; init; }

    /// <summary>
    /// Die Id der Decision aus <c>zeebe:calledDecision/@decisionId</c>. Null an einem Task,
    /// der seine Arbeit an einen Worker vergibt.
    /// </summary>
    public string? FlowzerCalledDecisionId { get; init; }

    /// <summary>
    /// Der Name, unter dem das Ergebnis der Entscheidung in den Prozess geschrieben wird
    /// (<c>zeebe:calledDecision/@resultVariable</c>).
    /// </summary>
    public string? FlowzerResultVariable { get; init; }

    public FlowzerList<FlowzerIoMapping>? InputMappings { get; init; }
    public FlowzerList<FlowzerIoMapping>? OutputMappings { get; init; }
}
