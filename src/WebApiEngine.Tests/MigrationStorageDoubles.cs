using Model;
using StorageSystem;
using Version = Model.Version;

namespace WebApiEngine.Tests;

/// <summary>
/// Naht fuer die Migrationstests: Sie laesst einen Testfall genau dann eingreifen, wenn die
/// Migration mitten in ihrer eigenen Transaktion steht. Anders liesse sich weder ein Deployment
/// aus einem zweiten API-Prozess noch ein paralleler Entwurfsschreiber deterministisch
/// dazwischenschieben.
/// </summary>
internal sealed class MigrationStorageHooks
{
    /// <summary>Ab dem wievielten Lesen der deployten Version eingegriffen wird (0 = nie).</summary>
    internal int OnDeployedDefinitionRead { get; init; }

    /// <summary>Laeuft vor dem betroffenen Lesen und darf die Ablage veraendern.</summary>
    internal Func<Task>? BeforeDeployedDefinitionRead { get; init; }

    /// <summary>
    /// Laeuft, sobald die Migration die Sperre einer Aufgabe genommen hat — also bevor sie
    /// Subscription oder Entwuerfe anfasst. Nimmt sie die Sperre nicht, laeuft der Griff nie.
    /// </summary>
    internal Func<Task>? AfterTaskLock { get; init; }

    internal int DeployedDefinitionReads;
}

internal sealed class HookedTransactionalStorageProvider(
    ITransactionalStorageProvider inner,
    MigrationStorageHooks hooks) : ITransactionalStorageProvider
{
    public ITransactionalStorage GetTransactionalStorage() =>
        new HookedTransactionalStorage(inner.GetTransactionalStorage(), hooks);
}

internal sealed class HookedTransactionalStorage(ITransactionalStorage inner, MigrationStorageHooks hooks)
    : ITransactionalStorage
{
    public IDefinitionStorage DefinitionStorage { get; } = new HookedDefinitionStorage(inner.DefinitionStorage, hooks);
    public IUserTaskLifecycleStorage UserTaskLifecycleStorage { get; } =
        new HookedUserTaskLifecycleStorage(inner.UserTaskLifecycleStorage, hooks);
    public IUserTaskDraftStorage UserTaskDraftStorage => inner.UserTaskDraftStorage;
    public IFolderStorage FolderStorage => inner.FolderStorage;
    public IMessageSubscriptionStorage SubscriptionStorage => inner.SubscriptionStorage;
    public IInstanceStorage InstanceStorage => inner.InstanceStorage;
    public IFormStorage FormStorage => inner.FormStorage;
    public IFormAuthoringStorage FormAuthoringStorage => inner.FormAuthoringStorage;
    public IFormSectionStorage FormSectionStorage => inner.FormSectionStorage;
    public IServiceTaskStorage ServiceTaskStorage => inner.ServiceTaskStorage;
    public IIdempotencyStorage IdempotencyStorage => inner.IdempotencyStorage;
    public IIdentityDirectoryStorage IdentityDirectoryStorage => inner.IdentityDirectoryStorage;
    public IUserTaskDeadlineStorage UserTaskDeadlineStorage => inner.UserTaskDeadlineStorage;
    public IUserTaskNotificationStorage UserTaskNotificationStorage => inner.UserTaskNotificationStorage;
    public IRuntimeNodeEventStorage RuntimeNodeEventStorage => inner.RuntimeNodeEventStorage;
    public IAiConnectionStorage AiConnectionStorage => inner.AiConnectionStorage;
    public IAiRunStorage AiRunStorage => inner.AiRunStorage;
    public void CommitChanges() => inner.CommitChanges();
    public void RollbackTransaction() => inner.RollbackTransaction();
    public void Dispose() => inner.Dispose();
}

