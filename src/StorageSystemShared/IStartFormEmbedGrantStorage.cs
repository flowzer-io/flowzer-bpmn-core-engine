namespace StorageSystem;

/// <summary>Startformular-Anzeigefreigaben; keine künstlichen Human-Task-Fremdschlüssel.</summary>
public interface IStartFormEmbedGrantStorage
{
    /// <summary>Atomare Ausgabe mit höchstens vier Freigaben je stabiler Person/Version.</summary>
    Task Create(StartFormEmbedGrant grant, DateTimeOffset utcNow);
    /// <summary>Sperrfreie Vorprüfung, keine Rechtezusage.</summary>
    Task<StartFormEmbedGrant?> Find(string secretHash, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    /// <summary>Abgelaufene Freigaben ohne Engine-/Aufgabenlocks bereinigen.</summary>
    Task CleanupExpired(DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    /// <summary>Genau ein Verbraucher je Hash, auch über mehrere API-Prozesse.</summary>
    Task<StartFormEmbedGrant?> TryTake(string secretHash, DateTimeOffset utcNow);
}

/// <summary>Drittadapter bleiben kompatibel; Start-Einbettung erteilt ohne Speicher kein Recht.</summary>
public sealed class UnsupportedStartFormEmbedGrantStorage : IStartFormEmbedGrantStorage
{
    public static UnsupportedStartFormEmbedGrantStorage Instance { get; } = new();
    public Task Create(StartFormEmbedGrant grant, DateTimeOffset utcNow) => throw new NotSupportedException("This adapter does not support start form embedding.");
    public Task<StartFormEmbedGrant?> Find(string secretHash, DateTimeOffset utcNow, CancellationToken cancellationToken = default) => Task.FromResult<StartFormEmbedGrant?>(null);
    public Task CleanupExpired(DateTimeOffset utcNow, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<StartFormEmbedGrant?> TryTake(string secretHash, DateTimeOffset utcNow) => Task.FromResult<StartFormEmbedGrant?>(null);
}
