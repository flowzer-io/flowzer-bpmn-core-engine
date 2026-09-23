using WebApiEngine.Forms;

namespace WebApiEngine.BusinessLogic;

public class FormBusinessLogic(ITransactionalStorageProvider storageProvider)
{
    public async Task<Form> SaveForm(Form form)
    {
        // Versionsermittlung und Speichern in einer Transaktion, damit zwei parallele
        // Speichervorgaenge nicht dieselbe Versionsnummer vergeben.
        using var storageSystem = storageProvider.GetTransactionalStorage();
        // Der kompatible Altendpunkt veroeffentlicht weiterhin direkt. Er muss aber dieselbe
        // serverseitige Komponentenauflösung und Vertragsprüfung wie der Autorenpfad nutzen.
        try
        {
            form.FormData = await FormSectionBindingExpander.ExpandAsync(
                storageSystem.FormStorage,
                storageSystem.FormSectionStorage,
                form.FormId,
                form.FormData);
        }
        catch (InvalidOperationException exception)
        {
            throw new FormPublicationValidationException(exception.Message, exception);
        }
        form.Id = Guid.NewGuid();
        form.Version = (await storageSystem.FormStorage.GetMaxVersion(form.FormId)) + 1;
        await storageSystem.FormStorage.SaveForm(form);
        storageSystem.CommitChanges();
        return form;
    }

}
