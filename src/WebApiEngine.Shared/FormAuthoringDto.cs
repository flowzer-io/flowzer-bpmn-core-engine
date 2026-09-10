namespace WebApiEngine.Shared;

/// <summary>Bearbeitbarer Stand und seine unveraenderliche veroeffentlichte Basis.</summary>
public sealed class FormAuthoringDraftDto
{
    public required Guid FormId { get; init; }
    public required long Revision { get; init; }
    public required bool HasDraft { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    public Guid? BasedOnPublishedFormId { get; init; }
    public VersionDto? BasedOnVersion { get; init; }
    public required string FormData { get; init; }
}

public sealed class SaveFormAuthoringDraftRequestDto
{
    public required long ExpectedRevision { get; init; }
    public required string FormData { get; init; }
}

public sealed class PublishFormAuthoringDraftRequestDto
{
    public required long ExpectedRevision { get; init; }
}

/// <summary>Lokaler Autorenstand, der ausschließlich serverseitig aufgeloest wird.</summary>
public sealed class PreviewFormAuthoringRequestDto
{
    public required string FormData { get; init; }
}

/// <summary>Vollständig expandierter, nicht persistierter Vorschau-Snapshot.</summary>
public sealed class FormAuthoringPreviewDto
{
    public required string FormData { get; init; }
    public required string ValidationProfile { get; init; }
}
