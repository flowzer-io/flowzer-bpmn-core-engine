namespace StorageSystem;

public interface ITransactionalStorageProvider
{
    ITransactionalStorage GetTransactionalStorage();
}