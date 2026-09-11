using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FilesystemStorageSystem;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Model;
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

[NonParallelizable]
public sealed class FormLibraryControllerIntegrationTest
{
    // Testzweck: Bestehende Dateiinstallationen verlieren beim Upgrade weder Abschnitts-ID
    // noch Versions-ID oder Schema; der frühere Abschnitt erscheint als reguläres Formular.
    [Test]
    public async Task FilesystemStorage_ShouldMigrateLegacySectionIntoFormLibraryOnRestart()
    {
        var previousRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
        var root = Path.Combine(Path.GetTempPath(), $"flowzer-form-migration-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, root);
        try
        {
            var sectionId = Guid.NewGuid();
            var versionId = Guid.NewGuid();
            var schema = """{"components":[{"type":"textfield","key":"street"}]}""";
            var first = new Storage();
            await first.FormSectionStorage.CreateMetadata(new FormSectionMetadata(sectionId, "Adresse"));
            var written = await first.FormSectionStorage.TrySave(new FormSectionAuthoringDraft(
                sectionId, 1, Guid.NewGuid(), DateTimeOffset.UtcNow, null, null, schema), 0);
            written.Status.Should().Be(FormSectionAuthoringWriteStatus.Written);
            var published = await first.FormSectionStorage.TryPublish(sectionId, 1, versionId);
            published.Status.Should().Be(FormSectionAuthoringPublishStatus.Published);

            var reopened = new Storage();
            var metadata = await reopened.FormStorage.GetFormMetaData(sectionId);
            var version = (await reopened.FormStorage.GetForms(sectionId)).Single();

            metadata.Name.Should().Be("Adresse");
            version.Id.Should().Be(versionId);
            version.FormId.Should().Be(sectionId);
            version.FormData.Should().Be(schema);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, previousRoot);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    // Testzweck: Formulare lassen sich in hierarchischen Katalogordnern organisieren, ohne
    // dass die Ordnerkennung Teil einer veröffentlichten Formularversion wird.
    [Test]
    public async Task FolderApi_ShouldCreateHierarchyAndMoveForm()
    {
        using var context = new Context();
        using var client = context.CreateClient();
        var parent = await CreateFolder(client, "Personal");
        var child = await CreateFolder(client, "Abwesenheit", parent.Id);
        var formId = Guid.NewGuid();

        (await client.PostAsJsonAsync($"/form/meta/{formId}", new FormMetaDataDto
        {
            FormId = formId,
            Name = "Urlaubsantrag",
            FolderId = child.Id
        })).EnsureSuccessStatusCode();

        var metadata = await Read<FormMetaDataDto>(await client.GetAsync($"/form/meta/{formId}"));
        metadata.FolderId.Should().Be(child.Id);
        var folders = await Read<FormFolderDto[]>(await client.GetAsync("/form/folders"));
        folders.Should().ContainEquivalentOf(parent);
        folders.Should().ContainEquivalentOf(child);

        var moved = await Read<FormMetaDataDto>(await client.PutAsJsonAsync(
            $"/form/meta/{formId}/folder", new MoveFormRequestDto { FolderId = parent.Id }));
        moved.FolderId.Should().Be(parent.Id);
    }

    // Testzweck: Ein Verschieben unter einen eigenen Nachfahren wird serverseitig verhindert;
    // der Browser ist keine Vertrauensgrenze für die Baumkonsistenz.
    [Test]
    public async Task FolderApi_ShouldRejectCycles()
    {
        using var context = new Context();
        using var client = context.CreateClient();
        var parent = await CreateFolder(client, "Personal");
        var child = await CreateFolder(client, "Abwesenheit", parent.Id);

        var response = await client.PutAsJsonAsync($"/form/folders/{parent.Id}", new FormFolderRequestDto
        {
            Name = parent.Name,
            ParentId = child.Id
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("form_folder.cycle");
    }

    // Testzweck: Ordner mit Unterordnern oder Formularen werden nicht implizit rekursiv
    // gelöscht; eine versehentliche Katalogbereinigung darf keine Formulare entfernen.
    [Test]
    public async Task FolderApi_ShouldDeleteOnlyEmptyFolders()
    {
        using var context = new Context();
        using var client = context.CreateClient();
        var parent = await CreateFolder(client, "Personal");
        var child = await CreateFolder(client, "Abwesenheit", parent.Id);

        (await client.DeleteAsync($"/form/folders/{parent.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.DeleteAsync($"/form/folders/{child.Id}")).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/form/folders/{parent.Id}")).EnsureSuccessStatusCode();
    }

    // Testzweck: Der Picker bekommt nur IDs und Versionsnummern, nicht die vollständigen
    // Schemata aller Formulare; die konkrete Fassung wird erst beim Publish serverseitig gelesen.
    [Test]
    public async Task VersionsApi_ShouldReturnDataSparsePublishedVersionList()
    {
        using var context = new Context();
        using var client = context.CreateClient();
        var formId = Guid.NewGuid();
        (await client.PostAsJsonAsync($"/form/meta/{formId}", new FormMetaDataDto
        {
            FormId = formId,
            Name = "Adresse"
        })).EnsureSuccessStatusCode();
        var published = await Read<FormDto>(await client.PostAsJsonAsync("/form", new FormDto
        {
            FormId = formId,
            FormData = """{"display":"form","components":[{"type":"textfield","key":"street"}]}"""
        }));

        var versions = await Read<FormVersionSummaryDto[]>(await client.GetAsync($"/form/{formId}/versions"));

        versions.Should().ContainSingle();
        published.Id.Should().NotBeNull();
        versions[0].Id.Should().Be(published.Id!.Value);
        versions[0].Version.ToString().Should().Be("0.1");
    }

    // Testzweck: Vorschau und Veröffentlichung benutzen dieselbe serverseitige Expansion;
    // der gespeicherte Snapshot hat keine Laufzeitabhängigkeit von der Komponentenbibliothek.
    [Test]
    public async Task AuthoringApi_ShouldPreviewAndPublishAFormComponentAsExpandedSnapshot()
    {
        using var context = new Context();
        using var client = context.CreateClient();
        var componentId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        await SaveMetadata(client, componentId, "Adresse");
        await SaveMetadata(client, parentId, "Antrag");
        await Read<FormDto>(await client.PostAsJsonAsync("/form", new FormDto
        {
            FormId = componentId,
            FormData = """{"display":"form","components":[{"type":"textfield","key":"street"}]}"""
        }));
        var draftData = $$"""
            {"display":"form","components":[{"type":"flowzerForm","key":"address","label":"Adresse","formId":"{{componentId}}","version":"0.1"}]}
            """;
        var draft = await Read<FormAuthoringDraftDto>(await client.PutAsJsonAsync(
            $"/form/{parentId}/draft",
            new SaveFormAuthoringDraftRequestDto { ExpectedRevision = 0, FormData = draftData }));

        var preview = await Read<FormAuthoringPreviewDto>(await client.PostAsJsonAsync(
            $"/form/{parentId}/preview", new PreviewFormAuthoringRequestDto { FormData = draftData }));
        var published = await Read<FormDto>(await client.PostAsJsonAsync(
            $"/form/{parentId}/publish", new PublishFormAuthoringDraftRequestDto { ExpectedRevision = draft.Revision }));

        preview.FormData.Should().Contain("street").And.NotContain("flowzerForm");
        published.FormData.Should().Be(preview.FormData);
        published.FormData.Should().Contain("boundForms");
    }

    // Testzweck: Der frühere Abschnittsendpunkt bleibt kompatibel, bildet aber keinen
    // getrennten Bestand mehr; reguläre Formulare sind dort derselbe Katalogeintrag.
    [Test]
    public async Task LegacySectionApi_ShouldReadTheSharedFormCatalog()
    {
        using var context = new Context();
        using var client = context.CreateClient();
        var formId = Guid.NewGuid();
        await SaveMetadata(client, formId, "Adresse");

        var legacy = await Read<FormSectionMetadataDto[]>(await client.GetAsync("/form-section"));

        legacy.Should().ContainSingle(item => item.SectionId == formId && item.Name == "Adresse");
    }

    private static async Task<FormFolderDto> CreateFolder(HttpClient client, string name, Guid? parentId = null)
    {
        var response = await client.PostAsJsonAsync("/form/folders", new FormFolderRequestDto
        {
            Name = name,
            ParentId = parentId
        });
        response.EnsureSuccessStatusCode();
        return await Read<FormFolderDto>(response);
    }

    private static async Task SaveMetadata(HttpClient client, Guid formId, string name)
    {
        var response = await client.PostAsJsonAsync($"/form/meta/{formId}", new FormMetaDataDto
        {
            FormId = formId,
            Name = name
        });
        response.EnsureSuccessStatusCode();
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<T>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        return payload.Result!;
    }

    private sealed class Context : IDisposable
    {
        private readonly string? _previousRoot;
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"flowzer-form-library-{Guid.NewGuid():N}");
        private readonly TestWebApplicationFactory _factory;

        public Context()
        {
            _previousRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _root);
            var storage = new TransactionalStorage();
            _factory = new TestWebApplicationFactory(storage);
        }

        public HttpClient CreateClient()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Flowzer-UserId", Guid.NewGuid().ToString());
            return client;
        }

        public void Dispose()
        {
            _factory.Dispose();
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previousRoot);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TestWebApplicationFactory(TransactionalStorage storage) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["TimerScheduler:Enabled"] = "false",
                    ["UserTaskDeadlines:Enabled"] = "false"
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStorageSystem>();
                services.RemoveAll<ITransactionalStorageProvider>();
                services.AddSingleton<IStorageSystem>(storage);
                services.AddSingleton<ITransactionalStorageProvider>(new FixedStorageProvider(storage));
            });
        }
    }

    private sealed class FixedStorageProvider(TransactionalStorage storage) : ITransactionalStorageProvider
    {
        public ITransactionalStorage GetTransactionalStorage() => storage;
    }
}
