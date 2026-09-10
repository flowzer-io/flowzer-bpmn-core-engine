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
        if (!string.IsNullOrWhiteSpace(key))
        {
            var resolved = await new FormKeyResolver(storage).ResolveAsync(key, definitionId);
            if (resolved.Form?.FormData is not { } formData)
                throw new FormSubmissionException(new Dictionary<string, string[]> { [""] = ["form.binding_missing"] });
            if (resolved.Form.ValidationProfile is not null and not FormContract.Profile)
                throw new FormSubmissionException(new Dictionary<string, string[]> { [""] = ["form.contract_unsupported"] });
            schema = formData;
        }
        FormContract contract;
        try { contract = FormContractCompiler.Compile(schema); }
        catch (InvalidOperationException)
        {
            throw new FormSubmissionException(new Dictionary<string, string[]> { [""] = ["form.contract_unsupported"] });
        }
        return FormSubmissionValidator.Validate(contract, data, context);
    }
}
