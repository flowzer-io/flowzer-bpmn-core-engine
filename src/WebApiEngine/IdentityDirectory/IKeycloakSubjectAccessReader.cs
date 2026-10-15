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

    /// <summary>
    /// Prüft Flowzer- und TT-API-Zugang derselben Person in einem gemeinsamen
    /// Gesamtbudget. Profil und Scope werden einmal gelesen; beide effektiven
    /// Clientrollen müssen aktuell gelten (TT: feste Rolle <c>access</c>). Die Clients sind verschieden und
    /// installationsgebunden; es gibt keinen Worker-/JWT-/Directory-Fallback.
    /// </summary>
    Task<bool> HasCurrentTicketActionAccessAsync(AuthenticatedSubject identity,
        string flowzerClientId, string flowzerRequiredRole, string tickyTaskClientId,
        CancellationToken cancellationToken);
}
