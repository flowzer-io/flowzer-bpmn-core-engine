namespace core_engine;

/// <summary>
/// BPMN-Elemente, die laut BPMN 2.0 in jedem Prozess stehen duerfen, aber keine
/// Ausfuehrungssemantik tragen: Gliederung, Beschriftung, Gruppierung und Datenbeiwerk.
/// Flowzer ueberliest sie — im Parser <b>und</b> in der Veroeffentlichungspruefung.
///
/// Die Liste steht bewusst an genau einer Stelle. Ein Element, das der Parser ueberliest, die
/// Pruefung aber als Flow-Element behandelt (oder umgekehrt), waere genau der Widerspruch, den
/// diese Toleranz beseitigen soll: Ein fremdes Diagramm scheitert dann an seiner Dekoration
/// statt an einer echten Ausfuehrungsluecke.
///
/// Ueberlesen heisst ausdruecklich <b>nicht</b> ausgefuehrt: Lanes weisen keine Arbeit zu,
/// Datenobjekte tragen keine Variablen. Wer das braucht, modelliert es als Flow-Element.
/// </summary>
internal static class BpmnDiagramDecorations
{
    private static readonly HashSet<string> LocalNames = new(StringComparer.Ordinal)
    {
        // Gliederung: Die Knoten selbst stehen weiterhin direkt im Prozess.
        "laneSet",
        "lane",
        // Beschriftung, Gruppierung, Kategorisierung.
        "textAnnotation",
        "association",
        "group",
        "category",
        "documentation",
        // Datenbeiwerk an Prozess und Aktivitaet.
        "dataObject",
        "dataObjectReference",
        "dataStoreReference",
        "ioSpecification",
        "dataInput",
        "dataOutput",
        "dataInputAssociation",
        "dataOutputAssociation",
        "property",
    };

    /// <summary>Traegt dieses Kindelement eines Prozesses oder einer Aktivitaet keine Ausfuehrungssemantik?</summary>
    public static bool Contains(string localName) => LocalNames.Contains(localName);
}
