using StorageSystem;

namespace FilesystemStorageSystem;

public class TransactionalStorage : Storage, ITransactionalStorage
{
    public void CommitChanges()
    {
        return;
    }

    public void RollbackTransaction()
    {
        return;
    }

    public void Dispose()
    {
        return;
    }
}