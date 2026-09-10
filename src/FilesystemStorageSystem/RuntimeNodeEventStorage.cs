using Model;
using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;

/// <summary>
/// Einprozess-Entwicklungsablage für unveränderliche Runtime-Knotenereignisse. Die PostgreSQL-
/// Implementierung ist für Mehrprozessbetrieb vorgesehen; die Dateiablage verhindert dennoch
/// lokale Read-Modify-Write-Rennen und bewahrt die Idempotenz auch nach einem Neustart.
/// </summary>
internal sealed class RuntimeNodeEventStorage(Storage storage) : IRuntimeNodeEventStorage
{
    internal const string DirectoryName = "RuntimeNodeEvents";
    // Der Entwicklungsadapter serialisiert diese kurzen Dateianlagen global. Ein Lock pro
    // append-only Ereignis-ID würde andernfalls bis zum Prozessende unbeschränkt wachsen.
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _path = storage.GetBasePath(Path.Combine("FileStorage", DirectoryName));

    public async Task<bool> AppendIfAbsent(RuntimeNodeEvent runtimeEvent)
    {
        RuntimeNodeEventContract.Validate(runtimeEvent);
        await Gate.WaitAsync();
        try
        {
            var path = EventFile(runtimeEvent.Id);
            if (File.Exists(path)) return false;

            try
            {
                await StorageFile.WriteAllTextNewAtomicAsync(path,
                    JsonConvert.SerializeObject(runtimeEvent, storage.NewtonSoftDefaultSettings));
                return true;
            }
            catch (IOException) when (File.Exists(path))
            {
                // Ein anderer Prozess kann zwischen Exists und Move gewonnen haben. Das ist
                // dieselbe erfolgreiche Wiederholung wie innerhalb dieses Prozesses.
                return false;
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<IReadOnlyList<RuntimeNodeEvent>> GetByProcessInstance(Guid processInstanceId)
    {
        if (processInstanceId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(processInstanceId));
        var result = new List<RuntimeNodeEvent>();
        foreach (var file in Directory.EnumerateFiles(_path, "event_*.json"))
        {
            var content = await StorageFile.ReadAllTextIfExistsAsync(file);
            if (content is null) continue;
            var item = JsonConvert.DeserializeObject<RuntimeNodeEvent>(content, storage.NewtonSoftDefaultSettings)
                       ?? throw new InvalidDataException("Stored runtime node event is empty.");
            if (item.ProcessInstanceId == processInstanceId) result.Add(item);
        }

        return result.OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.TokenId)
            .ThenBy(item => item.Id)
            .ToArray();
    }

    private string EventFile(Guid id) => Path.Combine(_path, $"event_{id:N}.json");
}
