namespace WebApiEngine.Shared;

/// <summary>Hostneutraler Katalogeintrag eines wiederverwendbaren Formularabschnitts.</summary>
public sealed class FormSectionMetadataDto
{
    public required Guid SectionId { get; init; }
    public required string Name { get; init; }
}

public sealed class CreateFormSectionRequestDto
{
    public required string Name { get; init; }
}

public sealed class RenameFormSectionRequestDto
{
    public required string Name { get; init; }
}

/// <summary>Datensparsame Versionsauswahl fuer Modellierungsoberflaechen.</summary>
public sealed class FormSectionVersionSummaryDto
{
    public required Guid Id { get; init; }
    public required Guid SectionId { get; init; }
    public required VersionDto Version { get; init; }
}

/// <summary>Konkrete, unveraenderliche Abschnittsfassung.</summary>
public sealed class FormSectionVersionDto
{
    public required Guid Id { get; init; }
    public required Guid SectionId { get; init; }
    public required VersionDto Version { get; init; }
    public required string SectionData { get; init; }
}

/// <summary>Gemeinsamer Abschnittsentwurf oder unveraenderliche Veroeffentlichungsbasis.</summary>
public sealed class FormSectionAuthoringDraftDto
{
    public required Guid SectionId { get; init; }
    public required long Revision { get; init; }
    public required bool HasDraft { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    public Guid? BasedOnPublishedSectionId { get; init; }
    public VersionDto? BasedOnVersion { get; init; }
    public required string SectionData { get; init; }
}

public sealed class SaveFormSectionAuthoringDraftRequestDto
{
    public required long ExpectedRevision { get; init; }
    public required string SectionData { get; init; }
}

public sealed class PublishFormSectionAuthoringDraftRequestDto
{
    public required long ExpectedRevision { get; init; }
}
