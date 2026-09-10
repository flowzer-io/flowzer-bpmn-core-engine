namespace WebApiEngine.Shared;

/// <summary>Datensparsame Kompatibilitaetszeile fuer Modellierer.</summary>
public sealed class FormCompatibilityItemDto
{
    public required Guid FormId { get; init; }
    public required string FormName { get; init; }
    public required string Source { get; init; }
    public Guid? PublishedFormId { get; init; }
    public VersionDto? Version { get; init; }
    public long? DraftRevision { get; init; }
    public required bool Compatible { get; init; }
    public string? ValidationProfile { get; init; }
    public string? IssueCode { get; init; }
}
