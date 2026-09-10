namespace Model;

/// <summary>
/// Revisionierter Arbeitsstand eines Katalogformulars. Veröffentlichte <see cref="Form"/>
/// bleiben davon getrennt und werden niemals nachträglich verändert.
/// </summary>
public sealed class FormAuthoringDraft
{
    public required Guid FormId { get; init; }
    public required long Revision { get; init; }
    public required Guid UpdatedByUserId { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
    public Guid? BasedOnPublishedFormId { get; init; }
    public Version? BasedOnVersion { get; init; }
    public required string FormData { get; init; }
}
