using System.Text.RegularExpressions;

namespace StorageSystem;

/// <summary>
/// Vollstaendiger, erfolgreich gelesener Stand eines externen Identitaetsverzeichnisses.
/// Die Kennungen der Eintraege sind lokale, persistente IDs; sie werden beim Publizieren aus
/// stabilen externen Schluesseln abgeleitet und nicht aus einem Importlauf uebernommen.
/// </summary>
public sealed class DirectorySnapshot
{
    public required Guid GenerationId { get; set; }
    public required string Issuer { get; set; }
    public required DateTime CompletedAtUtc { get; set; }
    public List<DirectoryUser> Users { get; set; } = [];
    public List<DirectoryGroup> Groups { get; set; } = [];
    public List<DirectoryMembership> Memberships { get; set; } = [];
}

public enum DirectorySourceKind
{
    Keycloak = 0
}

/// <summary>Lokaler Benutzer mit einer unveraenderlichen, extern eindeutig belegten Herkunft.</summary>
public sealed class DirectoryUser
{
    public Guid Id { get; set; }
    public required DirectorySourceKind SourceKind { get; set; }
    public required string Issuer { get; set; }
    public required string Subject { get; set; }
    public required string DisplayName { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Lokale Gruppe. <see cref="ExternalId"/> und <see cref="Path"/> sind innerhalb eines
/// Issuers eindeutig; <see cref="ParentId"/> verweist immer auf die lokale Gruppen-ID.
/// </summary>
public sealed class DirectoryGroup
{
    public Guid Id { get; set; }
    public required DirectorySourceKind SourceKind { get; set; }
    public required string Issuer { get; set; }
    public required string ExternalId { get; set; }
    public required string Name { get; set; }
    public required string Path { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>Aktuelle Mitgliedschaft zwischen stabilen lokalen Benutzer- und Gruppenkennungen.</summary>
public sealed class DirectoryMembership
{
    public required Guid UserId { get; set; }
    public required Guid GroupId { get; set; }
}

public enum DirectorySyncState
{
    Disabled = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3
}

/// <summary>
/// Beobachtbarer Zustand der Verzeichnissynchronisation. Fehler werden beim Speichern begrenzt
/// und zeilenbereinigt, damit keine ungefilterten Providerantworten in Statusansichten gelangen.
/// </summary>
public sealed class DirectorySyncStatus
{
    private static readonly Regex AuthorizationHeaderPattern = new(@"(?i)\bauthorization\s*:\s*bearer\s+[^\s,;]+", RegexOptions.CultureInvariant);
    private static readonly Regex SensitiveValuePattern = new(@"(?i)\b(authorization|bearer|token|secret|password|client_secret)\b(?:\s*[:=]\s*|\s+)[^\s,;]+", RegexOptions.CultureInvariant);
    public DirectorySyncState State { get; set; }
    public string? Issuer { get; set; }
    public Guid? ActiveGenerationId { get; set; }
    public Guid? RunningGenerationId { get; set; }
    public DateTime? AttemptedAtUtc { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime? SucceededAtUtc { get; set; }
    public DateTime? FailedAtUtc { get; set; }
    public int UserCount { get; set; }
    public int GroupCount { get; set; }
    public int MembershipCount { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Bereinigt unzuverlaessige Providerfehler fuer den dauerhaft sichtbaren Status.</summary>
    public static string? SanitizeError(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        normalized = AuthorizationHeaderPattern.Replace(normalized, "authorization=[redacted]");
        normalized = SensitiveValuePattern.Replace(normalized, "$1=[redacted]");
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}
