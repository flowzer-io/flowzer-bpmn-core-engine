using System.Dynamic;
using WebApiEngine.Forms;

namespace WebApiEngine.BusinessLogic;

public partial class BpmnBusinessLogic
{
    /// <summary>Innerhalb der Mutation/Transaktion aufrufen, nie vor Aufgabenrechten.</summary>
    private static async Task<ExpandoObject> ValidateFormInputAsync(IStorageSystem storage, string? key,
        Guid definitionId, ExpandoObject? data, ExpandoObject? context = null)
    {
        var schema = "{\"components\":[]}";
        string? boundProfile = null;
        if (!string.IsNullOrWhiteSpace(key))
        {
            var resolved = await new FormKeyResolver(storage).ResolveAsync(key, definitionId);
            if (resolved.Form?.FormData is not { } formData)
                throw new FormSubmissionException(new Dictionary<string, string[]> { [""] = ["form.binding_missing"] });
            if (!FormContract.IsSupportedProfile(resolved.Form.ValidationProfile))
                throw new FormSubmissionException(new Dictionary<string, string[]> { [""] = ["form.contract_unsupported"] });
            schema = formData;
            boundProfile = resolved.Form.ValidationProfile;
        }
        FormContract contract;
        try { contract = FormContractCompiler.Compile(schema); }
        catch (InvalidOperationException)
        {
            throw new FormSubmissionException(new Dictionary<string, string[]> { [""] = ["form.contract_unsupported"] });
        }
        if (boundProfile is not null && boundProfile != contract.ValidationProfile)
            throw new FormSubmissionException(new Dictionary<string, string[]> { [""] = ["form.contract_unsupported"] });
        if (contract.Fields.Any(field => field.SubjectSelection is not null))
        {
            try
            {
                var snapshot = await storage.IdentityDirectoryStorage.GetActiveSnapshot();
                return FormSubmissionValidator.Validate(contract, data, context, snapshot);
            }
            catch (FormSubmissionException)
            {
                throw;
            }
            catch (Exception)
            {
                // Die Submission darf Storage-/Konfigurationsdetails nicht nach außen tragen.
                throw new FormSubmissionException(new Dictionary<string, string[]> { [""] = ["directory.unavailable"] });
            }
        }
        return FormSubmissionValidator.Validate(contract, data, context);
    }
}
