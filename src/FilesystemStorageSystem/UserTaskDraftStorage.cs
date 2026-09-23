using System.Collections.Concurrent;
using Model;
using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;

/// <summary>
/// Entwicklungsablage fuer Aufgabenentwuerfe. Der schluesselbezogene Prozess-Lock macht
/// Revisionen innerhalb eines API-Prozesses atomar; Mehrprozessbetrieb bleibt PostgreSQL
/// vorbehalten und wird fuer die Dateiablage nicht behauptet.
/// </summary>
internal sealed class UserTaskDraftStorage(Storage storage) : IUserTaskDraftStorage
{
    internal const string DirectoryName = "UserTaskDrafts";

    /// <summary>
    /// Je Entwurf eine Sperre. Sichtbar bis zur Assembly-Grenze, damit ein Test belegen kann,
    /// dass Lesen und Schreiben eines Entwurfs wirklich innerhalb derselben Sperre liegen —
    /// von aussen betrachtet unterscheidet sich das sonst nicht vom fehlerhaften Fall.
    /// </summary>
    internal static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);
    private readonly string _path = storage.GetBasePath(Path.Combine("FileStorage", DirectoryName));

    public async Task<UserTaskDraft?> Get(Guid userTaskId, string ownerKey)
    {
        ValidateOwnerKey(ownerKey);
        var content = await StorageFile.ReadAllTextIfExistsAsync(File(userTaskId, ownerKey));
        return content is null
            ? null
            : JsonConvert.DeserializeObject<UserTaskDraft>(content, storage.NewtonSoftDefaultSettings)
              ?? throw new InvalidDataException("Stored user-task draft is empty.");
    }

    public async Task<UserTaskDraftWriteResult> TrySave(UserTaskDraft draft, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ValidateOwnerKey(draft.OwnerKey);
        if (expectedRevision < 0 || draft.Revision != checked(expectedRevision + 1))
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));

        var key = Key(draft.UserTaskId, draft.OwnerKey);
        var gate = Locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var current = await Get(draft.UserTaskId, draft.OwnerKey);
            var currentRevision = current?.Revision ?? 0;
            if (currentRevision != expectedRevision)
                return new UserTaskDraftWriteResult(
                    UserTaskDraftWriteStatus.RevisionConflict, current, currentRevision);

            var json = JsonConvert.SerializeObject(draft, storage.NewtonSoftDefaultSettings);
            await StorageFile.WriteAllTextAtomicAsync(File(draft.UserTaskId, draft.OwnerKey), json);
            return new UserTaskDraftWriteResult(UserTaskDraftWriteStatus.Written, draft, draft.Revision);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<UserTaskDraftDeleteResult> TryDelete(
        Guid userTaskId,
        string ownerKey,
        long expectedRevision)
    {
        ValidateOwnerKey(ownerKey);
        if (expectedRevision < 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        var key = Key(userTaskId, ownerKey);
        var gate = Locks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var current = await Get(userTaskId, ownerKey);
            var currentRevision = current?.Revision ?? 0;
            if (currentRevision != expectedRevision)
                return new UserTaskDraftDeleteResult(
                    UserTaskDraftDeleteStatus.RevisionConflict, currentRevision);
            StorageFile.DeleteIfExists(File(userTaskId, ownerKey));
            return new UserTaskDraftDeleteResult(UserTaskDraftDeleteStatus.Deleted, 0);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<int> CountForTask(Guid userTaskId) => Task.FromResult(FilesOf(userTaskId).Length);

    public async Task<int> DeleteAllForTask(Guid userTaskId)
    {
        var deleted = 0;
        foreach (var file in FilesOf(userTaskId))
        {
            if (OwnerKeyOf(file) is not { } ownerKey) continue;
            var gate = Locks.GetOrAdd(Key(userTaskId, ownerKey), static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                StorageFile.DeleteIfExists(file);
                deleted++;
            }
            finally
            {
                gate.Release();
            }
        }

        return deleted;
    }

    public async Task<int> RebindAllForTask(Guid userTaskId, Guid definitionId)
    {
        var rebound = 0;
        foreach (var file in FilesOf(userTaskId))
        {
            // Der Eigentuemerschluessel steckt im Dateinamen. Er wird vor dem Lesen gebraucht:
            // Nur unter der Sperre dieses Entwurfs steht fest, dass zwischen Lesen und Schreiben
            // kein Speichern dazwischenkommt und der alte Stand zurueckkehrt.
            if (OwnerKeyOf(file) is not { } ownerKey) continue;
            var gate = Locks.GetOrAdd(Key(userTaskId, ownerKey), static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                var content = await StorageFile.ReadAllTextIfExistsAsync(file);
                if (content is null) continue;
                var draft = JsonConvert.DeserializeObject<UserTaskDraft>(content, storage.NewtonSoftDefaultSettings)
                            ?? throw new InvalidDataException("Stored user-task draft is empty.");
                await StorageFile.WriteAllTextAtomicAsync(
                    file,
                    JsonConvert.SerializeObject(
                        UserTaskDraftRebinding.To(draft, definitionId), storage.NewtonSoftDefaultSettings));
                rebound++;
            }
            finally
            {
                gate.Release();
            }
        }

        return rebound;
    }

    /// <summary>Bereinigt alle privaten Entwuerfe, sobald die zugehoerige Aufgabe verschwindet.</summary>
    internal void DeleteAllFiles(Guid userTaskId) => DeleteFiles(userTaskId);

    private int DeleteFiles(Guid userTaskId)
    {
        var files = FilesOf(userTaskId);
        foreach (var file in files)
            StorageFile.DeleteIfExists(file);
        return files.Length;
    }

    private string[] FilesOf(Guid userTaskId) => Directory.GetFiles(_path, $"draft_{userTaskId:N}_*.json");

    /// <summary>Der Eigentuemerschluessel aus dem Dateinamen, ohne die Datei zu lesen.</summary>
    private static string? OwnerKeyOf(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var separator = name.LastIndexOf('_');
        return separator < 0 ? null : name[(separator + 1)..];
    }

    private string File(Guid userTaskId, string ownerKey) =>
        Path.Combine(_path, $"draft_{userTaskId:N}_{ownerKey}.json");

    internal static string Key(Guid userTaskId, string ownerKey) => $"{userTaskId:N}:{ownerKey}";

    private static void ValidateOwnerKey(string ownerKey)
    {
        if (ownerKey.Length != 64 || ownerKey.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A user-task draft owner key must be a SHA-256 hex value.", nameof(ownerKey));
    }
}
