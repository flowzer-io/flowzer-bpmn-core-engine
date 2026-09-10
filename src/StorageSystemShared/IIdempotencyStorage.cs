namespace StorageSystem;

public interface IIdempotencyStorage
{
    Task<IdempotencyRecord?> Get(string scopeHash);
    Task<bool> TryCreate(IdempotencyRecord record);
    Task Complete(string scopeHash, Guid? processInstanceId);
    Task Remove(string scopeHash);

    /// <summary>
    /// Entfernt abgelaufene, abgeschlossene Ergebnisse. Offene Reservierungen bleiben
    /// erhalten, da sie bei nichttransaktionaler Ablage einen unklaren Ausgang markieren.
    /// </summary>
    Task DeleteExpired(DateTime utcNow);
}

/// <summary>Kompatibler Default für ältere Test-/Drittadapter ohne HTTP-Idempotenz.</summary>
public sealed class UnsupportedIdempotencyStorage : IIdempotencyStorage
{
    public static UnsupportedIdempotencyStorage Instance { get; } = new();
    private static NotSupportedException Error() => new("This storage adapter does not support HTTP idempotency.");
    public Task<IdempotencyRecord?> Get(string scopeHash) => throw Error();
    public Task<bool> TryCreate(IdempotencyRecord record) => throw Error();
    public Task Complete(string scopeHash, Guid? processInstanceId) => throw Error();
    public Task Remove(string scopeHash) => throw Error();
    public Task DeleteExpired(DateTime utcNow) => throw Error();
}