internal sealed class HookedUserTaskLifecycleStorage(IUserTaskLifecycleStorage inner, MigrationStorageHooks hooks)
    : IUserTaskLifecycleStorage
{
    public async Task<bool> LockTask(Guid userTaskId)
    {
        var locked = await inner.LockTask(userTaskId);
        if (locked && hooks.AfterTaskLock is { } hook) await hook();
        return locked;
    }

    public Task<UserTaskWorkState?> Get(Guid userTaskId) => inner.Get(userTaskId);
    public Task<IReadOnlyDictionary<Guid, UserTaskWorkState>> GetMany(IEnumerable<Guid> userTaskIds) =>
        inner.GetMany(userTaskIds);
    public Task<UserTaskLifecycleWriteResult> TryWrite(
        UserTaskWorkState state, long expectedRevision, UserTaskAssignmentEvent auditEvent) =>
        inner.TryWrite(state, expectedRevision, auditEvent);
    public Task<IReadOnlyList<UserTaskAssignmentEvent>> GetEvents(Guid userTaskId) => inner.GetEvents(userTaskId);
    public Task<IReadOnlyList<UserTaskAssignmentEvent>> GetEventsByProcessInstance(Guid processInstanceId) =>
        inner.GetEventsByProcessInstance(processInstanceId);
}

internal sealed class HookedDefinitionStorage(IDefinitionStorage inner, MigrationStorageHooks hooks)
    : IDefinitionStorage
{
    public async Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId)
    {
        var read = Interlocked.Increment(ref hooks.DeployedDefinitionReads);
        if (hooks.BeforeDeployedDefinitionRead is { } hook && read == hooks.OnDeployedDefinitionRead)
            await hook();
        return await inner.GetDeployedDefinition(definitionDefinitionId);
    }

    public Task StoreBinary(Guid guid, string data) => inner.StoreBinary(guid, data);
    public Task<string> GetBinary(Guid guid) => inner.GetBinary(guid);
    public Task DeleteBinary(Guid guid) => inner.DeleteBinary(guid);
    public Task<Guid[]> GetAllBinaryDefinitions() => inner.GetAllBinaryDefinitions();
    public Task<BpmnDefinition[]> GetAllDefinitions() => inner.GetAllDefinitions();
    public Task StoreDefinition(BpmnDefinition definition) => inner.StoreDefinition(definition);
    public Task DeleteDefinition(Guid id) => inner.DeleteDefinition(id);
    public Task<Version?> GetMaxVersionId(string modelId) => inner.GetMaxVersionId(modelId);
    public Task<BpmnDefinition> GetDefinitionById(Guid id) => inner.GetDefinitionById(id);
    public Task<BpmnDefinition> GetLatestDefinition(string definitionId) => inner.GetLatestDefinition(definitionId);
    public Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions() => inner.GetAllMetaDefinitions();
    public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition) => inner.StoreMetaDefinition(metaDefinition);
    public Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition) => inner.UpdateMetaDefinition(metaDefinition);
    public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id) => inner.GetMetaDefinitionById(id);
    public Task DeleteMetaDefinition(string definitionId) => inner.DeleteMetaDefinition(definitionId);
    public Task LockForDefinitionChange(string definitionId) => inner.LockForDefinitionChange(definitionId);
}

/// <summary>
/// Eine Ablage, die Entwuerfe fuehren koennte, den Entwurfsvertrag der Migration aber nicht
/// kennt — der Fall eines externen Adapters, der nur die aelteren Methoden implementiert.
/// </summary>
internal sealed class LegacyUserTaskDraftStorage(IUserTaskDraftStorage inner) : IUserTaskDraftStorage
{
    public Task<UserTaskDraft?> Get(Guid userTaskId, string ownerKey) => inner.Get(userTaskId, ownerKey);
    public Task<UserTaskDraftWriteResult> TrySave(UserTaskDraft draft, long expectedRevision) =>
        inner.TrySave(draft, expectedRevision);
    public Task<UserTaskDraftDeleteResult> TryDelete(Guid userTaskId, string ownerKey, long expectedRevision) =>
        inner.TryDelete(userTaskId, ownerKey, expectedRevision);
}

internal sealed class LegacyDraftStorageProvider(ITransactionalStorageProvider inner) : ITransactionalStorageProvider
{
    public ITransactionalStorage GetTransactionalStorage() =>
        new LegacyDraftStorage(inner.GetTransactionalStorage());
}

