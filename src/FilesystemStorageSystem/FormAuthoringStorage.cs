using Model;
using Newtonsoft.Json;
using StorageSystem;

namespace FilesystemStorageSystem;

/// <summary>
/// Entwicklungsablage fuer gemeinsame Formularentwuerfe. CAS und Publish sind innerhalb
/// eines API-Prozesses atomar; fuer mehrere Prozesse ist weiterhin PostgreSQL erforderlich.
/// </summary>
internal sealed class FormAuthoringStorage(Storage storage) : IFormAuthoringStorage
{
    internal const string DirectoryName = "FormAuthoringDrafts";
    private readonly string _draftPath = storage.GetBasePath(Path.Combine("FileStorage", DirectoryName));
    private readonly string _formPath = storage.GetBasePath(Path.Combine("FileStorage", "Forms"));
    private readonly string _metaPath = storage.GetBasePath(Path.Combine("FileStorage", "Forms", "Meta"));

    public async Task<FormAuthoringDraft?> Get(Guid formId)
    {
        var content = await StorageFile.ReadAllTextIfExistsAsync(File(formId));
        return content is null
            ? null
            : JsonConvert.DeserializeObject<FormAuthoringDraft>(content, storage.NewtonSoftDefaultSettings)
              ?? throw new InvalidDataException("Stored form-authoring draft is empty.");
    }

    public async Task<FormAuthoringWriteResult> TrySave(FormAuthoringDraft draft, long expectedRevision)
    {
        Validate(draft, expectedRevision);
        var gate = FormStorage.SaveLocks.GetOrAdd(draft.FormId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (!MetadataExists(draft.FormId))
                return new FormAuthoringWriteResult(FormAuthoringWriteStatus.FormNotFound, null, 0);
            var current = await Get(draft.FormId);
            if ((current?.Revision ?? 0) != expectedRevision)
                return new FormAuthoringWriteResult(
                    FormAuthoringWriteStatus.RevisionConflict, current, current?.Revision ?? 0);
            await StorageFile.WriteAllTextAtomicAsync(
                File(draft.FormId), JsonConvert.SerializeObject(draft, storage.NewtonSoftDefaultSettings));
            return new FormAuthoringWriteResult(FormAuthoringWriteStatus.Written, draft, draft.Revision);
        }
        finally { gate.Release(); }
    }

    public async Task<FormAuthoringDeleteResult> TryDelete(Guid formId, long expectedRevision)
    {
        if (expectedRevision < 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        var gate = FormStorage.SaveLocks.GetOrAdd(formId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (!MetadataExists(formId))
                return new FormAuthoringDeleteResult(FormAuthoringDeleteStatus.FormNotFound, 0);
            var current = await Get(formId);
            var revision = current?.Revision ?? 0;
            if (revision != expectedRevision)
                return new FormAuthoringDeleteResult(FormAuthoringDeleteStatus.RevisionConflict, revision);
            StorageFile.DeleteIfExists(File(formId));
            return new FormAuthoringDeleteResult(FormAuthoringDeleteStatus.Deleted, 0);
        }
        finally { gate.Release(); }
    }

    public async Task<FormAuthoringPublishResult> TryPublish(
        Guid formId,
        long expectedRevision,
        Guid publishedFormId)
    {
        if (expectedRevision <= 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (publishedFormId == Guid.Empty) throw new ArgumentException("Published form ID is required.", nameof(publishedFormId));
        var gate = FormStorage.SaveLocks.GetOrAdd(formId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (!MetadataExists(formId))
                return new FormAuthoringPublishResult(FormAuthoringPublishStatus.FormNotFound, null, 0);
            var draft = await Get(formId);
            if (draft?.Revision != expectedRevision)
                return new FormAuthoringPublishResult(
                    FormAuthoringPublishStatus.RevisionConflict, null, draft?.Revision ?? 0);

            var existing = ReadForms(formId);
            var nextVersion = (existing.Count == 0
                ? new Model.Version()
                : existing.Max(form => form.Version) ?? new Model.Version()) + 1;
            var published = new Form
            {
                Id = publishedFormId,
                FormId = formId,
                Version = nextVersion,
                FormData = draft.FormData
            };
            var path = Path.Combine(_formPath, $"{formId}_{publishedFormId}.json");
            await StorageFile.WriteAllTextNewAtomicAsync(
                path, JsonConvert.SerializeObject(published, storage.NewtonSoftDefaultSettings));
            StorageFile.DeleteIfExists(File(formId));
            return new FormAuthoringPublishResult(FormAuthoringPublishStatus.Published, published, 0);
        }
        finally { gate.Release(); }
    }

    internal void DeleteForForm(Guid formId) => StorageFile.DeleteIfExists(File(formId));

    private List<Form> ReadForms(Guid formId) => StorageFile
        .ReadExistingFiles(_formPath, $"{formId}_*.json")
        .Select(entry => JsonConvert.DeserializeObject<Form>(entry.Content, storage.NewtonSoftDefaultSettings)!)
        .ToList();

    private bool MetadataExists(Guid formId) => System.IO.File.Exists(Path.Combine(_metaPath, $"{formId}.json"));
    private string File(Guid formId) => Path.Combine(_draftPath, $"draft_{formId:N}.json");

    private static void Validate(FormAuthoringDraft draft, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.FormId == Guid.Empty || expectedRevision < 0 || draft.Revision != checked(expectedRevision + 1))
            throw new ArgumentOutOfRangeException(nameof(expectedRevision));
    }
}
