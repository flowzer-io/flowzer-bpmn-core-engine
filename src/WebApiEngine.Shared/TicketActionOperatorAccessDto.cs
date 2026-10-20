using System.ComponentModel.DataAnnotations;

namespace WebApiEngine.Shared;

/// <summary>
/// Minimaler aktueller persönlicher Betriebsnachweis für den TT-Vermittler.
/// Kein Grant/Secret, keine Ticketrechte und ausdrücklich keine Wiederfreigabe oder Jobmutation.
/// </summary>
public sealed record TicketActionOperatorAccessDto(
    [property: Required] Guid ProcessInstanceId,
    [property: Required] string MetaDefinitionId,
    [property: Required] Guid DefinitionId,
    [property: Required] string InitiatorIssuer,
    [property: Required] string InitiatorSubject,
    [property: Required] string ActorIssuer,
    [property: Required] string ActorSubject,
    [property: Required] string ActorAuthorizedClientId,
    [property: Required] DateTimeOffset CheckedAtUtc);
