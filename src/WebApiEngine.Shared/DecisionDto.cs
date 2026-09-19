using System.Dynamic;
using System.Text.Json.Serialization;

namespace WebApiEngine.Shared;

/// <summary>Eine Entscheidung innerhalb einer Entscheidungsdatei.</summary>
public sealed class DecisionSummaryDto
{
    /// <summary>Die Kennung aus <c>dmn:decision/@id</c>.</summary>
    public required string DecisionId { get; init; }

    /// <summary>Der Anzeigename; ohne Namen im DMN die Kennung.</summary>
    public required string Name { get; init; }
}

/// <summary>
/// Ein Katalogeintrag in seiner juengsten Fassung — ohne XML, damit der Katalog nicht jede
/// Datei mitliefert.
/// </summary>
public class DecisionDefinitionDto
{
    public required string DecisionDefinitionId { get; init; }
    public required string Name { get; init; }
    public required int Version { get; init; }
    public required DateTimeOffset DeployedAt { get; init; }

    /// <summary>
    /// Die Person, die diesen Stand gespeichert hat; <c>null</c> ohne aufgeloesten
    /// Benutzerkontext. Bewusst auch dann im JSON, wenn es <c>null</c> ist: Die Konsole
    /// unterscheidet „niemand bekannt" von „Feld fehlt".
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public Guid? DeployedBy { get; init; }

    public required IReadOnlyList<DecisionSummaryDto> Decisions { get; init; }
}

/// <summary>Wie <see cref="DecisionDefinitionDto"/>, zusaetzlich mit dem DMN-XML.</summary>
public sealed class DecisionDefinitionDetailDto : DecisionDefinitionDto
{
    public required string Xml { get; init; }
}

/// <summary>Ein Eintrag der Versionsliste; das XML holt erst der gezielte Versionsabruf.</summary>
public sealed class DecisionDefinitionVersionDto
{
    public required int Version { get; init; }
    public required DateTimeOffset DeployedAt { get; init; }

    /// <inheritdoc cref="DecisionDefinitionDto.DeployedBy"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public Guid? DeployedBy { get; init; }

    public required IReadOnlyList<DecisionSummaryDto> Decisions { get; init; }
}

/// <summary>Der Rumpf beim Anlegen und beim Versionieren einer Entscheidungsdatei.</summary>
public sealed class SaveDecisionDefinitionRequestDto
{
    /// <summary>
    /// Der Anzeigename. Ohne Angabe gilt <c>dmn:definitions/@name</c>, sonst die Kennung.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>Das vollstaendige DMN-Dokument.</summary>
    public string? Xml { get; init; }
}

/// <summary>Der Rumpf eines Trockenlaufs.</summary>
public sealed class EvaluateDecisionRequestDto
{
    /// <summary>Die Kennung der Entscheidung, die gerechnet werden soll.</summary>
    public string? DecisionId { get; init; }

    /// <summary>Die Variablen, die der Entscheidung zur Verfuegung stehen.</summary>
    [JsonConverter(typeof(ExpandoObjectConverter))]
    public ExpandoObject? Variables { get; init; }
}

/// <summary>
/// Das Ergebnis eines Trockenlaufs. Er laeuft ohne Instanz und ohne Seiteneffekt — er
/// speichert nichts und startet nichts.
/// </summary>
public class DecisionEvaluationResultDto
{
    public required string DecisionId { get; init; }

    /// <summary>
    /// Der Wert gemaess Trefferregel. Bewusst auch dann im JSON, wenn er <c>null</c> ist:
    /// „keine Regel hat getroffen" ist eine Auskunft, kein fehlendes Feld.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public object? Value { get; init; }

    public required IReadOnlyList<string> MatchedRules { get; init; }
}

/// <summary>
/// Das vollstaendige Ergebnis der ausgewerteten Entscheidung samt den Zwischenergebnissen
/// der Entscheidungen, von denen sie abhaengt.
/// </summary>
public sealed class DecisionEvaluationDto : DecisionEvaluationResultDto
{
    public required IReadOnlyDictionary<string, DecisionEvaluationResultDto> RequiredResults { get; init; }
}