internal sealed class NoDraftStorageProvider(ITransactionalStorageProvider inner) : ITransactionalStorageProvider
{
    public ITransactionalStorage GetTransactionalStorage() =>
        new NoDraftStorage(inner.GetTransactionalStorage());
}

/// <summary>
/// Eine Ablage, die den Entwurfsvertrag gar nicht erst anbietet: Sie bekommt den
/// Kompatibilitaetsadapter des Vertrags und fuehrt damit ausdruecklich keine Entwuerfe.
/// </summary>
internal sealed class NoDraftStorage(ITransactionalStorage inner) : ITransactionalStorage
{
    public IDefinitionStorage DefinitionStorage => inner.DefinitionStorage;
    public IFolderStorage FolderStorage => inner.FolderStorage;
    public IMessageSubscriptionStorage SubscriptionStorage => inner.SubscriptionStorage;
    public IInstanceStorage InstanceStorage => inner.InstanceStorage;
    public IFormStorage FormStorage => inner.FormStorage;
    public IFormAuthoringStorage FormAuthoringStorage => inner.FormAuthoringStorage;
    public IFormSectionStorage FormSectionStorage => inner.FormSectionStorage;
    public IServiceTaskStorage ServiceTaskStorage => inner.ServiceTaskStorage;
    public IIdempotencyStorage IdempotencyStorage => inner.IdempotencyStorage;
    public IIdentityDirectoryStorage IdentityDirectoryStorage => inner.IdentityDirectoryStorage;
    public IUserTaskLifecycleStorage UserTaskLifecycleStorage => inner.UserTaskLifecycleStorage;
    public IUserTaskDeadlineStorage UserTaskDeadlineStorage => inner.UserTaskDeadlineStorage;
    public IUserTaskNotificationStorage UserTaskNotificationStorage => inner.UserTaskNotificationStorage;
    public IRuntimeNodeEventStorage RuntimeNodeEventStorage => inner.RuntimeNodeEventStorage;
    public IAiConnectionStorage AiConnectionStorage => inner.AiConnectionStorage;
    public IAiRunStorage AiRunStorage => inner.AiRunStorage;
    public void CommitChanges() => inner.CommitChanges();
    public void RollbackTransaction() => inner.RollbackTransaction();
    public void Dispose() => inner.Dispose();
}

internal sealed class LegacyDraftStorage(ITransactionalStorage inner) : ITransactionalStorage
{
    public IUserTaskDraftStorage UserTaskDraftStorage { get; } = new LegacyUserTaskDraftStorage(inner.UserTaskDraftStorage);
    public IDefinitionStorage DefinitionStorage => inner.DefinitionStorage;
    public IFolderStorage FolderStorage => inner.FolderStorage;
    public IMessageSubscriptionStorage SubscriptionStorage => inner.SubscriptionStorage;
    public IInstanceStorage InstanceStorage => inner.InstanceStorage;
    public IFormStorage FormStorage => inner.FormStorage;
    public IFormAuthoringStorage FormAuthoringStorage => inner.FormAuthoringStorage;
    public IFormSectionStorage FormSectionStorage => inner.FormSectionStorage;
    public IServiceTaskStorage ServiceTaskStorage => inner.ServiceTaskStorage;
    public IIdempotencyStorage IdempotencyStorage => inner.IdempotencyStorage;
    public IIdentityDirectoryStorage IdentityDirectoryStorage => inner.IdentityDirectoryStorage;
    public IUserTaskLifecycleStorage UserTaskLifecycleStorage => inner.UserTaskLifecycleStorage;
    public IUserTaskDeadlineStorage UserTaskDeadlineStorage => inner.UserTaskDeadlineStorage;
    public IUserTaskNotificationStorage UserTaskNotificationStorage => inner.UserTaskNotificationStorage;
    public IRuntimeNodeEventStorage RuntimeNodeEventStorage => inner.RuntimeNodeEventStorage;
    public IAiConnectionStorage AiConnectionStorage => inner.AiConnectionStorage;
    public IAiRunStorage AiRunStorage => inner.AiRunStorage;
    public void CommitChanges() => inner.CommitChanges();
    public void RollbackTransaction() => inner.RollbackTransaction();
    public void Dispose() => inner.Dispose();
}
