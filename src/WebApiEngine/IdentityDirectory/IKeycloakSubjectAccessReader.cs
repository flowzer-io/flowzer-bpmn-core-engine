using Model;

namespace WebApiEngine.IdentityDirectory;

/// <summary>
/// Liest den aktuellen Zugang genau einer verifizierten Person. Kein Tokenclaim, kein
/// periodischer Directory-Snapshot und keine Workeridentität ersetzen diesen Live-Stand.
/// </summary>
public interface IKeycloakSubjectAccessReader
{
    /// <summary>
    /// Prüft aktives Konto, Mitgliedschaft im konfigurierten Teilbaum und effektive
    /// Clientrolle. Nur ein sicher festgestellter Entzug liefert false; Quellfehler werfen.
    /// Client und Rolle stammen aus der Installation, niemals aus einem HTTP-Anfragekörper.
    /// </summary>
    Task<bool> HasCurrentAccessAsync(AuthenticatedSubject identity, string clientId, string requiredRole,
        CancellationToken cancellationToken);
}
