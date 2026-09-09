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
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Oeffentlicher HTTP-Vertrag der hostneutralen Abschnittsbibliothek.</summary>
[NonParallelizable]
public sealed class FormSectionControllerIntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Testzweck: Modellierer koennen einen Abschnitt als Entwurf mit CAS pflegen,
    // unveraenderlich publizieren und anschliessend nur konkrete Versionen abrufen.
    [Test]
    public async Task AuthoringApi_ShouldCreateDraftPublishAndReadConcreteVersion()
    {
        using var context = new Context();
        using var client = context.CreateClient();

        var create = await client.PostAsJsonAsync("/form-section", new CreateFormSectionRequestDto
        {
            Name = " Applicant "
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var metadata = (await create.Content.ReadFromJsonAsync<ApiStatusResult<FormSectionMetadataDto>>(JsonOptions))!
            .Result!;
        metadata.Name.Should().Be("Applicant");

        var baseline = await client.GetFromJsonAsync<ApiStatusResult<FormSectionAuthoringDraftDto>>(
            $"/form-section/{metadata.SectionId}/draft", JsonOptions);
        baseline!.Result!.Revision.Should().Be(0);
        baseline.Result.HasDraft.Should().BeFalse();

        const string sectionData = """
            {"flowzer":{"contractVersion":3},"components":[{"type":"textfield","key":"requester"}]}
            """;
        var saved = await client.PutAsJsonAsync($"/form-section/{metadata.SectionId}/draft",
            new SaveFormSectionAuthoringDraftRequestDto
            {
                ExpectedRevision = 0,
                SectionData = sectionData
            });
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        var stale = await client.PutAsJsonAsync($"/form-section/{metadata.SectionId}/draft",
            new SaveFormSectionAuthoringDraftRequestDto
            {
                ExpectedRevision = 0,
                SectionData = "{\"components\":[{\"type\":\"textfield\",\"key\":\"SECRET_STALE\"}]}"
            });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var staleBody = await stale.Content.ReadAsStringAsync();
        staleBody.Should().Contain("form_section_draft.revision_conflict").And.NotContain("SECRET_STALE");

        var publish = await client.PostAsJsonAsync($"/form-section/{metadata.SectionId}/publish",
            new PublishFormSectionAuthoringDraftRequestDto { ExpectedRevision = 1 });
        publish.StatusCode.Should().Be(HttpStatusCode.OK);
        var published = (await publish.Content.ReadFromJsonAsync<ApiStatusResult<FormSectionVersionDto>>(JsonOptions))!
            .Result!;
        published.Version.ToString().Should().Be("0.1");
        published.SectionData.Should().Be(sectionData);

        var versions = await client.GetFromJsonAsync<ApiStatusResult<FormSectionVersionSummaryDto[]>>(
            $"/form-section/{metadata.SectionId}/versions", JsonOptions);
        versions!.Result.Should().ContainSingle(item => item.Id == published.Id && item.Version.ToString() == "0.1");
        var concrete = await client.GetFromJsonAsync<ApiStatusResult<FormSectionVersionDto>>(
            $"/form-section/{metadata.SectionId}/versions/0.1", JsonOptions);
        concrete!.Result!.SectionData.Should().Be(sectionData);
    }

    // Testzweck: Unsichere Abschnittsinhalte werden erst beim Publish stabil als 422
    // abgelehnt; der gemeinsame Entwurf bleibt danach zur Korrektur erhalten.
    [Test]
    public async Task Publish_ShouldRejectScriptWithoutLeakingItsContentAndKeepDraft()
    {
        using var context = new Context();
        using var client = context.CreateClient();
        var metadata = await CreateSection(client);
        const string unsafeData = """
            {"components":[{"type":"textfield","key":"name","calculateValue":"SECRET_SCRIPT"}]}
            """;
        (await client.PutAsJsonAsync($"/form-section/{metadata.SectionId}/draft",
            new SaveFormSectionAuthoringDraftRequestDto
            {
                ExpectedRevision = 0,
                SectionData = unsafeData
            })).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.PostAsJsonAsync($"/form-section/{metadata.SectionId}/publish",
            new PublishFormSectionAuthoringDraftRequestDto { ExpectedRevision = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("schema.script").And.NotContain("SECRET_SCRIPT");
        var draft = await client.GetFromJsonAsync<ApiStatusResult<FormSectionAuthoringDraftDto>>(
            $"/form-section/{metadata.SectionId}/draft", JsonOptions);
        draft!.Result!.Revision.Should().Be(1);
        draft.Result.HasDraft.Should().BeTrue();
    }

    // Testzweck: Nur kanonische konkrete Versionspfade sind gueltig; latest bleibt
    // auch auf der Lese-API gesperrt und wird nicht still auf eine Fassung aufgeloest.
    [Test]
    public async Task VersionApi_ShouldRejectSymbolicLatest()
    {
        using var context = new Context();
        using var client = context.CreateClient();
        var metadata = await CreateSection(client);

        var response = await client.GetAsync($"/form-section/{metadata.SectionId}/versions/latest");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // Testzweck: Fehlende Abschnitte und konkrete Fassungen liefern denselben kleinen
    // Problem-Details-Vertrag, ohne angefragte IDs oder Versionswerte zurueckzuspiegeln.
    [Test]
    public async Task ReadApi_ShouldReturnValueFreeProblemDetailsForMissingResources()
    {
        using var context = new Context();
        using var client = context.CreateClient();
        var missingId = Guid.NewGuid();
        var missingSection = await client.GetAsync($"/form-section/{missingId}");
        var metadata = await CreateSection(client);
        var missingVersion = await client.GetAsync($"/form-section/{metadata.SectionId}/versions/7.9");

        missingSection.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingVersion.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingSection.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        missingVersion.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var sectionBody = await missingSection.Content.ReadAsStringAsync();
        var versionBody = await missingVersion.Content.ReadAsStringAsync();
        sectionBody.Should().Contain("form_section.not_found").And.NotContain(missingId.ToString());
        versionBody.Should().Contain("form_section.version_not_found")
            .And.NotContain(metadata.SectionId.ToString()).And.NotContain("7.9");
    }

    private static async Task<FormSectionMetadataDto> CreateSection(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/form-section", new CreateFormSectionRequestDto
        {
            Name = "Applicant"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiStatusResult<FormSectionMetadataDto>>(JsonOptions))!
            .Result!;
    }

    private sealed class Context : IDisposable
    {
        private readonly string? _previousRoot;
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"flowzer-section-api-{Guid.NewGuid():N}");
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
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TimerScheduler:Enabled"] = "false",
                    ["UserTaskDeadlines:Enabled"] = "false"
                });
            });
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
