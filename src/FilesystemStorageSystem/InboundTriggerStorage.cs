using Model;
using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;

/// <summary>
/// Einzelprozess-Entwicklungsablage für Auslöser. Ein gemeinsames Gate hält Eindeutigkeit des
/// Schlüssels und das Hochzählen zusammen; mehrere API-Prozesse brauchen PostgreSQL, weil dieses
/// Gate nur innerhalb eines Prozesses gilt.
/// </summary>
internal sealed class InboundTriggerStorage(Storage storage) : IInboundTriggerStorage
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _path = storage.GetBasePath(Path.Combine("FileStorage", "InboundTriggers"));

    public Task<IReadOnlyList<InboundTrigger>> GetAll()
    {
        IReadOnlyList<InboundTrigger> result = ReadAll()
            .OrderBy(trigger => trigger.CreatedAt)
            .ThenBy(trigger => trigger.Id)
            .ToArray();
        return Task.FromResult(result);
    }

    public async Task<InboundTrigger?> Get(Guid id)
    {
        var content = await StorageFile.ReadAllTextIfExistsAsync(File(id));
        return content is null ? null : Deserialize(content);
    }

    public Task<InboundTrigger?> GetByKey(string key)
    {
        // Bewusst ohne Filter auf Enabled: Ein abgeschalteter Auslöser muss von außen genauso
        // aussehen wie ein unbekannter, und diese Entscheidung trifft der Aufrufer.
        var match = ReadAll().FirstOrDefault(trigger => string.Equals(trigger.Key, key, StringComparison.Ordinal));
        return Task.FromResult(match);
    }

    public async Task Save(InboundTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        await Gate.WaitAsync();
        try
        {
            var duplicate = ReadAll().Any(other =>
                other.Id != trigger.Id && string.Equals(other.Key, trigger.Key, StringComparison.Ordinal));
            if (duplicate)
            {
                throw new InvalidOperationException(
                    $"Another inbound trigger already uses the key \"{trigger.Key}\".");
            }

            // Zähler und Fehlerfelder kommen vom gespeicherten Stand, nicht vom Aufrufer: Eine
            // Verwaltungsänderung darf den Nutzungsstand nicht auf den Stand zurücksetzen, den
            // die Oberfläche zufällig geladen hatte.
            var current = await Get(trigger.Id);
            if (current is not null)
            {
                trigger.CreatedAt = current.CreatedAt;
                trigger.CreatedBy = current.CreatedBy;
                trigger.LastUsedAt = current.LastUsedAt;
                trigger.UseCount = current.UseCount;
                trigger.LastFailureAt = current.LastFailureAt;
                trigger.LastFailureReason = current.LastFailureReason;
            }

            await StorageFile.WriteAllTextAtomicAsync(File(trigger.Id), Serialize(trigger));
        }
        finally { Gate.Release(); }
    }

    public async Task<bool> Remove(Guid id)
    {
        await Gate.WaitAsync();
        try
        {
            var path = File(id);
            if (!System.IO.File.Exists(path)) return false;
            StorageFile.DeleteIfExists(path);
            return true;
        }
        finally { Gate.Release(); }
    }

    public Task RecordUse(Guid id, DateTime usedAt) => Mutate(id, trigger =>
    {
        trigger.UseCount += 1;
        trigger.LastUsedAt = usedAt;
    });

    public Task RecordFailure(Guid id, DateTime failedAt, string reason) => Mutate(id, trigger =>
    {
        trigger.LastFailureAt = failedAt;
        trigger.LastFailureReason = reason;
    });

    /// <summary>
    /// Lesen, Ändern und Schreiben unter demselben Gate wie <see cref="Save"/>. Ohne das
    /// zählten zwei gleichzeitige Aufrufe desselben Auslösers als einer.
    /// </summary>
    private async Task Mutate(Guid id, Action<InboundTrigger> change)
    {
        await Gate.WaitAsync();
        try
        {
            var current = await Get(id);
            if (current is null) return;
            change(current);
            await StorageFile.WriteAllTextAtomicAsync(File(id), Serialize(current));
        }
        finally { Gate.Release(); }
    }

    private IEnumerable<InboundTrigger> ReadAll() =>
        StorageFile.ReadExistingFiles(_path, "*.json").Select(entry => Deserialize(entry.Content));

    private string File(Guid id) => Path.Combine(_path, $"{id:N}.json");

    private string Serialize(InboundTrigger trigger) =>
        JsonConvert.SerializeObject(trigger, storage.NewtonSoftDefaultSettings);

    private InboundTrigger Deserialize(string content) =>
        JsonConvert.DeserializeObject<InboundTrigger>(content, storage.NewtonSoftDefaultSettings)
        ?? throw new InvalidDataException("Stored inbound trigger is empty.");
}
