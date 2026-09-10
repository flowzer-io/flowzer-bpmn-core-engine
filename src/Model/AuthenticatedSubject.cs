namespace Model;

/// <summary>
/// Verifizierte Authentifizierungsidentität. Anzeigenamen und E-Mail-Adressen sind
/// ausdrücklich kein Bestandteil des Besitznachweises; Vergleiche sind ordinal.
/// </summary>
public sealed record AuthenticatedSubject(string Issuer, string Subject);
