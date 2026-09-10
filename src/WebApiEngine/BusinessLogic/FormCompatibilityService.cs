using WebApiEngine.Forms;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Erstellt einen datensparsamen Kompatibilitaetsbericht fuer Modellierer.
/// Jede Formularfassung wird isoliert geprueft: ein defektes Schema darf den Bericht
/// fuer andere Formularfassungen nicht abbrechen.
/// </summary>
public sealed class FormCompatibilityService(ITransactionalStorageProvider storageProvider)
{
    public async Task<IReadOnlyList<FormCompatibilityItemDto>> GetAsync(bool? needsMigration = null)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        var metadata = (await storage.FormStorage.GetFormMetadatas())
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.FormId)
            .ToArray();
        List<FormCompatibilityItemDto> report = [];

        foreach (var formMetadata in metadata)
        {
            var published = (await storage.FormStorage.GetForms(formMetadata.FormId))
                .OrderBy(form => form.Version)
                .ThenBy(form => form.Id)
                .Select(form => AssessPublished(formMetadata, form));
            report.AddRange(published);

            var draft = await storage.FormAuthoringStorage.Get(formMetadata.FormId);
            if (draft is not null)
                report.Add(await AssessDraftAsync(storage, formMetadata, draft));
        }

        return needsMigration switch
        {
            true => report.Where(item => !item.Compatible).ToArray(),
            false => report.Where(item => item.Compatible).ToArray(),
            null => report
        };
    }

    private static FormCompatibilityItemDto AssessPublished(FormMetadata metadata, Model.Form form) =>
        Assess(
            formData: form.FormData,
            formId: metadata.FormId,
            formName: metadata.Name,
            source: "published",
            publishedFormId: form.Id,
            version: form.Version.ToDto(),
            draftRevision: null);

    private static async Task<FormCompatibilityItemDto> AssessDraftAsync(
        IStorageSystem storage,
        FormMetadata metadata,
        Model.FormAuthoringDraft draft)
    {
        try
        {
            var expanded = await FormSectionBindingExpander.ExpandAsync(
                storage.FormSectionStorage,
                draft.FormData);
            return Assess(
                formData: expanded,
                formId: metadata.FormId,
                formName: metadata.Name,
                source: "draft",
                publishedFormId: draft.BasedOnPublishedFormId,
                version: draft.BasedOnVersion?.ToDto(),
                draftRevision: draft.Revision);
        }
        catch (FormContractException exception)
        {
            return Incompatible(
                metadata.FormId,
                metadata.Name,
                "draft",
                draft.BasedOnPublishedFormId,
                draft.BasedOnVersion?.ToDto(),
                draft.Revision,
                exception.Code);
        }
        catch (Exception)
        {
            return Incompatible(
                metadata.FormId,
                metadata.Name,
                "draft",
                draft.BasedOnPublishedFormId,
                draft.BasedOnVersion?.ToDto(),
                draft.Revision,
                "schema.invalid");
        }
    }

    private static FormCompatibilityItemDto Assess(
        string formData,
        Guid formId,
        string formName,
        string source,
        Guid? publishedFormId,
        VersionDto? version,
        long? draftRevision)
    {
        try
        {
            var contract = FormContractCompiler.Compile(formData);
            return new FormCompatibilityItemDto
            {
                FormId = formId,
                FormName = formName,
                Source = source,
                PublishedFormId = publishedFormId,
                Version = version,
                DraftRevision = draftRevision,
                Compatible = true,
                ValidationProfile = contract.ValidationProfile
            };
        }
        catch (FormContractException exception)
        {
            return Incompatible(formId, formName, source, publishedFormId, version, draftRevision, exception.Code);
        }
        catch (Exception)
        {
            // Ein unerwarteter Compilerfehler darf den Bericht nicht unbrauchbar machen.
            // Details bleiben absichtlich serverseitig und werden nicht als API-Text gespiegelt.
            return Incompatible(formId, formName, source, publishedFormId, version, draftRevision, "schema.invalid");
        }
    }

    private static FormCompatibilityItemDto Incompatible(
        Guid formId,
        string formName,
        string source,
        Guid? publishedFormId,
        VersionDto? version,
        long? draftRevision,
        string issueCode) => new()
    {
        FormId = formId,
        FormName = formName,
        Source = source,
        PublishedFormId = publishedFormId,
        Version = version,
        DraftRevision = draftRevision,
        Compatible = false,
        IssueCode = issueCode
    };
}
