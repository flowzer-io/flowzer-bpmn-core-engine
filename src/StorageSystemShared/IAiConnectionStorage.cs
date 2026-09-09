using Model;

namespace StorageSystem;

/// <summary>
/// Revisionsgeschuetzte Ablage nicht geheimer KI-Verbindungsmetadaten. Implementierungen
/// muessen Namens- und Revisionskonflikte innerhalb derselben atomaren Schreiboperation pruefen.
/// </summary>
public interface IAiConnectionStorage
{
    Task<IReadOnlyList<AiConnection>> List();
    Task<AiConnection?> Get(Guid id);
    Task<AiConnectionWriteResult> TryCreate(AiConnection connection);
    Task<AiConnectionWriteResult> TryUpdate(AiConnection connection, long expectedRevision);
}

public enum AiConnectionWriteStatus
{
    Written,
    NotFound,
    Conflict
}

public sealed record AiConnectionWriteResult(
    AiConnectionWriteStatus Status,
    AiConnection? Connection,
    long CurrentRevision);

/// <summary>Kompatibilitaetswache fuer noch nicht auf den neuen Vertrag umgestellte Testdoppel.</summary>
internal sealed class UnsupportedAiConnectionStorage : IAiConnectionStorage
{
    public static UnsupportedAiConnectionStorage Instance { get; } = new();
    private UnsupportedAiConnectionStorage() { }

    public Task<IReadOnlyList<AiConnection>> List() => throw Unsupported();
    public Task<AiConnection?> Get(Guid id) => throw Unsupported();
    public Task<AiConnectionWriteResult> TryCreate(AiConnection connection) => throw Unsupported();
    public Task<AiConnectionWriteResult> TryUpdate(AiConnection connection, long expectedRevision) => throw Unsupported();

    private static NotSupportedException Unsupported() =>
        new("This storage implementation does not support AI connections.");
}
