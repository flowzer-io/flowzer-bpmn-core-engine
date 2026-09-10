using System.Text;
using System.Text.Json;
using WebApiEngine.Auth;
using WebApiEngine.Forms;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Autorisierter Anwendungsfall fuer Entwurf und Veroeffentlichung. Formulardaten aus dem
/// Browser bestimmen weder Akteur noch Version oder ID einer veroeffentlichten Fassung.
/// </summary>
public sealed class FormAuthoringService(
    ITransactionalStorageProvider storageProvider,
    ICurrentUserContextAccessor currentUserAccessor,
    TimeProvider timeProvider)
{
    private const int MaximumSchemaBytes = 1_048_576;
    private const string EmptySchema = "{\"display\":\"form\",\"components\":[]}";

    public async Task<FormAuthoringDraftDto> GetAsync(Guid formId)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        await EnsureFormExists(storage, formId);
        var draft = await storage.FormAuthoringStorage.Get(formId);
        if (draft is not null) return ToDto(draft);

        var latest = (await storage.FormStorage.GetForms(formId))
            .OrderByDescending(form => form.Version)
            .FirstOrDefault();
        return Baseline(formId, latest);
    }

    public async Task<FormAuthoringDraftDto> SaveAsync(
        Guid formId,
        SaveFormAuthoringDraftRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedRevision < 0)
            throw new ArgumentException("ExpectedRevision must not be negative.", nameof(request));
        ValidateDraftJson(request.FormData);
        var actor = currentUserAccessor.GetCurrentUser();
        actor.RequireResolvedUserId("saving a form-authoring draft");

        using var storage = storageProvider.GetTransactionalStorage();
        await EnsureFormExists(storage, formId);
        var current = await storage.FormAuthoringStorage.Get(formId);
        var latest = current is null
            ? (await storage.FormStorage.GetForms(formId)).OrderByDescending(form => form.Version).FirstOrDefault()
            : null;
        var draft = new FormAuthoringDraft
        {
            FormId = formId,
            Revision = checked(request.ExpectedRevision + 1),
            UpdatedByUserId = actor.UserId,
            UpdatedAtUtc = timeProvider.GetUtcNow(),
            BasedOnPublishedFormId = current?.BasedOnPublishedFormId ?? latest?.Id,
            BasedOnVersion = current?.BasedOnVersion ?? latest?.Version,
            FormData = request.FormData
        };
        var result = await storage.FormAuthoringStorage.TrySave(draft, request.ExpectedRevision);
        if (result.Status == FormAuthoringWriteStatus.FormNotFound) throw UnknownForm(formId);
        if (result.Status == FormAuthoringWriteStatus.RevisionConflict)
            throw new FormAuthoringConflictException(request.ExpectedRevision, result.CurrentRevision);
        storage.CommitChanges();
        return ToDto(result.Draft!);
    }

    public async Task DeleteAsync(Guid formId, long expectedRevision)
    {
        if (expectedRevision < 0)
            throw new ArgumentException("ExpectedRevision must not be negative.", nameof(expectedRevision));
        currentUserAccessor.GetCurrentUser().RequireResolvedUserId("discarding a form-authoring draft");
        using var storage = storageProvider.GetTransactionalStorage();
        var result = await storage.FormAuthoringStorage.TryDelete(formId, expectedRevision);
        if (result.Status == FormAuthoringDeleteStatus.FormNotFound) throw UnknownForm(formId);
        if (result.Status == FormAuthoringDeleteStatus.RevisionConflict)
            throw new FormAuthoringConflictException(expectedRevision, result.CurrentRevision);
        storage.CommitChanges();
    }

    public async Task<Form> PublishAsync(Guid formId, long expectedRevision)
    {
        if (expectedRevision <= 0)
            throw new ArgumentException("A positive ExpectedRevision is required for publication.", nameof(expectedRevision));
        currentUserAccessor.GetCurrentUser().RequireResolvedUserId("publishing a form-authoring draft");
        using var storage = storageProvider.GetTransactionalStorage();
        await EnsureFormExists(storage, formId);
        var draft = await storage.FormAuthoringStorage.Get(formId);
        if (draft?.Revision != expectedRevision)
            throw new FormAuthoringConflictException(expectedRevision, draft?.Revision ?? 0);

        string publishedFormData;
        try
        {
            publishedFormData = await FormSectionBindingExpander.ExpandAsync(
                storage.FormSectionStorage,
                draft.FormData);
        }
        catch (InvalidOperationException exception)
        {
            throw new FormPublicationValidationException(exception.Message, exception);
        }

        var result = await storage.FormAuthoringStorage.TryPublish(
            formId,
            expectedRevision,
            Guid.NewGuid(),
            publishedFormData);
        if (result.Status == FormAuthoringPublishStatus.FormNotFound) throw UnknownForm(formId);
        if (result.Status == FormAuthoringPublishStatus.RevisionConflict)
            throw new FormAuthoringConflictException(expectedRevision, result.CurrentRevision);
        storage.CommitChanges();
        return result.PublishedForm!;
    }

    /// <summary>
    /// Erzeugt aus einem lokalen Autorenstand denselben Snapshot wie Publish, ohne einen
    /// Entwurf oder eine Version zu verändern. Insbesondere löst nicht der Browser die
    /// Abschnittsbibliothek auf.
    /// </summary>
    public async Task<FormAuthoringPreviewDto> PreviewAsync(Guid formId, string formData)
    {
        ValidateDraftJson(formData);
        using var storage = storageProvider.GetTransactionalStorage();
        await EnsureFormExists(storage, formId);
        try
        {
            var expanded = await FormSectionBindingExpander.ExpandAsync(
                storage.FormSectionStorage,
                formData);
            return new FormAuthoringPreviewDto
            {
                FormData = expanded,
                ValidationProfile = FormContractCompiler.Compile(expanded).ValidationProfile
            };
        }
        catch (InvalidOperationException exception)
        {
            throw new FormPublicationValidationException(exception.Message, exception);
        }
    }

    private static void ValidateDraftJson(string formData)
    {
        if (string.IsNullOrWhiteSpace(formData))
            throw new ArgumentException("FormData is required.", nameof(formData));
        if (Encoding.UTF8.GetByteCount(formData) > MaximumSchemaBytes)
            throw new ArgumentException("FormData exceeds the one MiB authoring limit.", nameof(formData));
        try
        {
            using var document = JsonDocument.Parse(formData, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("FormData must be a JSON object.", nameof(formData));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("FormData must contain valid JSON.", nameof(formData), exception);
        }
    }

    private static async Task EnsureFormExists(IStorageSystem storage, Guid formId)
    {
        if (formId == Guid.Empty) throw new ArgumentException("FormId is required.", nameof(formId));
        try { _ = await storage.FormStorage.GetFormMetaData(formId); }
        catch (FileNotFoundException) { throw UnknownForm(formId); }
        catch (InvalidOperationException) { throw UnknownForm(formId); }
    }

    private static FileNotFoundException UnknownForm(Guid formId) =>
        new($"Form metadata not found with id: {formId}");

    private static FormAuthoringDraftDto Baseline(Guid formId, Form? latest) => new()
    {
        FormId = formId,
        Revision = 0,
        HasDraft = false,
        BasedOnPublishedFormId = latest?.Id,
        BasedOnVersion = latest?.Version.ToDto(),
        FormData = latest?.FormData ?? EmptySchema
    };

    private static FormAuthoringDraftDto ToDto(FormAuthoringDraft draft) => new()
    {
        FormId = draft.FormId,
        Revision = draft.Revision,
        HasDraft = true,
        UpdatedAtUtc = draft.UpdatedAtUtc,
        BasedOnPublishedFormId = draft.BasedOnPublishedFormId,
        BasedOnVersion = draft.BasedOnVersion?.ToDto(),
        FormData = draft.FormData
    };
}

/// <summary>Konfliktantwort enthaelt nur Revisionen, niemals den fremden Entwurfsinhalt.</summary>
public sealed class FormAuthoringConflictException(long expectedRevision, long currentRevision)
    : Exception("The form-authoring draft has changed since it was loaded.")
{
    public long ExpectedRevision { get; } = expectedRevision;
    public long CurrentRevision { get; } = currentRevision;
}

public sealed class FormPublicationValidationException(string message, Exception innerException)
    : Exception(message, innerException);
