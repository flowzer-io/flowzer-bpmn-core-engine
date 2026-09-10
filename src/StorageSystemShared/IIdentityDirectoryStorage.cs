namespace StorageSystem;

/// <summary>
/// Persistiert den zuletzt vollstaendig veroeffentlichten Identitaetsverzeichnis-Snapshot.
/// Ein fehlgeschlagener Import ersetzt nie den aktiven Snapshot.
/// </summary>
public interface IIdentityDirectoryStorage
{
    Task<DirectorySnapshot?> GetActiveSnapshot();
    Task<DirectorySyncStatus?> GetSyncStatus();
    /// <summary>
    /// Startet einen Lauf nur dann, wenn kein anderer noch eine gueltige Lease besitzt. So gilt
    /// Single-flight auch fuer mehrere API-Prozesse; eine abgelaufene Lease ermoeglicht Recovery.
    /// </summary>
    Task<bool> TryStartSync(string issuer, Guid generationId, DateTime startedAtUtc, DateTime leaseExpiresAtUtc);

    /// <summary>
    /// Markiert ausschließlich die angegebene laufende Generation als fehlgeschlagen. Ein
    /// verspaeteter Prozess darf den Status eines neueren Laufs nicht ueberschreiben.
    /// </summary>
    Task<bool> FailSync(string issuer, Guid generationId, string errorCode, string errorMessage, DateTime failedAtUtc);
    Task PublishSnapshot(DirectorySnapshot snapshot);
}

/// <summary>Kompatibler Default fuer Adapter, die noch kein Identitaetsverzeichnis anbieten.</summary>
public sealed class UnsupportedIdentityDirectoryStorage : IIdentityDirectoryStorage
{
    public static UnsupportedIdentityDirectoryStorage Instance { get; } = new();
    private static NotSupportedException Error() => new("This storage adapter does not support identity-directory synchronization.");
    public Task<DirectorySnapshot?> GetActiveSnapshot() => throw Error();
    public Task<DirectorySyncStatus?> GetSyncStatus() => throw Error();
    public Task<bool> TryStartSync(string issuer, Guid generationId, DateTime startedAtUtc, DateTime leaseExpiresAtUtc) => throw Error();
    public Task<bool> FailSync(string issuer, Guid generationId, string errorCode, string errorMessage, DateTime failedAtUtc) => throw Error();
    public Task PublishSnapshot(DirectorySnapshot snapshot) => throw Error();
}
