namespace WebApiEngine.Auth;

/// <summary>
/// Kapselt den aktuell aufgelösten Benutzerkontext der API.
/// Neben der technischen Id traegt er die Kennungen und Gruppen, unter denen ein BPMN-Modell
/// die Person meinen kann: Ein Modell schreibt <c>assignee="anna"</c> oder
/// <c>candidateGroups="buchhaltung"</c>, nicht die technische Id.
/// </summary>
public sealed record CurrentUserContext(
    Guid UserId,
    string Source,
    bool IsFallback)
{
    /// <summary>
    /// Alle Kennungen der Person: technische Id, Benutzername, E-Mail. Welche davon im Token
    /// steht, entscheidet der Identity Provider; die Zuweisungspruefung akzeptiert jede.
    /// </summary>
    public IReadOnlyCollection<string> Names { get; init; } = [];

    /// <summary>Gruppen aus dem <c>groups</c>-Claim, bei Keycloak als Pfade.</summary>
    public IReadOnlyCollection<string> Groups { get; init; } = [];
}
