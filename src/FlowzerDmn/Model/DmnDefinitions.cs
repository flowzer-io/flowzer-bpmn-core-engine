namespace FlowzerDmn.Model;

/// <summary>
/// Der Inhalt einer DMN-Datei: Kopfdaten und die enthaltenen Entscheidungen.
/// </summary>
public sealed record DmnDefinitions
{
    /// <summary>Die Id aus <c>definitions/@id</c>; fehlt sie, bleibt sie leer.</summary>
    public required string Id { get; init; }

    /// <summary>Der Anzeigename aus <c>definitions/@name</c>.</summary>
    public string? Name { get; init; }

    /// <summary>Der fachliche Namensraum aus <c>definitions/@namespace</c>.</summary>
    public required string Namespace { get; init; }

    /// <summary>Der XML-Namensraum der DMN-Elemente; aus ihm ergibt sich <see cref="Version"/>.</summary>
    public required string ModelNamespace { get; init; }

    /// <summary>Die erkannte DMN-Fassung.</summary>
    public required DmnVersion Version { get; init; }

    /// <summary>Die Entscheidungen in Dokumentreihenfolge.</summary>
    public required IReadOnlyList<DmnDecision> Decisions { get; init; }

    /// <summary>Sucht eine Decision anhand ihrer Id; liefert <c>null</c>, wenn es sie nicht gibt.</summary>
    public DmnDecision? FindDecision(string decisionId) =>
        Decisions.FirstOrDefault(decision => string.Equals(decision.Id, decisionId, StringComparison.Ordinal));
}
