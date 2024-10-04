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
    Task UpdateForm(Form form);
    Task DeleteForm(Guid id);
}