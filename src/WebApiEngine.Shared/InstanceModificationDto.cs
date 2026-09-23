using System.Dynamic;
using System.Text.Json.Serialization;

namespace WebApiEngine.Shared;

/// <summary>Ein einzelner Eingriff: welcher wartende Schritt auf welchen Knoten geht.</summary>
public sealed class InstanceModificationMoveDto
{
    public required Guid TokenId { get; init; }
    public required string TargetFlowNodeId { get; init; }
}

/// <summary>Die Variablenkorrektur einer Anfrage.</summary>
public sealed class InstanceModificationVariablesDto
{
    /// <summary>
    /// Werte, die geschrieben werden. Genannte Variablen werden ueberschrieben oder neu
    /// angelegt, ungenannte bleiben stehen. Derselbe Konverter wie beim Abschluss einer
    /// Aufgabe, sonst ueberleben die Werte die Ablage nicht.
    /// </summary>
    [JsonConverter(typeof(ExpandoObjectConverter))]
    public ExpandoObject? Set { get; init; }

    /// <summary>Variablen, die von der Prozessebene verschwinden.</summary>
    public string[]? Remove { get; init; }
}

/// <summary>
/// Was an einer laufenden Instanz geaendert werden soll. Beide Abschnitte sind freiwillig; der
/// Trockenlauf nimmt auch eine leere Anfrage an und beschreibt dann nur, was moeglich waere.
/// </summary>
public sealed class InstanceModificationRequestDto
{
    public InstanceModificationMoveDto[]? Moves { get; init; }
    public InstanceModificationVariablesDto? Variables { get; init; }
}

/// <summary>Ein Hindernis oder ein Hinweis. Der Code ist stabil, die Meldung technisch.</summary>
public sealed class InstanceModificationFindingDto
{
    public required string Code { get; init; }

    /// <summary>Der betroffene Token, sofern der Befund an einem einzelnen Schritt haengt.</summary>
    public Guid? TokenId { get; init; }

    public string? FlowNodeId { get; init; }
    public required string Message { get; init; }
}

/// <summary>Ein wartender Schritt, ueber den die Bedienung entscheidet.</summary>
public sealed class InstanceModificationStepDto
{
    public required Guid TokenId { get; init; }
    public required string FlowNodeId { get; init; }

    /// <summary>Null, wenn der Knoten im Modell keinen Namen traegt.</summary>
    public string? Name { get; init; }

    /// <summary>Der Elementtyp, etwa "UserTask"; die Oberflaeche gruppiert danach.</summary>
    public required string Type { get; init; }
}

/// <summary>Ein Knoten, der als Ziel zur Wahl steht.</summary>
public sealed class ModificationFlowNodeDto
{
    public required string Id { get; init; }
    public string? Name { get; init; }
    public required string Type { get; init; }
}

/// <summary>
/// Der Trockenlauf: was der Eingriff verhindert, was er kostet — und, fuer eine leere Anfrage,
/// welche Schritte warten und welche Knoten als Ziel in Frage kommen.
/// </summary>
public sealed class InstanceModificationPreviewDto
{
    public required Guid InstanceId { get; init; }

    /// <summary>Ob die Anfrage so ausgefuehrt werden koennte.</summary>
    public required bool Applicable { get; init; }

    /// <summary>Gruende, aus denen der Eingriff nicht ausgefuehrt wird.</summary>
    public required InstanceModificationFindingDto[] Problems { get; init; }

    /// <summary>Was der Eingriff mitnimmt, ohne ihn zu verhindern.</summary>
    public required InstanceModificationFindingDto[] Notices { get; init; }

    /// <summary>Die wartenden Schritte der Instanz.</summary>
    public required InstanceModificationStepDto[] Steps { get; init; }

    /// <summary>Die erlaubten Zielknoten der obersten Ebene.</summary>
    public required ModificationFlowNodeDto[] Targets { get; init; }
}

/// <summary>Das Ergebnis des Eingriffs.</summary>
public sealed class InstanceModificationResultDto
{
    public required Guid InstanceId { get; init; }
    public required bool Modified { get; init; }

    /// <summary>Was der Eingriff mitgenommen hat; dieselben Codes wie im Trockenlauf.</summary>
    public required InstanceModificationFindingDto[] Notices { get; init; }

    /// <summary>Die Instanz nach dem Eingriff, damit die Oberflaeche sofort den neuen Stand zeigt.</summary>
    public required ProcessInstanceInfoDto Instance { get; init; }
}
