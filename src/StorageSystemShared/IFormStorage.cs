using Version = System.Version;

namespace StorageSystem;

public interface IFormStorage
{
    Task SaveFormMetaData(FormMetadata formMetadata);
    Task<FormMetadata> GetFormMetaData(Guid formId);
    Task<IEnumerable<FormMetadata>> GetFormMetadatas();
    Task UpdateFormMetaData(FormMetadata formMetaData);
    Task DeleteFormMetaData(Guid formId);
    
    
    Task SaveForm(Form form);
    Task<Form> GetForm(Guid id);
    Task<IEnumerable<Form>> GetForms(Guid formId);
    Task DeleteForm(Guid id);
    Task<Model.Version> GetMaxVersion(Guid formId);

    /// <summary>Hierarchische Katalogordner. Alte Test-/Hostadapter sehen ohne Implementierung einen leeren Katalog.</summary>
    Task<IReadOnlyList<FormFolder>> GetFolders() => Task.FromResult<IReadOnlyList<FormFolder>>([]);
    Task<FormFolder?> GetFolder(Guid folderId) => Task.FromResult<FormFolder?>(null);
    Task SaveFolder(FormFolder folder) => throw new NotSupportedException("Form folders are not supported by this storage.");
    Task UpdateFolder(FormFolder folder) => throw new NotSupportedException("Form folders are not supported by this storage.");
    Task DeleteFolder(Guid folderId) => throw new NotSupportedException("Form folders are not supported by this storage.");
}
