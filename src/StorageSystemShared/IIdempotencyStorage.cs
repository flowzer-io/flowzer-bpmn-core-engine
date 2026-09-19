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

    /// <summary>
    /// Entfernt die Wiederholungsverträge, die auf eine Instanz zeigen, und liefert deren
    /// Anzahl. Anders als <see cref="DeleteExpired"/> gehen offene Reservierungen mit: Was sie
    /// schützen — den Vorgang nicht ein zweites Mal auszulösen — gibt es nach dem Löschen der
    /// Instanz nicht mehr, und ein Verweis auf eine nicht mehr vorhandene Instanz ist ein Rest.
    ///
    /// Bewusst ohne stillen Standard, wie beim Löschen einer Instanz.
    /// </summary>
    Task<int> DeleteByProcessInstance(Guid processInstanceId) =>
        throw new NotSupportedException($"{GetType().Name} does not support deleting idempotency records of an instance.");
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
    public Task<int> DeleteByProcessInstance(Guid processInstanceId) => throw Error();
}
