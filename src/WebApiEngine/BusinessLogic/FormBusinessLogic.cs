namespace WebApiEngine.BusinessLogic;

public class FormBusinessLogic(IStorageSystem storageSystem)
{
    public async Task<Form> SaveForm(Form form)
    {
        form.Id = Guid.NewGuid();
        form.Version = (await storageSystem.FormStorage.GetMaxVersion(form.FormId)) + 1;
        await storageSystem.FormStorage.SaveForm(form);
        return form;
    }
}