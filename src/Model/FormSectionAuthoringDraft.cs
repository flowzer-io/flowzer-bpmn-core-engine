namespace Model;

/// <summary>
/// Revisionierter, noch nicht veroeffentlichter Arbeitsstand eines Formularabschnitts.
/// </summary>
public sealed record FormSectionAuthoringDraft(
    Guid SectionId,
    long Revision,
    Guid UpdatedByUserId,
    DateTimeOffset UpdatedAtUtc,
    Guid? BasedOnPublishedSectionId,
    Version? BasedOnVersion,
    string SectionData);
