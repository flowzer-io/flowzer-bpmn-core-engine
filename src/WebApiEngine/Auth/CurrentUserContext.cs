namespace WebApiEngine.Auth;

/// <summary>
/// Kapselt den aktuell aufgelösten Benutzerkontext der API.
/// Solange noch keine vollständige Authentifizierung aktiv ist, kann die API
/// damit sauber zwischen Header-/Claim-basierten Benutzern und einem
/// kontrollierten System-Fallback unterscheiden.
/// </summary>
public sealed record CurrentUserContext(
    Guid UserId,
    string Source,
    bool IsFallback);
