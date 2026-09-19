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
            // Offene Datensätze können nach einer nichttransaktionalen Teilmutation einen
            // unklaren Ausgang markieren. Sie dürfen nicht zeitgesteuert verschwinden,
            // weil derselbe Retry den Facheffekt danach duplizieren könnte.
            if (record is { IsCompleted: true } && record.ExpiresAt <= utcNow)
                StorageFile.DeleteIfExists(entry.Path);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Nimmt auch offene Reservierungen mit: Die Instanz, deren Doppelausloesung sie verhindern
    /// sollen, gibt es nach dem Loeschen nicht mehr.
    /// </summary>
    public Task<int> DeleteByProcessInstance(Guid processInstanceId)
    {
        if (processInstanceId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(processInstanceId));
        var deleted = 0;
        foreach (var entry in StorageFile.ReadExistingFiles(_path, "*.json"))
        {
            var record = JsonConvert.DeserializeObject<IdempotencyRecord>(entry.Content, _settings);
            if (record?.ProcessInstanceId != processInstanceId) continue;
            StorageFile.DeleteIfExists(entry.Path);
            deleted++;
        }

        return Task.FromResult(deleted);
    }

    private string PathOf(string hash) => Path.Combine(_path, $"idempotency_{hash}.json");
}
