using Model;
using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;

/// <summary>
/// Einzelprozess-Entwicklungsablage fuer KI-Verbindungsmetadaten. Ein gemeinsames Gate schuetzt
/// Revision und installationsweit eindeutigen Namen; mehrere API-Prozesse brauchen PostgreSQL.
/// </summary>
internal sealed class AiConnectionStorage(Storage storage) : IAiConnectionStorage
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _path = storage.GetBasePath(Path.Combine("FileStorage", "AiConnections"));

    public Task<IReadOnlyList<AiConnection>> List()
    {
        IReadOnlyList<AiConnection> result = StorageFile.ReadExistingFiles(_path, "*.json")
            .Select(entry => Deserialize(entry.Content))
            .OrderBy(connection => connection.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(connection => connection.Id)
            .ToArray();
        return Task.FromResult(result);
    }

    public async Task<AiConnection?> Get(Guid id)
    {
        ValidateId(id);
        var content = await StorageFile.ReadAllTextIfExistsAsync(File(id));
        return content is null ? null : Deserialize(content);
    }

    public async Task<AiConnectionWriteResult> TryCreate(AiConnection connection)
    {
        Validate(connection, expectedRevision: 0);
        await Gate.WaitAsync();
        try
        {
            var current = await Get(connection.Id);
            if (current is not null || await NameExists(connection.Name, exceptId: null))
            {
                return new AiConnectionWriteResult(
                    AiConnectionWriteStatus.Conflict,
                    current,
                    current?.Revision ?? 0);
            }

            await StorageFile.WriteAllTextNewAtomicAsync(File(connection.Id), Serialize(connection));
            return new AiConnectionWriteResult(
                AiConnectionWriteStatus.Written,
                connection,
                connection.Revision);
        }
        catch (IOException)
        {
            var current = await Get(connection.Id);
            return new AiConnectionWriteResult(
                AiConnectionWriteStatus.Conflict,
                current,
                current?.Revision ?? 0);
        }
        finally { Gate.Release(); }
    }

    public async Task<AiConnectionWriteResult> TryUpdate(AiConnection connection, long expectedRevision)
    {
        Validate(connection, expectedRevision);
        await Gate.WaitAsync();
        try
        {
            var current = await Get(connection.Id);
            if (current is null)
                return new AiConnectionWriteResult(AiConnectionWriteStatus.NotFound, null, 0);
            if (current.Revision != expectedRevision
                || await NameExists(connection.Name, connection.Id))
                return new AiConnectionWriteResult(
                    AiConnectionWriteStatus.Conflict,
                    current,
                    current.Revision);

            await StorageFile.WriteAllTextAtomicAsync(File(connection.Id), Serialize(connection));
            return new AiConnectionWriteResult(
                AiConnectionWriteStatus.Written,
                connection,
                connection.Revision);
        }
        finally { Gate.Release(); }
    }

    private async Task<bool> NameExists(string name, Guid? exceptId)
    {
        var all = await List();
        return all.Any(connection => connection.Id != exceptId
                                     && string.Equals(connection.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private string File(Guid id) => Path.Combine(_path, $"{id:N}.json");
    private string Serialize(AiConnection connection) =>
        JsonConvert.SerializeObject(connection, storage.NewtonSoftDefaultSettings);
    private AiConnection Deserialize(string content) =>
        JsonConvert.DeserializeObject<AiConnection>(content, storage.NewtonSoftDefaultSettings)
        ?? throw new InvalidDataException("Stored AI connection metadata is empty.");

    private static void Validate(AiConnection connection, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ValidateId(connection.Id);
        if (expectedRevision < 0 || connection.Revision != checked(expectedRevision + 1))
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.DefaultModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.SecretReference);
        if (connection.UpdatedByUserId == Guid.Empty)
            throw new ArgumentException("Updating user ID is required.", nameof(connection));
    }

    private static void ValidateId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("AI connection ID is required.", nameof(id));
    }
}
