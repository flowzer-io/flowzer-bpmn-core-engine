using WebApiEngine.Forms;

namespace WebApiEngine.BusinessLogic;

public class FormBusinessLogic(ITransactionalStorageProvider storageProvider)
{
    public async Task<Form> SaveForm(Form form)
    {
        // Der kompatible Altendpunkt veroeffentlicht weiterhin direkt, darf aber nicht mehr
        // ungepruefte Schemata an der neuen Publish-Grenze vorbeischreiben.
        try { _ = FormContractCompiler.Compile(form.FormData); }
        catch (InvalidOperationException exception)
        {
            throw new FormPublicationValidationException(exception.Message, exception);
        }
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
