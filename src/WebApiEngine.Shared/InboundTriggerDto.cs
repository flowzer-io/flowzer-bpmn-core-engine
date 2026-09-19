using System.Text.Json.Serialization;

namespace WebApiEngine.Shared;

/// <summary>Was ein Aufruf des Auslösers bewirkt.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InboundTriggerKindDto>))]
public enum InboundTriggerKindDto
{
    [JsonStringEnumMemberName("start")]
    Start,

    [JsonStringEnumMemberName("message")]
    Message
}

/// <summary>Wie aus dem Körper des Aufrufs Prozessvariablen werden.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InboundTriggerVariablesModeDto>))]
public enum InboundTriggerVariablesModeDto
{
    /// <summary>Nur die ausdrücklich genannten obersten Felder — Standard und datensparsam.</summary>
    [JsonStringEnumMemberName("fields")]
    Fields,

    /// <summary>Der ganze Körper als eine Variable <c>payload</c>.</summary>
    [JsonStringEnumMemberName("body")]
    Body
}

/// <summary>
/// Ein Auslöser, wie ihn die Verwaltung sieht. Das Geheimnis fehlt hier absichtlich und
/// dauerhaft: Es wird genau einmal gezeigt, in der Antwort auf das Anlegen oder das Erneuern.
/// </summary>
public sealed record InboundTriggerDto(
    Guid Id,
    string Key,
    string Name,
    InboundTriggerKindDto Kind,
    string? DefinitionId,
    string? MessageName,
    string? CorrelationKeyPath,
    InboundTriggerVariablesModeDto VariablesMode,
    string[] AllowedFields,
    bool Enabled,
    DateTime CreatedAt,
    DateTime? LastUsedAt,
    long UseCount,
    DateTime? LastFailureAt,
    string? LastFailureReason);

/// <summary>
/// Die einzige Antwort, die das Geheimnis im Klartext enthält. Sie entsteht beim Anlegen und
/// beim Erneuern; ein späteres Lesen liefert sie nicht erneut.
/// </summary>
public sealed record InboundTriggerSecretDto(InboundTriggerDto Trigger, string Secret);

/// <summary>Angaben zum Anlegen eines Auslösers.</summary>
public sealed record CreateInboundTriggerRequestDto
{
    public string Name { get; init; } = string.Empty;

    public InboundTriggerKindDto Kind { get; init; }

    /// <summary>Katalogkennung des Workflows; erforderlich bei <c>start</c>.</summary>
    public string? DefinitionId { get; init; }

    /// <summary>Name der BPMN-Nachricht; erforderlich bei <c>message</c>.</summary>
    public string? MessageName { get; init; }

    /// <summary>JSON-Pfad in Punktnotation (<c>order.id</c>); erforderlich bei <c>message</c>.</summary>
    public string? CorrelationKeyPath { get; init; }

    public InboundTriggerVariablesModeDto VariablesMode { get; init; } = InboundTriggerVariablesModeDto.Fields;

    public string[]? AllowedFields { get; init; }
}

/// <summary>
/// Änderbare Angaben eines bestehenden Auslösers. Art und Schlüssel stehen nicht darin: Wer das
/// Ziel wechseln will, legt einen neuen Auslöser an, statt einem bereits verteilten Schlüssel
/// still eine andere Bedeutung zu geben.
/// </summary>
public sealed record UpdateInboundTriggerRequestDto
{
    public string Name { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public string? DefinitionId { get; init; }

    public string? MessageName { get; init; }

    public string? CorrelationKeyPath { get; init; }

    public InboundTriggerVariablesModeDto VariablesMode { get; init; } = InboundTriggerVariablesModeDto.Fields;

    public string[]? AllowedFields { get; init; }
}

/// <summary>Antwort eines erfolgreichen Aufrufs mit der Art <c>start</c>.</summary>
public sealed record InboundTriggerStartResultDto(Guid InstanceId);

/// <summary>
/// Antwort eines erfolgreichen Aufrufs mit der Art <c>message</c>. <c>correlated: false</c> ist
/// kein Fehler: Wartete keine Instanz, wäre eine Fehlermeldung für den Aufrufer ein Weg
/// herauszufinden, welche Vorgänge es in dieser Installation gibt.
/// </summary>
public sealed record InboundTriggerMessageResultDto(bool Correlated);
