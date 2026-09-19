namespace FlowzerDmn.Model;

/// <summary>
/// Eine Entscheidung: Kopfdaten, Abhaengigkeiten und genau eine Entscheidungslogik.
/// </summary>
public sealed record DmnDecision
{
    /// <summary>Die Id der Decision. Sie ist der Schluessel fuer die Auswertung und ist Pflicht.</summary>
    public required string Id { get; init; }

    /// <summary>Der Anzeigename der Decision.</summary>
    public string? Name { get; init; }

    /// <summary>Die Entscheidungslogik: Tabelle oder Literal-Ausdruck.</summary>
    public required DmnDecisionLogic Logic { get; init; }

    /// <summary>Die Abhaengigkeiten der Decision in Dokumentreihenfolge.</summary>
    public required IReadOnlyList<DmnInformationRequirement> Requirements { get; init; }

    /// <summary>
    /// Name der Ergebnisvariable aus dem <c>variable</c>-Element. Unter diesem Namen steht
    /// das Ergebnis einer benoetigten Decision im Kontext der aufrufenden Decision.
    /// </summary>
    public string? OutputVariableName { get; init; }

    /// <summary>Typangabe der Ergebnisvariable; bei einem Literal-Ausdruck typisiert sie das Ergebnis.</summary>
    public string? OutputTypeRef { get; init; }

    /// <summary>Die Entscheidungslogik als Tabelle, oder <c>null</c> bei einem Literal-Ausdruck.</summary>
    public DmnDecisionTable? DecisionTable => Logic as DmnDecisionTable;

    /// <summary>Die Entscheidungslogik als Literal-Ausdruck, oder <c>null</c> bei einer Tabelle.</summary>
    public DmnLiteralExpression? LiteralExpression => Logic as DmnLiteralExpression;

    /// <summary>Die Ids der benoetigten Decisions in Dokumentreihenfolge.</summary>
    public IReadOnlyList<string> RequiredDecisionIds =>
        Requirements.Where(requirement => requirement.RequiredDecisionId is not null)
            .Select(requirement => requirement.RequiredDecisionId!)
            .ToList();

    /// <summary>Die Ids der benoetigten Input-Data-Knoten in Dokumentreihenfolge.</summary>
    public IReadOnlyList<string> RequiredInputIds =>
        Requirements.Where(requirement => requirement.RequiredInputId is not null)
            .Select(requirement => requirement.RequiredInputId!)
            .ToList();

    /// <summary>
    /// Der Name, unter dem das Ergebnis dieser Decision im Kontext einer abhaengigen
    /// Decision auftaucht: die Ergebnisvariable, sonst der Name, sonst die Id.
    /// </summary>
    public string ResultVariableName =>
        !string.IsNullOrWhiteSpace(OutputVariableName) ? OutputVariableName
        : !string.IsNullOrWhiteSpace(Name) ? Name
        : Id;
}
