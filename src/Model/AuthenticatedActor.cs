namespace Model;

/// <summary>
/// Interner auditierter Akteur einer authentisierten Aktion: stabile persönliche
/// Identität, tatsächlich aufgelöste Benutzer-GUID und verifizierter OIDC-Vermittler.
/// Kein Secret, Besitzrecht oder allgemein öffentliches API-DTO.
/// </summary>
public sealed record AuthenticatedActor(AuthenticatedSubject Identity, Guid UserId, string? ClientId);
