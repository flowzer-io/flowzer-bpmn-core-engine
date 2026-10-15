namespace StorageSystem;

/// <summary>Persistenter Einmal-Einstieg; Einlösung ist atomar auch zwischen API-Prozessen.</summary>
public interface IFormEmbedGrantStorage
{
    /// <summary>Legt einen Hash an; höchstens vier Freigaben je Aufgabe bleiben erhalten.</summary>
    Task Create(FormEmbedGrant grant, DateTimeOffset utcNow);
    /// <summary>Sperrfreie Vorprüfung, kein Verbrauch und keine Autorisierungszusage.</summary>
    Task<FormEmbedGrant?> Find(string secretHash, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    /// <summary>Bereinigt abgelaufene Bindungen außerhalb von Aufgaben-/Engine-Sperren.</summary>
    Task CleanupExpired(DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    /// <summary>Entfernt und liefert höchstens einmal einen noch gültigen Einstieg.</summary>
    Task<FormEmbedGrant?> TryTake(string secretHash, DateTimeOffset utcNow);
}

/// <summary>Alte Drittadapter bleiben kompatibel, unterstützen aber keine Einbettung.</summary>
public sealed class UnsupportedFormEmbedGrantStorage : IFormEmbedGrantStorage
{
    public static UnsupportedFormEmbedGrantStorage Instance { get; } = new();
    public Task Create(FormEmbedGrant grant, DateTimeOffset utcNow) =>
        throw new NotSupportedException("This adapter does not support form embedding.");
    public Task<FormEmbedGrant?> Find(string secretHash, DateTimeOffset utcNow, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This adapter does not support form embedding.");
    public Task CleanupExpired(DateTimeOffset utcNow, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This adapter does not support form embedding.");
    public Task<FormEmbedGrant?> TryTake(string secretHash, DateTimeOffset utcNow) =>
        throw new NotSupportedException("This adapter does not support form embedding.");
}
