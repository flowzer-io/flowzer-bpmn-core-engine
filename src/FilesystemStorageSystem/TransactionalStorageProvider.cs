using StorageSystem;

namespace FilesystemStorageSystem;

public class FileSystemTransactionalStorageProvider: ITransactionalStorageProvider
{
    public ITransactionalStorage GetTransactionalStorage()
    {
        return new TransactionalStorage();
    }
}