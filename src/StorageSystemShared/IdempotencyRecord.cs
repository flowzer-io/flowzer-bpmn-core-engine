namespace StorageSystem;

/// <summary>
/// Persistierter HTTP-Wiederholungsvertrag. Scope und Inhalt sind SHA-256-Hashes;
/// Clientschlüssel, Akteur und Requestdaten werden nicht im Klartext gespeichert.
/// </summary>
public sealed class IdempotencyRecord
{
    public required string ScopeHash { get; init; }
    public required string RequestHash { get; init; }
    public required string Operation { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime ExpiresAt { get; init; }
    public bool IsCompleted { get; set; }
    public Guid? ProcessInstanceId { get; set; }
}
