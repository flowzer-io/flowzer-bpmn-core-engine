using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;

/// <summary>Dateibasierter Entwicklungsweg; atomare Dateien, aber kein Mehrprozess-Lock.</summary>
internal sealed class IdempotencyStorage(Storage storage) : IIdempotencyStorage
{
    private readonly string _path = storage.GetBasePath("FileStorage/Idempotency");
    private readonly JsonSerializerSettings _settings = storage.NewtonSoftDefaultSettings;

    public async Task<IdempotencyRecord?> Get(string scopeHash)
    {
        var content = await StorageFile.ReadAllTextIfExistsAsync(PathOf(scopeHash));
        return content is null ? null : JsonConvert.DeserializeObject<IdempotencyRecord>(content, _settings);
    }

    public async Task<bool> TryCreate(IdempotencyRecord record)
    {
        var path = PathOf(record.ScopeHash);
        if (File.Exists(path)) return false;
        // Die API serialisiert lokale Mutationen; das Dateisystem ist ausdrücklich kein
        // sicherer Mehrprozesspfad. Atomisch schreiben verhindert nur Teildokumente.
        await StorageFile.WriteAllTextAtomicAsync(path, JsonConvert.SerializeObject(record, _settings));
        return true;
    }

    public async Task Complete(string scopeHash, Guid? processInstanceId)
    {
        var record = await Get(scopeHash) ?? throw new InvalidOperationException("The idempotency reservation was lost.");
        record.IsCompleted = true;
        record.ProcessInstanceId = processInstanceId;
        await StorageFile.WriteAllTextAtomicAsync(PathOf(scopeHash), JsonConvert.SerializeObject(record, _settings));
    }

    public Task Remove(string scopeHash) { StorageFile.DeleteIfExists(PathOf(scopeHash)); return Task.CompletedTask; }
    public Task DeleteExpired(DateTime utcNow)
    {
        foreach (var entry in StorageFile.ReadExistingFiles(_path, "*.json"))
        {
            var record = JsonConvert.DeserializeObject<IdempotencyRecord>(entry.Content, _settings);
            if (record is null || record.ExpiresAt <= utcNow) StorageFile.DeleteIfExists(entry.Path);
        }
        return Task.CompletedTask;
    }

    private string PathOf(string hash) => Path.Combine(_path, $"idempotency_{hash}.json");
}
