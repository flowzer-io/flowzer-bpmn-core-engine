namespace core_engine;

/// <summary>
/// Ein einzelner Eingriff: Der Token <paramref name="TokenId"/> wird zurueckgezogen, und am
/// Knoten <paramref name="TargetFlowNodeId"/> beginnt ein neuer Token — so, als haette ihn der
/// Sequenzfluss gerade erreicht.
/// </summary>
public sealed record InstanceModificationMove(Guid TokenId, string TargetFlowNodeId);

/// <summary>
/// Was der Betrieb an einer laufenden Instanz aendern will: wohin ihre wartenden Schritte
/// gesetzt werden und welche Variablen dabei zu korrigieren sind. Beides ist freiwillig; eine
/// Anfrage ohne Moves und ohne Variablen ist ein gueltiger Trockenlauf und beschreibt nur, was
/// moeglich waere.
/// </summary>
/// <param name="Moves">
/// Die Verschiebungen. Derselbe Quell-Token darf hoechstens einmal vorkommen.
/// </param>
/// <param name="VariablesToSet">
/// Variablen, die geschrieben werden. Geschrieben wird auf der Ebene, auf der die Variable
/// heute liegt; kennt sie dort niemand, auf der Prozessebene.
/// </param>
/// <param name="VariablesToRemove">
/// Variablen, die von der Prozessebene verschwinden. Ein unbekannter Name ist kein Fehler,
/// sondern ein Hinweis.
/// </param>
public sealed record InstanceModificationRequest(
    IReadOnlyList<InstanceModificationMove>? Moves = null,
    IReadOnlyDictionary<string, object?>? VariablesToSet = null,
    IReadOnlyList<string>? VariablesToRemove = null)
{
    /// <summary>Die Anfrage aendert nichts; nur der Trockenlauf hat dafuer Verwendung.</summary>
    public bool IsEmpty =>
        (Moves is null || Moves.Count == 0)
        && (VariablesToSet is null || VariablesToSet.Count == 0)
        && (VariablesToRemove is null || VariablesToRemove.Count == 0);
}
