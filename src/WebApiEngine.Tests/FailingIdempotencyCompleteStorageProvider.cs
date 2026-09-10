using StorageSystem;

namespace WebApiEngine.Tests;

/// <summary>
/// Testadapter, der ausschließlich das Festschreiben des Idempotenz-Ergebnisses
/// unterbricht. Alle fachlichen Schreibvorgänge erreichen weiterhin den echten Adapter.
/// </summary>
internal sealed class FailingIdempotencyCompleteStorageProvider(ITransactionalStorageProvider inner)
    : ITransactionalStorageProvider
{
    public ITransactionalStorage GetTransactionalStorage() =>
        new FailingIdempotencyCompleteStorage(inner.GetTransactionalStorage());

    private sealed class FailingIdempotencyCompleteStorage(ITransactionalStorage inner) : ITransactionalStorage
    {
        public IDefinitionStorage DefinitionStorage => inner.DefinitionStorage;
        public IFolderStorage FolderStorage => inner.FolderStorage;
        public IMessageSubscriptionStorage SubscriptionStorage => inner.SubscriptionStorage;
        public IInstanceStorage InstanceStorage => inner.InstanceStorage;
        public IFormStorage FormStorage => inner.FormStorage;
        public IServiceTaskStorage ServiceTaskStorage => inner.ServiceTaskStorage;
        public IIdempotencyStorage IdempotencyStorage { get; } =
            new FailingIdempotencyCompleteStorageAdapter(inner.IdempotencyStorage);

        public void CommitChanges() => inner.CommitChanges();
        public void RollbackTransaction() => inner.RollbackTransaction();
        public void Dispose() => inner.Dispose();
    }

    private sealed class FailingIdempotencyCompleteStorageAdapter(IIdempotencyStorage inner)
        : IIdempotencyStorage
    {
        public Task<IdempotencyRecord?> Get(string scopeHash) => inner.Get(scopeHash);
        public Task<bool> TryCreate(IdempotencyRecord record) => inner.TryCreate(record);
        public Task Complete(string scopeHash, Guid? processInstanceId) =>
            throw new InvalidOperationException("Injected idempotency completion failure.");
        public Task Remove(string scopeHash) => inner.Remove(scopeHash);
        public Task DeleteExpired(DateTime utcNow) => inner.DeleteExpired(utcNow);
    }
}
