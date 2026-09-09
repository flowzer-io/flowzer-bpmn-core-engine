using System.Text.Json.Nodes;
using FilesystemStorageSystem;
using FluentAssertions;
using Model;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Ende-zu-Ende-Vertrag zwischen Abschnittsbibliothek und Formularpublikation.</summary>
[NonParallelizable]
public sealed class FormSectionPublicationTest : IDisposable
{
    private readonly string? _previousRoot;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"flowzer-section-publish-{Guid.NewGuid():N}");
    private readonly Storage _storage;
    private readonly Guid _actorId = Guid.NewGuid();

    public FormSectionPublicationTest()
    {
        _previousRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
        Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _root);
        _storage = new Storage();
    }

    // Testzweck: Formularpublikation speichert nur den vollstaendig expandierten
    // Serversnapshot; der Autorenentwurf mit Referenz ist danach atomar entfernt.
    [Test]
    public async Task Publish_ShouldPersistSelfContainedSectionSnapshot()
    {
        var formId = Guid.NewGuid();
        var sectionId = await PublishSection("""
            {"flowzer":{"contractVersion":3},"components":[{"type":"textfield","key":"requester"}]}
            """);
        await _storage.FormStorage.SaveFormMetaData(new FormMetadata { FormId = formId, Name = "Leave" });
        var service = Service();
        var authoringData = $$"""
            {"flowzer":{"contractVersion":3},"components":[
              {"type":"flowzerSection","key":"person","sectionId":"{{sectionId}}","version":"0.1"},
              {"type":"number","key":"days"}
            ]}
            """;
        await service.SaveAsync(formId, new SaveFormAuthoringDraftRequestDto
        {
            ExpectedRevision = 0,
            FormData = authoringData
        });

        var published = await service.PublishAsync(formId, 1);

        published.FormData.Should().NotContain("flowzerSection");
        var snapshot = JsonNode.Parse(published.FormData)!.AsObject();
        snapshot["components"]!.AsArray().Select(component => component!["key"]!.GetValue<string>())
            .Should().Equal("requester", "days");
        snapshot["flowzer"]!["boundSections"]!.AsArray().Should().ContainSingle();
        (await _storage.FormAuthoringStorage.Get(formId)).Should().BeNull();
        (await _storage.FormStorage.GetForms(formId)).Single().FormData.Should().Be(published.FormData);
    }

    // Testzweck: Scheitert die Abschnittsauflösung, bleiben Entwurf und Revision
    // vollständig erhalten und es entsteht keine halbe Formularversion.
    [Test]
    public async Task Publish_ShouldKeepDraftWhenSectionVersionIsMissing()
    {
        var formId = Guid.NewGuid();
        var sectionId = Guid.NewGuid();
        await _storage.FormStorage.SaveFormMetaData(new FormMetadata { FormId = formId, Name = "Leave" });
        var service = Service();
        await service.SaveAsync(formId, new SaveFormAuthoringDraftRequestDto
        {
            ExpectedRevision = 0,
            FormData = $$"""
                {"components":[{"type":"flowzerSection","key":"person","sectionId":"{{sectionId}}","version":"0.1"}]}
                """
        });

        Func<Task> publish = async () => _ = await service.PublishAsync(formId, 1);

        await publish.Should().ThrowAsync<FormPublicationValidationException>();
        (await _storage.FormAuthoringStorage.Get(formId))!.Revision.Should().Be(1);
        (await _storage.FormStorage.GetForms(formId)).Should().BeEmpty();
    }

    // Testzweck: Der Kompatibilitaetsbericht bewertet einen referenzierenden Entwurf
    // gegen den tatsaechlich expandierten Serversnapshot und meldet fehlende Fassungen stabil.
    [Test]
    public async Task Compatibility_ShouldResolveConcreteSectionDraftsAndReportMissingVersions()
    {
        var validFormId = Guid.NewGuid();
        var missingFormId = Guid.NewGuid();
        var sectionId = await PublishSection(
            """{"components":[{"type":"textfield","key":"requester"}]}""");
        await _storage.FormStorage.SaveFormMetaData(new FormMetadata { FormId = validFormId, Name = "Valid" });
        await _storage.FormStorage.SaveFormMetaData(new FormMetadata { FormId = missingFormId, Name = "Missing" });
        var service = Service();
        await service.SaveAsync(validFormId, new SaveFormAuthoringDraftRequestDto
        {
            ExpectedRevision = 0,
            FormData = $$"""{"components":[{"type":"flowzerSection","key":"person","sectionId":"{{sectionId}}","version":"0.1"}]}"""
        });
        await service.SaveAsync(missingFormId, new SaveFormAuthoringDraftRequestDto
        {
            ExpectedRevision = 0,
            FormData = $$"""{"components":[{"type":"flowzerSection","key":"person","sectionId":"{{sectionId}}","version":"0.2"}]}"""
        });

        var report = await new FormCompatibilityService(new FileSystemTransactionalStorageProvider()).GetAsync();

        report.Should().ContainSingle(item =>
            item.FormId == validFormId && item.Source == "draft" && item.Compatible);
        report.Should().ContainSingle(item =>
            item.FormId == missingFormId && item.Source == "draft" && !item.Compatible
            && item.IssueCode == "section.version_not_found");
    }

    // Testzweck: Die Vorschau verwendet exakt dieselbe serverseitige Abschnittsauflösung
    // wie Publish, erzeugt dabei aber weder Entwurf noch neue Formularversion.
    [Test]
    public async Task Preview_ShouldResolveSectionWithoutPersistingAuthoringState()
    {
        var formId = Guid.NewGuid();
        var sectionId = await PublishSection(
            """{"components":[{"type":"textfield","key":"requester"}]}""");
        await _storage.FormStorage.SaveFormMetaData(new FormMetadata { FormId = formId, Name = "Preview" });

        var preview = await Service().PreviewAsync(formId, $$"""
            {"components":[{"type":"flowzerSection","key":"person","sectionId":"{{sectionId}}","version":"0.1"}]}
            """);

        preview.FormData.Should().Contain("requester").And.NotContain("flowzerSection");
        preview.ValidationProfile.Should().Be("flowzer.forms/1");
        (await _storage.FormAuthoringStorage.Get(formId)).Should().BeNull();
        (await _storage.FormStorage.GetForms(formId)).Should().BeEmpty();
    }

    private async Task<Guid> PublishSection(string data)
    {
        var sectionId = Guid.NewGuid();
        await _storage.FormSectionStorage.CreateMetadata(new FormSectionMetadata(sectionId, "Person"));
        var draft = new FormSectionAuthoringDraft(
            sectionId, 1, _actorId, DateTimeOffset.UtcNow, null, null, data);
        (await _storage.FormSectionStorage.TrySave(draft, 0)).Status
            .Should().Be(StorageSystem.FormSectionAuthoringWriteStatus.Written);
        (await _storage.FormSectionStorage.TryPublish(sectionId, 1, Guid.NewGuid())).Status
            .Should().Be(StorageSystem.FormSectionAuthoringPublishStatus.Published);
        return sectionId;
    }

    private FormAuthoringService Service() => new(
        new FileSystemTransactionalStorageProvider(),
        new FixedCurrentUserAccessor(_actorId),
        TimeProvider.System);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previousRoot);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class FixedCurrentUserAccessor(Guid actorId) : ICurrentUserContextAccessor
    {
        public CurrentUserContext GetCurrentUser() => new(actorId, "test", false);
    }
}
