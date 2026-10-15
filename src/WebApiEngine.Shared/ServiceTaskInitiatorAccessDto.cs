using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace WebApiEngine.Shared;

/// <summary>Besitznachweis des Workers, ohne wählbare Person, Rolle oder Client-Audience.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ServiceTaskInitiatorAccessRequestDto
{
    [Required, MaxLength(128)]
    public required string WorkerId { get; init; }
}

/// <summary>
/// Frischer Flowzer- UND TT-API-Zugangsstand des gespeicherten Initiators, gebunden an einen eigenen aktiven
/// Auftrag. Kein übertragbarer Grant, keine Formular-/Profilinhalte und kein Token.
/// </summary>
public sealed record ServiceTaskInitiatorAccessDto(
    [property: Required] Guid JobId,
    [property: Required] Guid ProcessInstanceId,
    [property: Required] string MetaDefinitionId,
    [property: Required] Guid DefinitionId,
    [property: Required] Guid TokenId,
    [property: Required] string FlowNodeId,
    [property: Required] string Type,
    [property: Required] string InitiatorIssuer,
    [property: Required] string InitiatorSubject,
    [property: Required] bool Allowed,
    [property: Required] DateTimeOffset CheckedAtUtc);
