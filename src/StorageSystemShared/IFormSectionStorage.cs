namespace StorageSystem;

/// <summary>
/// Speichervertrag fuer die Bibliothek wiederverwendbarer Formularabschnitte. Metadaten und
/// veroeffentlichte Fassungen sind getrennt: Fassungen sind append-only, waehrend genau ein
/// revisionsgesicherter Autorenentwurf je Abschnitt existieren kann.
/// </summary>
public interface IFormSectionStorage
{
    /// <summary>Legt einen neuen Abschnittskatalogeintrag an. Eine vorhandene ID ist ein Konflikt.</summary>
    Task CreateMetadata(FormSectionMetadata metadata);

    Task<FormSectionMetadata> GetMetadata(Guid sectionId);
    Task<IReadOnlyList<FormSectionMetadata>> ListMetadata();

    /// <summary>Benennen ist die einzige aenderbare Eigenschaft eines Katalogeintrags.</summary>
    Task<FormSectionMetadata> RenameMetadata(Guid sectionId, string name);

    Task<FormSectionVersion> GetVersion(Guid id);
    Task<FormSectionVersion> GetVersion(Guid sectionId, Model.Version version);
    Task<IReadOnlyList<FormSectionVersion>> ListVersions(Guid sectionId);

    Task<FormSectionAuthoringDraft?> GetDraft(Guid sectionId);

    /// <summary>
    /// Speichert genau dann, wenn <paramref name="expectedRevision"/> dem aktuellen Entwurf
    /// entspricht; 0 steht fuer einen noch nicht vorhandenen Entwurf.
    /// </summary>
    Task<FormSectionAuthoringWriteResult> TrySave(
        FormSectionAuthoringDraft draft,
        long expectedRevision);

    Task<FormSectionAuthoringDeleteResult> TryDelete(Guid sectionId, long expectedRevision);

    /// <summary>
    /// Veröffentlicht den aktuell erwarteten Entwurf als neue, unveränderliche Folgeversion
    /// und entfernt den Entwurf atomar.
    /// </summary>
    Task<FormSectionAuthoringPublishResult> TryPublish(
        Guid sectionId,
        long expectedRevision,
        Guid publishedSectionId);
}

public enum FormSectionAuthoringWriteStatus { Written, RevisionConflict, SectionNotFound }
public sealed record FormSectionAuthoringWriteResult(
    FormSectionAuthoringWriteStatus Status,
    FormSectionAuthoringDraft? Draft,
    long CurrentRevision);

public enum FormSectionAuthoringDeleteStatus { Deleted, RevisionConflict, SectionNotFound }
public sealed record FormSectionAuthoringDeleteResult(
    FormSectionAuthoringDeleteStatus Status,
    long CurrentRevision);

public enum FormSectionAuthoringPublishStatus { Published, RevisionConflict, SectionNotFound }
public sealed record FormSectionAuthoringPublishResult(
    FormSectionAuthoringPublishStatus Status,
    FormSectionVersion? PublishedSection,
    long CurrentRevision);
