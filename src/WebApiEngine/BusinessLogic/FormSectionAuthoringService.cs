using System.Text;
using System.Text.Json;
using Model;
using WebApiEngine.Auth;
using WebApiEngine.Forms;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Autorisierter Anwendungsfall fuer eine hostneutrale Bibliothek deklarativer
/// Formularabschnitte. IDs, Versionen, Akteur und Zeit stammen nie aus Browserdaten.
/// </summary>
public sealed class FormSectionAuthoringService(
    ITransactionalStorageProvider storageProvider,
    ICurrentUserContextAccessor currentUserAccessor,
    TimeProvider timeProvider)
{
    private const int MaximumSchemaBytes = 1_048_576;
    private const int MaximumNameLength = 200;
    private const string EmptySection = "{\"display\":\"form\",\"components\":[]}";

    public async Task<IReadOnlyList<FormSectionMetadataDto>> ListAsync()
    {
        using var storage = storageProvider.GetTransactionalStorage();
        return (await storage.FormSectionStorage.ListMetadata()).Select(ToDto).ToArray();
    }

    public async Task<FormSectionMetadataDto> GetMetadataAsync(Guid sectionId)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        await EnsureSectionExists(storage, sectionId);
        return ToDto(await storage.FormSectionStorage.GetMetadata(sectionId));
    }

    public async Task<FormSectionMetadataDto> CreateAsync(string name)
    {
        currentUserAccessor.GetCurrentUser().RequireResolvedUserId("creating a form section");
        var metadata = new FormSectionMetadata(Guid.NewGuid(), ValidateName(name));
        using var storage = storageProvider.GetTransactionalStorage();
        await storage.FormSectionStorage.CreateMetadata(metadata);
        storage.CommitChanges();
        return ToDto(metadata);
    }

    public async Task<FormSectionMetadataDto> RenameAsync(Guid sectionId, string name)
    {
        currentUserAccessor.GetCurrentUser().RequireResolvedUserId("renaming a form section");
        using var storage = storageProvider.GetTransactionalStorage();
        await EnsureSectionExists(storage, sectionId);
        var metadata = await storage.FormSectionStorage.RenameMetadata(
            sectionId, ValidateName(name));
        storage.CommitChanges();
        return ToDto(metadata);
    }

    public async Task<IReadOnlyList<FormSectionVersionSummaryDto>> ListVersionsAsync(Guid sectionId)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        await EnsureSectionExists(storage, sectionId);
        return (await storage.FormSectionStorage.ListVersions(sectionId)).Select(version => new FormSectionVersionSummaryDto
        {
            Id = version.Id,
            SectionId = version.SectionId,
            Version = version.Version.ToDto()
        }).ToArray();
    }

    public async Task<FormSectionVersionDto> GetVersionAsync(Guid sectionId, string versionText)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        await EnsureSectionExists(storage, sectionId);
        var version = ParseVersion(versionText);
        try { return ToDto(await storage.FormSectionStorage.GetVersion(sectionId, version)); }
        catch (FileNotFoundException) { throw UnknownSectionVersion(); }
        catch (InvalidOperationException) { throw UnknownSectionVersion(); }
    }

    public async Task<FormSectionAuthoringDraftDto> GetDraftAsync(Guid sectionId)
    {
        using var storage = storageProvider.GetTransactionalStorage();
        await EnsureSectionExists(storage, sectionId);
        var draft = await storage.FormSectionStorage.GetDraft(sectionId);
        if (draft is not null) return ToDto(draft);
        var latest = (await storage.FormSectionStorage.ListVersions(sectionId))
            .OrderByDescending(version => version.Version)
            .FirstOrDefault();
        return Baseline(sectionId, latest);
    }

    public async Task<FormSectionAuthoringDraftDto> SaveDraftAsync(
        Guid sectionId,
        SaveFormSectionAuthoringDraftRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ExpectedRevision < 0)
            throw new ArgumentException("ExpectedRevision must not be negative.", nameof(request));
        ValidateDraftJson(request.SectionData);
        var actor = currentUserAccessor.GetCurrentUser();
        actor.RequireResolvedUserId("saving a form-section draft");

        using var storage = storageProvider.GetTransactionalStorage();
        await EnsureSectionExists(storage, sectionId);
        var current = await storage.FormSectionStorage.GetDraft(sectionId);
        var latest = current is null
            ? (await storage.FormSectionStorage.ListVersions(sectionId))
                .OrderByDescending(version => version.Version).FirstOrDefault()
            : null;
        var draft = new FormSectionAuthoringDraft(
            sectionId,
            checked(request.ExpectedRevision + 1),
            actor.UserId,
            timeProvider.GetUtcNow(),
            current?.BasedOnPublishedSectionId ?? latest?.Id,
            current?.BasedOnVersion ?? latest?.Version,
            request.SectionData);
        var result = await storage.FormSectionStorage.TrySave(draft, request.ExpectedRevision);
        if (result.Status == FormSectionAuthoringWriteStatus.SectionNotFound) throw UnknownSection();
        if (result.Status == FormSectionAuthoringWriteStatus.RevisionConflict)
            throw new FormSectionAuthoringConflictException(request.ExpectedRevision, result.CurrentRevision);
        storage.CommitChanges();
        return ToDto(result.Draft!);
    }

    public async Task DeleteDraftAsync(Guid sectionId, long expectedRevision)
    {
        if (expectedRevision < 0)
            throw new ArgumentException("ExpectedRevision must not be negative.", nameof(expectedRevision));
        currentUserAccessor.GetCurrentUser().RequireResolvedUserId("discarding a form-section draft");
        using var storage = storageProvider.GetTransactionalStorage();
        var result = await storage.FormSectionStorage.TryDelete(
            RequireSectionId(sectionId), expectedRevision);
        if (result.Status == FormSectionAuthoringDeleteStatus.SectionNotFound) throw UnknownSection();
        if (result.Status == FormSectionAuthoringDeleteStatus.RevisionConflict)
            throw new FormSectionAuthoringConflictException(expectedRevision, result.CurrentRevision);
        storage.CommitChanges();
    }

    public async Task<FormSectionVersionDto> PublishAsync(Guid sectionId, long expectedRevision)
    {
        if (expectedRevision <= 0)
            throw new ArgumentException("A positive ExpectedRevision is required for publication.", nameof(expectedRevision));
        currentUserAccessor.GetCurrentUser().RequireResolvedUserId("publishing a form-section draft");
        using var storage = storageProvider.GetTransactionalStorage();
        await EnsureSectionExists(storage, sectionId);
        var draft = await storage.FormSectionStorage.GetDraft(sectionId);
        if (draft?.Revision != expectedRevision)
            throw new FormSectionAuthoringConflictException(expectedRevision, draft?.Revision ?? 0);

        try { _ = FormSectionCompiler.Compile(draft.SectionData); }
        catch (InvalidOperationException exception)
        {
            throw new FormPublicationValidationException(exception.Message, exception);
        }

        var result = await storage.FormSectionStorage.TryPublish(sectionId, expectedRevision, Guid.NewGuid());
        if (result.Status == FormSectionAuthoringPublishStatus.SectionNotFound) throw UnknownSection();
        if (result.Status == FormSectionAuthoringPublishStatus.RevisionConflict)
            throw new FormSectionAuthoringConflictException(expectedRevision, result.CurrentRevision);
        storage.CommitChanges();
        return ToDto(result.PublishedSection!);
    }

    private static void ValidateDraftJson(string sectionData)
    {
        if (string.IsNullOrWhiteSpace(sectionData))
            throw new ArgumentException("SectionData is required.", nameof(sectionData));
        if (Encoding.UTF8.GetByteCount(sectionData) > MaximumSchemaBytes)
            throw new ArgumentException("SectionData exceeds the one MiB authoring limit.", nameof(sectionData));
        try
        {
            using var document = JsonDocument.Parse(sectionData, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("SectionData must be a JSON object.", nameof(sectionData));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("SectionData must contain valid JSON.", nameof(sectionData), exception);
        }
    }

    private static Guid RequireSectionId(Guid sectionId) => sectionId != Guid.Empty
        ? sectionId
        : throw new ArgumentException("SectionId is required.", nameof(sectionId));

    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var trimmed = name.Trim();
        if (trimmed.Length > MaximumNameLength)
            throw new ArgumentException($"Name must not exceed {MaximumNameLength} characters.", nameof(name));
        return trimmed;
    }

    private static Model.Version ParseVersion(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var parts = value.Split('.', StringSplitOptions.None);
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var major)
            || !int.TryParse(parts[1], out var minor)
            || major < 0 || minor < 0
            || parts[0] != major.ToString(System.Globalization.CultureInfo.InvariantCulture)
            || parts[1] != minor.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new ArgumentException("Version must use the canonical major.minor format.", nameof(value));
        return new Model.Version(major, minor);
    }

    private static async Task EnsureSectionExists(IStorageSystem storage, Guid sectionId)
    {
        RequireSectionId(sectionId);
        try { _ = await storage.FormSectionStorage.GetMetadata(sectionId); }
        catch (FileNotFoundException) { throw UnknownSection(); }
        catch (InvalidOperationException) { throw UnknownSection(); }
    }

    private static FormSectionNotFoundException UnknownSection() => new();
    private static FormSectionVersionNotFoundException UnknownSectionVersion() => new();

    private static FormSectionMetadataDto ToDto(FormSectionMetadata metadata) => new()
    {
        SectionId = metadata.SectionId,
        Name = metadata.Name
    };

    private static FormSectionVersionDto ToDto(FormSectionVersion version) => new()
    {
        Id = version.Id,
        SectionId = version.SectionId,
        Version = version.Version.ToDto(),
        SectionData = version.SectionData
    };

    private static FormSectionAuthoringDraftDto Baseline(Guid sectionId, FormSectionVersion? latest) => new()
    {
        SectionId = sectionId,
        Revision = 0,
        HasDraft = false,
        BasedOnPublishedSectionId = latest?.Id,
        BasedOnVersion = latest?.Version.ToDto(),
        SectionData = latest?.SectionData ?? EmptySection
    };

    private static FormSectionAuthoringDraftDto ToDto(FormSectionAuthoringDraft draft) => new()
    {
        SectionId = draft.SectionId,
        Revision = draft.Revision,
        HasDraft = true,
        UpdatedAtUtc = draft.UpdatedAtUtc,
        BasedOnPublishedSectionId = draft.BasedOnPublishedSectionId,
        BasedOnVersion = draft.BasedOnVersion?.ToDto(),
        SectionData = draft.SectionData
    };
}

/// <summary>Konfliktantwort enthält nur Revisionen, niemals fremde Abschnittsdaten.</summary>
public sealed class FormSectionAuthoringConflictException(long expectedRevision, long currentRevision)
    : Exception("The form-section authoring draft has changed since it was loaded.")
{
    public long ExpectedRevision { get; } = expectedRevision;
    public long CurrentRevision { get; } = currentRevision;
}

public sealed class FormSectionNotFoundException()
    : KeyNotFoundException("The form section was not found.");

public sealed class FormSectionVersionNotFoundException()
    : KeyNotFoundException("The form section version was not found.");
