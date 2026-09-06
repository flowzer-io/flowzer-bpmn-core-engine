namespace WebApiEngine.BusinessLogic;

public class FormBusinessLogic(ITransactionalStorageProvider storageProvider)
{
    public async Task<Form> SaveForm(Form form)
    {
        // Versionsermittlung und Speichern in einer Transaktion, damit zwei parallele
        // Speichervorgaenge nicht dieselbe Versionsnummer vergeben.
        using var storageSystem = storageProvider.GetTransactionalStorage();
        form.Id = Guid.NewGuid();
        form.Version = (await storageSystem.FormStorage.GetMaxVersion(form.FormId)) + 1;
        await storageSystem.FormStorage.SaveForm(form);
        storageSystem.CommitChanges();
        return form;
    }

}
