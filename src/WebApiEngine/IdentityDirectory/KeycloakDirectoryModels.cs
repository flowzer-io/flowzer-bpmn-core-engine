namespace WebApiEngine.IdentityDirectory;

/// <summary>Transportmodell eines vollständig eingelesenen Keycloak-Verzeichnisses.</summary>
public sealed record KeycloakDirectorySnapshot(
    IReadOnlyList<KeycloakDirectoryUser> Users,
    IReadOnlyList<KeycloakDirectoryGroup> Groups);

/// <summary>Minimaler, stabiler Benutzerstand aus dem Keycloak-Admin-API.</summary>
public sealed record KeycloakDirectoryUser(
    string Subject,
    bool Enabled,
    string? Username,
    string? FirstName,
    string? LastName,
    IReadOnlyList<string> Groups);

/// <summary>Minimaler Gruppenstand aus dem Keycloak-Admin-API.</summary>
public sealed record KeycloakDirectoryGroup(string Id, string? Name, string? Path, string? ParentId);

/// <summary>Klassifiziert einen Fehler ohne Antwortkörper, Token oder Client-Secret preiszugeben.</summary>
public enum KeycloakAdminClientFailureKind
{
    Configuration,
    Authentication,
    Authorization,
    NotFound,
    Transient,
    InvalidResponse,
    Permanent
}

/// <summary>Sanitisierter Fehler des Keycloak-Leseclients.</summary>
public sealed class KeycloakAdminClientException(KeycloakAdminClientFailureKind kind, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public KeycloakAdminClientFailureKind Kind { get; } = kind;
}

/// <summary>Abstraktion für den Snapshot-Leser, damit der Synchronisierer ohne HTTP testbar bleibt.</summary>
public interface IKeycloakAdminClient
{
    Task<KeycloakDirectorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
