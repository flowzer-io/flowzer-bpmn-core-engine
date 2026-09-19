namespace FlowzerDmn.Model;

/// <summary>
/// Eine Abhaengigkeit einer Decision: entweder auf eine andere Decision oder auf einen
/// Input-Data-Knoten.
/// </summary>
/// <remarks>
/// Aus den Referenzen auf andere Decisions ergibt sich die Auswertungsreihenfolge. Der
/// Parser prueft, dass jede Referenz aufloesbar ist und dass das Netz zyklenfrei bleibt.
/// </remarks>
public sealed record DmnInformationRequirement
{
    /// <summary>Id des Requirement-Elements, sofern vorhanden.</summary>
    public string? Id { get; init; }

    /// <summary>Id der benoetigten Decision (aus <c>requiredDecision/@href</c>, ohne <c>#</c>).</summary>
    public string? RequiredDecisionId { get; init; }

    /// <summary>Id des benoetigten Input-Data-Knotens (aus <c>requiredInput/@href</c>, ohne <c>#</c>).</summary>
    public string? RequiredInputId { get; init; }
}
