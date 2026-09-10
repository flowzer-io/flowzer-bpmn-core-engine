namespace WebApiEngine.Shared;

/// <summary>Datensparsame Browser-Session ohne OIDC-Token oder Provider-Rohclaims.</summary>
public sealed record BffSessionDto(
    string Id,
    string Name,
    string? Email,
    IReadOnlyCollection<string> Capabilities);

/// <summary>Request-Token und Headername fuer den naechsten schreibenden Browser-Aufruf.</summary>
public sealed record BffCsrfDto(string RequestToken, string HeaderName);
