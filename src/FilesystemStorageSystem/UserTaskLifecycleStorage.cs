using System.Collections.Concurrent;
using Model;
using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;

/// <summary>
/// Entwicklungsablage für den Human-Task-Lifecycle. CAS gilt pro API-Prozess; für
/// Mehrprozessbetrieb ist ausschließlich die PostgreSQL-Implementierung vorgesehen.
/// </summary>
internal sealed class UserTaskLifecycleStorage(Storage storage) : IUserTaskLifecycleStorage
{
    internal const string DirectoryName = "UserTaskLifecycle";
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> Locks = new();
    private readonly string _path = storage.GetBasePath(Path.Combine("FileStorage", DirectoryName));

    public async Task<UserTaskWorkState?> Get(Guid userTaskId)
    {
        var content = await StorageFile.ReadAllTextIfExistsAsync(StateFile(userTaskId));
        return content is null
            ? null
            : JsonConvert.DeserializeObject<UserTaskWorkState>(content, storage.NewtonSoftDefaultSettings)
              ?? throw new InvalidDataException("Stored user-task work state is empty.");
    }

    public async Task<IReadOnlyDictionary<Guid, UserTaskWorkState>> GetMany(IEnumerable<Guid> userTaskIds)
    {
        var result = new Dictionary<Guid, UserTaskWorkState>();
        foreach (var id in userTaskIds.Distinct())
        {
            var state = await Get(id);
            if (state is not null) result[id] = state;
        }
        return result;
    }

    public Task<bool> LockTask(Guid userTaskId) => Task.FromResult(TaskExists(userTaskId));

    public async Task<UserTaskLifecycleWriteResult> TryWrite(
        UserTaskWorkState state,
        long expectedRevision,
        UserTaskAssignmentEvent auditEvent)
    {
        UserTaskLifecycleContract.Validate(state, expectedRevision, auditEvent);
        var gate = Locks.GetOrAdd(state.UserTaskId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (!TaskExists(state.UserTaskId))
                return new UserTaskLifecycleWriteResult(UserTaskLifecycleWriteStatus.TaskNotFound, null, 0);
            var current = await Get(state.UserTaskId);
            var currentRevision = current?.Revision ?? 0;
            if (currentRevision != expectedRevision)
                return new UserTaskLifecycleWriteResult(
                    UserTaskLifecycleWriteStatus.RevisionConflict, current, currentRevision);

            await StorageFile.WriteAllTextAtomicAsync(StateFile(state.UserTaskId),
                JsonConvert.SerializeObject(state, storage.NewtonSoftDefaultSettings));
            // Eine Datei je Ereignis verhindert ein Read-Modify-Write auf einer gemeinsamen Liste.
            await StorageFile.WriteAllTextAtomicAsync(EventFile(auditEvent),
                JsonConvert.SerializeObject(auditEvent, storage.NewtonSoftDefaultSettings));
            return new UserTaskLifecycleWriteResult(UserTaskLifecycleWriteStatus.Written, state, state.Revision);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<UserTaskAssignmentEvent>> GetEvents(Guid userTaskId)
    {
        var result = new List<UserTaskAssignmentEvent>();
        foreach (var file in Directory.GetFiles(_path, $"event_{userTaskId:N}_*.json"))
        {
            var content = await File.ReadAllTextAsync(file);
            result.Add(JsonConvert.DeserializeObject<UserTaskAssignmentEvent>(content, storage.NewtonSoftDefaultSettings)
                       ?? throw new InvalidDataException("Stored user-task audit event is empty."));
        }
        return result.OrderBy(item => item.Revision).ToArray();
    }

    public async Task<IReadOnlyList<UserTaskAssignmentEvent>> GetEventsByProcessInstance(Guid processInstanceId)
    {
        // Die Dateiablage ist nur der Entwicklungsadapter. Sie hat keinen sekundären Index und
        // liest daher die unveränderlichen Eventdateien einmal ein; PostgreSQL nutzt dafür eine
        // indexierte Instanzspalte.
        var result = new List<UserTaskAssignmentEvent>();
        foreach (var file in Directory.EnumerateFiles(_path, "event_*.json"))
        {
            var content = await File.ReadAllTextAsync(file);
            var item = JsonConvert.DeserializeObject<UserTaskAssignmentEvent>(content, storage.NewtonSoftDefaultSettings)
                       ?? throw new InvalidDataException("Stored user-task audit event is empty.");
            if (item.ProcessInstanceId == processInstanceId) result.Add(item);
        }

        return result
            .OrderBy(item => item.OccurredAtUtc)
            .ThenBy(item => item.UserTaskId)
            .ThenBy(item => item.Revision)
            .ThenBy(item => item.Id)
            .ToArray();
    }

    internal void DeleteState(Guid userTaskId) => StorageFile.DeleteIfExists(StateFile(userTaskId));

    private bool TaskExists(Guid userTaskId) =>
        storage.SubscriptionStorage is MessageSubscriptionStorage subscriptions
        && subscriptions.UserTaskExists(userTaskId);

    private string StateFile(Guid userTaskId) => Path.Combine(_path, $"state_{userTaskId:N}.json");
    private string EventFile(UserTaskAssignmentEvent item) =>
        Path.Combine(_path, $"event_{item.UserTaskId:N}_{item.Revision:D20}_{item.Id:N}.json");

}
