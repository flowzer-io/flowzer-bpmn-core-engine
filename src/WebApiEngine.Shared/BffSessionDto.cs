namespace WebApiEngine.Shared;

/// <summary>Datensparsame Browser-Session ohne OIDC-Token oder Provider-Rohclaims.</summary>
public sealed record BffSessionDto(
    string Id,
    string Name,
    string? Email,
    IReadOnlyCollection<string> Capabilities);

/// <summary>Request-Token und Headername fuer den naechsten schreibenden Browser-Aufruf.</summary>
public sealed record BffCsrfDto(string RequestToken, string HeaderName);

/// <summary>
/// Antwort auf eine Abmeldung, die auch die Sitzung beim Identity Provider beenden soll:
/// Adresse des Provider-Logouts, zu der die Konsole den Browser schickt.
/// </summary>
public sealed record BffLogoutResponseDto(string? RedirectTo);
