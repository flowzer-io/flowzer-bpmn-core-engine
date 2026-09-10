namespace StorageSystem;

/// <summary>
/// Atomarer Speichervertrag fuer gemeinsame Formularautoren-Entwuerfe. Revision 0 bedeutet,
/// dass noch kein Entwurf existiert. Die Produktionsimplementierung muss Publish und Loeschen
/// des Entwurfs in derselben Transaktion ausfuehren.
/// </summary>
public interface IFormAuthoringStorage
{
    Task<FormAuthoringDraft?> Get(Guid formId);
    Task<FormAuthoringWriteResult> TrySave(FormAuthoringDraft draft, long expectedRevision);
    Task<FormAuthoringDeleteResult> TryDelete(Guid formId, long expectedRevision);
    /// <summary>
    /// Veroeffentlicht den erwarteten Entwurf atomar. <paramref name="publishedFormData"/>
    /// erlaubt dem Anwendungsdienst, einen bereits serverseitig aufgeloesten und validierten
    /// Snapshot zu speichern, ohne den Autorenentwurf vorher umzuschreiben.
    /// </summary>
    Task<FormAuthoringPublishResult> TryPublish(
        Guid formId,
        long expectedRevision,
        Guid publishedFormId,
        string? publishedFormData = null);
}

public enum FormAuthoringWriteStatus { Written, RevisionConflict, FormNotFound }
public sealed record FormAuthoringWriteResult(
    FormAuthoringWriteStatus Status,
    FormAuthoringDraft? Draft,
    long CurrentRevision);

public enum FormAuthoringDeleteStatus { Deleted, RevisionConflict, FormNotFound }
public sealed record FormAuthoringDeleteResult(FormAuthoringDeleteStatus Status, long CurrentRevision);

public enum FormAuthoringPublishStatus { Published, RevisionConflict, FormNotFound }
public sealed record FormAuthoringPublishResult(
    FormAuthoringPublishStatus Status,
    Form? PublishedForm,
    long CurrentRevision);
