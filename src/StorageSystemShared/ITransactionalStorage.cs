namespace StorageSystem;

public interface ITransactionalStorage: IStorageSystem, IDisposable
{
    void CommitChanges();
    void RollbackTransaction();
}