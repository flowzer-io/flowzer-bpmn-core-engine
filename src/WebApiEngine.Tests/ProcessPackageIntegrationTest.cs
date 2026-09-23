using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FilesystemStorageSystem;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Model;
using StorageSystem;
using WebApiEngine.ProcessPackages;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Prozesspakete von Ende zu Ende: Ein Workflow verlaesst eine Installation als eine Datei und
/// kommt in einer anderen wieder an — ohne Secrets, ohne Personenkennungen und ohne dass
/// irgendetwas stillschweigend zugeordnet wird.
/// </summary>
[NonParallelizable]
public sealed class ProcessPackageIntegrationTest
{
    // Testzweck: Das Paket enthält das BPMN der veröffentlichten Fassung, die daran gebundenen
    // Formulare und ein Manifest — und nirgends im Archiv eine Personen-, Gruppen- oder
    // Verbindungskennung oder einen geheimen Wert.
    [Test]
    public async Task Export_ShouldCarryWorkflowAndFormsWithoutSecretsOrIdentities()
    {
        using var context = new Context();
        var seeded = await context.SeedDeployedWorkflowAsync();
        using var client = context.CreateClient(isModeler: true);

        using var response = await client.GetAsync($"/definition/meta/{seeded.DefinitionId}/package");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/zip");
        response.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".flowzer.zip");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var entries = ReadEntries(bytes);

        entries.Keys.Should().Contain([ProcessPackageFormat.ManifestEntry, ProcessPackageFormat.WorkflowEntry,
            ProcessPackageFormat.ReadmeEntry]);
        entries.Keys.Should().Contain(name => name.StartsWith("forms/", StringComparison.Ordinal));

        var manifest = ReadManifest(entries);
        manifest.Format.Should().Be(ProcessPackageFormat.FormatName);
        manifest.FormatVersion.Should().Be(1);
        manifest.Workflow.DefinitionId.Should().Be(seeded.DefinitionId);
        manifest.Workflow.Source.Should().Be(ProcessPackageFormat.SourceDeployed);
        manifest.Workflow.ProcessIds.Should().Contain("Process_Package");
        manifest.Forms.Should().ContainSingle()
            .Which.Should().Match<ProcessPackageFormDto>(form =>
                form.FormId == seeded.FormId && form.Revision == "1.0" && !form.Embedded);
        entries[manifest.Forms[0].File].Should().Contain("\"key\":\"reason\"");

        // Der eigentliche Vertrag: Das Archiv darf nichts enthalten, was nur hier gilt.
        var archiveText = string.Join("\n", entries.Values);
        archiveText.Should().NotContain(seeded.UserId.ToString());
        archiveText.Should().NotContain(seeded.GroupId.ToString());

        // Stattdessen stehen die Anzeigenamen im Manifest und Platzhalter im Modell.
        manifest.References.Should().Contain(reference =>
            reference.Kind == ProcessPackageReferenceKinds.DirectoryGroup && reference.Label == "/team/pruefung");
        manifest.References.Should().Contain(reference =>
            reference.Kind == ProcessPackageReferenceKinds.DirectoryUser && reference.Label == "Berta Beispiel");
        manifest.References.Should().Contain(reference =>
            reference.Kind == ProcessPackageReferenceKinds.JobType && reference.Label == "invoice.check"
            && !reference.RequiresMapping);
        manifest.References.Should().Contain(reference =>
            reference.Kind == ProcessPackageReferenceKinds.Secret && reference.Label == "INVOICE_TOKEN"
            && !reference.RequiresMapping);
        entries[ProcessPackageFormat.WorkflowEntry].Should().Contain("f1002ef0-0000-4000-8000-");
    }

    // Testzweck: Ein Workflow ohne Veröffentlichung lässt sich als Entwurf exportieren; das
    // Manifest sagt das ausdrücklich, statt einen veröffentlichten Stand vorzutäuschen.
    [Test]
    public async Task Export_ShouldMarkAnUnpublishedWorkflowAsDraft()
    {
        using var context = new Context();
        var seeded = await context.SeedSavedWorkflowAsync();
        using var client = context.CreateClient(isModeler: true);

        using var response = await client.GetAsync($"/definition/meta/{seeded.DefinitionId}/package");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var manifest = ReadManifest(ReadEntries(await response.Content.ReadAsByteArrayAsync()));
        manifest.Workflow.Source.Should().Be(ProcessPackageFormat.SourceDraft);
        manifest.References.Should().Contain(reference =>
            reference.Kind == ProcessPackageReferenceKinds.AiConnection && reference.Label == "Hausmodell"
            && reference.RequiresMapping);

        // Die Verbindung wird beim Namen genannt — ihre Kennung und vor allem ihre
        // Secret-Referenz bleiben in der Quellinstallation.
        var entries = ReadEntries(await response.Content.ReadAsByteArrayAsync());
        var archiveText = string.Join("\n", entries.Values);
        archiveText.Should().NotContain("env:FLOWZER_AI_KEY");
        archiveText.Should().NotContain(seeded.ConnectionId.ToString());
    }

    // Testzweck: Ein Workflow, den es nicht gibt, führt zu einer sauberen 404 und nicht zu
    // einem Serverfehler auf dem Downloadpfad.
    [Test]
    public async Task Export_ShouldAnswerNotFoundForAnUnknownWorkflow()
    {
        using var context = new Context();
        using var client = context.CreateClient(isModeler: true);

        using var response = await client.GetAsync("/definition/meta/gibt-es-nicht/package");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Die Vorschau nennt Manifest, Prüfergebnis, die zuzuordnenden Bezüge samt
    // gleichnamigem Vorschlag und den Konflikt mit einer bereits vergebenen Kennung.
    [Test]
    public async Task Preview_ShouldReportReferencesSuggestionsAndConflict()
    {
        using var source = new Context();
        var seeded = await source.SeedDeployedWorkflowAsync();
        var package = await source.ExportAsync(seeded.DefinitionId);

        // Dieselbe Installation liest das Paket wieder ein: Kennung und Verzeichnis sind
        // vorhanden, also gibt es einen Konflikt und für jeden Bezug einen Vorschlag.
        using var client = source.CreateClient(isModeler: true);
        var preview = await PreviewAsync(client, package);

        preview.Manifest.Workflow.DefinitionId.Should().Be(seeded.DefinitionId);
        preview.DeployableHere.Should().BeTrue();
        preview.FormsContractSupported.Should().BeTrue();
        preview.Problems.Should().BeEmpty();
        preview.Conflict.Should().NotBeNull();
        preview.Conflict!.DefinitionId.Should().Be(seeded.DefinitionId);
        preview.Conflict.MayCreateNewVersion.Should().BeTrue();

        var group = preview.References.Single(option =>
            option.Reference.Kind == ProcessPackageReferenceKinds.DirectoryGroup);
        group.Candidates.Should().Contain(candidate => candidate.Id == seeded.GroupId.ToString());
        group.SuggestedId.Should().Be(seeded.GroupId.ToString());

        var jobType = preview.References.Single(option =>
            option.Reference.Kind == ProcessPackageReferenceKinds.JobType);
        jobType.Candidates.Should().BeEmpty();
        jobType.SuggestedId.Should().BeNull();
    }

    // Testzweck: Der Import als neuer Workflow legt Katalogeintrag, Fassung und Formular an,
    // setzt die zugeordneten Kennungen ins Modell und veröffentlicht ausdrücklich nicht.
    [Test]
    public async Task Import_ShouldCreateWorkflowWithMappedReferencesAndWithoutDeploying()
    {
        using var source = new Context();
        var seeded = await source.SeedDeployedWorkflowAsync();
        var package = await source.ExportAsync(seeded.DefinitionId);

        using var target = new Context();
        var (targetUserId, targetGroupId) = await target.SeedDirectoryAsync(
            "carla", "Carla Ziel", "/team/ziel");
        using var client = target.CreateClient(isModeler: true);

        var preview = await PreviewAsync(client, package);
        preview.Conflict.Should().BeNull();

        var mapping = new ProcessPackageMappingDto
        {
            Mode = ProcessPackageFormat.ModeNew,
            Name = "Importierte Rechnungsprüfung",
            References = preview.References
                .Where(option => option.Reference.RequiresMapping)
                .ToDictionary(
                    option => option.Reference.Id,
                    option => option.Reference.Kind == ProcessPackageReferenceKinds.DirectoryGroup
                        ? targetGroupId.ToString()
                        : targetUserId.ToString())
        };

        var result = await ImportAsync(client, package, mapping);

        result.Name.Should().Be("Importierte Rechnungsprüfung");
        result.Version.Major.Should().Be(1);
        result.Forms.Should().ContainSingle().Which.Outcome.Should().Be("created");
        result.Notices.Should().Contain(notice => notice.Code == "package.import.not_deployed");

        var stored = await target.Storage.DefinitionStorage.GetBinary(result.VersionId);
        stored.Should().Contain(targetGroupId.ToString());
        stored.Should().Contain(targetUserId.ToString());
        stored.Should().NotContain("f1002ef0-0000-4000-8000-");
        stored.Should().Contain($"id=\"{result.DefinitionId}\"");

        // Der Form-Key zeigt auf genau den Stand, den das Paket mitgebracht hat.
        var importedForm = result.Forms[0];
        stored.Should().Contain($"formKey=\"{importedForm.Name}:{importedForm.Revision}\"");

        (await target.Storage.DefinitionStorage.GetDeployedDefinition(result.DefinitionId)).Should().BeNull();
        (await target.Storage.FormStorage.GetFormMetaData(importedForm.FormId!.Value)).Name
            .Should().Be("Rechnungsprüfung");
    }

    // Testzweck: Ein Bezug ohne Zuordnung lässt keine halb aufgelöste Definition entstehen.
    [Test]
    public async Task Import_ShouldRejectAnIncompleteMapping()
    {
        using var source = new Context();
        var seeded = await source.SeedDeployedWorkflowAsync();
        var package = await source.ExportAsync(seeded.DefinitionId);

        using var target = new Context();
        using var client = target.CreateClient(isModeler: true);

        using var response = await PostPackageAsync(client, "/definition/package/import", package,
            new ProcessPackageMappingDto { Mode = ProcessPackageFormat.ModeNew });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessPackageImportResultDto>>();
        payload!.ErrorMessage.Should().Contain("fehlt eine Zuordnung");
        (await target.Storage.DefinitionStorage.GetAllMetaDefinitions()).Should().BeEmpty();
    }

    // Testzweck: Der Import als neuer Stand hängt sich an den vorhandenen Katalogeintrag, legt
    // keinen zweiten an und erhöht die Fassung.
    [Test]
    public async Task Import_ShouldAddANewVersionToAnExistingWorkflow()
    {
        using var context = new Context();
        var seeded = await context.SeedDeployedWorkflowAsync();
        var package = await context.ExportAsync(seeded.DefinitionId);
        using var client = context.CreateClient(isModeler: true);

        var result = await ImportAsync(client, package, new ProcessPackageMappingDto
        {
            Mode = ProcessPackageFormat.ModeNewVersionOf,
            DefinitionId = seeded.DefinitionId,
            References = await MapOntoItselfAsync(client, package, seeded)
        });

        result.DefinitionId.Should().Be(seeded.DefinitionId);
        (await context.Storage.DefinitionStorage.GetAllMetaDefinitions()).Should().ContainSingle();
        var versions = (await context.Storage.DefinitionStorage.GetAllDefinitions())
            .Where(definition => definition.DefinitionId == seeded.DefinitionId).ToArray();
        versions.Should().HaveCountGreaterThan(1);
        versions.Should().ContainSingle(definition => definition.Id == result.VersionId);
    }

    // Testzweck: Ein Formular, das hier schon in genau diesem Inhalt steht, wird wiederverwendet
    // statt unter derselben Kennung eine zweite, gleiche Fassung anzulegen.
    [Test]
    public async Task Import_ShouldReuseAnIdenticalFormInsteadOfCreatingARevision()
    {
        using var context = new Context();
        var seeded = await context.SeedDeployedWorkflowAsync();
        var package = await context.ExportAsync(seeded.DefinitionId);
        using var client = context.CreateClient(isModeler: true);

        var result = await ImportAsync(client, package, new ProcessPackageMappingDto
        {
            Mode = ProcessPackageFormat.ModeNew,
            DefinitionId = "workflow-package-kopie",
            References = await MapOntoItselfAsync(client, package, seeded)
        });

        result.Forms.Should().ContainSingle().Which.Should().Match<ProcessPackageImportedFormDto>(form =>
            form.Outcome == "reused" && form.FormId == seeded.FormId && form.Revision == "1.0");
        (await context.Storage.FormStorage.GetForms(seeded.FormId)).Should().ContainSingle();
    }

    // Testzweck: Derselbe Formularstand mit verändertem Inhalt wird zur neuen Fassung desselben
    // Formulars; der vorhandene Stand bleibt unangetastet.
    [Test]
    public async Task Import_ShouldAddARevisionWhenTheSameFormChanged()
    {
        using var source = new Context();
        var seeded = await source.SeedDeployedWorkflowAsync();
        var package = await source.ExportAsync(seeded.DefinitionId);

        using var target = new Context();
        await target.Storage.FormStorage.SaveFormMetaData(
            new FormMetadata { FormId = seeded.FormId, Name = "Rechnungsprüfung" });
        await target.Storage.FormStorage.SaveForm(new Form
        {
            Id = Guid.NewGuid(), FormId = seeded.FormId, Version = new Model.Version(1, 0),
            FormData = """{"components":[{"type":"textfield","key":"other"}]}"""
        });
        using var client = target.CreateClient(isModeler: true);

        var result = await ImportAsync(client, package, new ProcessPackageMappingDto
        {
            Mode = ProcessPackageFormat.ModeNew,
            References = await MapOntoItselfAsync(client, package, seeded, allowMissing: true)
        });

        result.Forms.Should().ContainSingle().Which.Should().Match<ProcessPackageImportedFormDto>(form =>
            form.Outcome == "revised" && form.Revision == "1.1");
        (await target.Storage.FormStorage.GetForms(seeded.FormId)).Should().HaveCount(2);
    }

    // Testzweck: Ein Archiv mit einem Eintrag, der aus dem Zielordner herausführt, wird
    // abgelehnt, bevor irgendetwas gelesen oder angelegt wird.
    [Test]
    public async Task Preview_ShouldRejectAZipSlipEntry()
    {
        using var context = new Context();
        using var client = context.CreateClient(isModeler: true);

        var package = BuildArchive(new Dictionary<string, string>
        {
            ["../../etc/passwd"] = "x",
            [ProcessPackageFormat.ManifestEntry] = "{}"
        });

        using var response = await PostPackageAsync(client, "/definition/package/preview", package, mapping: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessPackagePreviewDto>>())!
            .ErrorMessage.Should().Contain("zulässigen Namen");
    }

    // Testzweck: Ein stark komprimiertes Archiv darf den Speicher nicht füllen; gezählt wird die
    // entpackte Größe beim Lesen, nicht die Angabe im Archivverzeichnis.
    [Test]
    public async Task Preview_ShouldRejectAnArchiveThatUnpacksBeyondTheLimit()
    {
        using var context = new Context();
        using var client = context.CreateClient(isModeler: true);

        var package = BuildArchive(new Dictionary<string, string>
        {
            [ProcessPackageFormat.ManifestEntry] = new('a', (int)ProcessPackageFormat.MaxUncompressedBytes + 1024)
        });

        using var response = await PostPackageAsync(client, "/definition/package/preview", package, mapping: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessPackagePreviewDto>>())!
            .ErrorMessage.Should().Contain("MiB");
    }

    // Testzweck: Ohne Manifest ist eine ZIP-Datei kein Prozesspaket und wird als solche abgelehnt.
    [Test]
    public async Task Preview_ShouldRejectAnArchiveWithoutManifest()
    {
        using var context = new Context();
        using var client = context.CreateClient(isModeler: true);

        var package = BuildArchive(new Dictionary<string, string> { [ProcessPackageFormat.WorkflowEntry] = "<x/>" });

        using var response = await PostPackageAsync(client, "/definition/package/preview", package, mapping: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessPackagePreviewDto>>())!
            .ErrorMessage.Should().Contain("package.json");
    }

    // Testzweck: Eine fremde Formatversion wird nicht geraten, sondern als nicht verarbeitbar
    // gemeldet — auch wenn das Archiv sonst vollständig aussieht.
    [Test]
    public async Task Preview_ShouldRejectAnUnsupportedFormatVersion()
    {
        using var context = new Context();
        using var client = context.CreateClient(isModeler: true);

        var package = BuildArchive(new Dictionary<string, string>
        {
            [ProcessPackageFormat.ManifestEntry] =
                $$"""{"format":"{{ProcessPackageFormat.FormatName}}","formatVersion":99}""",
            [ProcessPackageFormat.WorkflowEntry] = "<x/>"
        });

        using var response = await PostPackageAsync(client, "/definition/package/preview", package, mapping: null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessPackagePreviewDto>>())!
            .ErrorMessage.Should().Contain("Formatversion 99");
    }

    // Testzweck: Kein ZIP ist kein Paket — die Antwort bleibt eine saubere Ablehnung und kein
    // Serverfehler.
    [Test]
    public async Task Preview_ShouldRejectSomethingThatIsNotAnArchive()
    {
        using var context = new Context();
        using var client = context.CreateClient(isModeler: true);

        using var response = await PostPackageAsync(client, "/definition/package/preview",
            Encoding.UTF8.GetBytes("Das ist kein Archiv."), mapping: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessPackagePreviewDto>>())!
            .ErrorMessage.Should().Contain("ZIP-Archiv");
    }

    // Testzweck: Exportieren darf, wer den Workflow lesen darf; Vorschau und Import bleiben der
    // Rolle fürs Modellieren vorbehalten, und ohne Anmeldung geht gar nichts.
    [Test]
    public async Task Package_ShouldEnforceRoles()
    {
        using var context = new Context();
        var seeded = await context.SeedDeployedWorkflowAsync();
        var package = await context.ExportAsync(seeded.DefinitionId);

        using var reader = context.CreateClient(isModeler: false);
        using var export = await reader.GetAsync($"/definition/meta/{seeded.DefinitionId}/package");
        export.StatusCode.Should().Be(HttpStatusCode.OK);

        using var preview = await PostPackageAsync(reader, "/definition/package/preview", package, mapping: null);
        preview.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var import = await PostPackageAsync(reader, "/definition/package/import", package,
            new ProcessPackageMappingDto { Mode = ProcessPackageFormat.ModeNew });
        import.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var anonymous = context.CreateAnonymousClient();
        using var unauthenticated = await anonymous.GetAsync($"/definition/meta/{seeded.DefinitionId}/package");
        unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #region Helfer

    private static Dictionary<string, string> ReadEntries(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return archive.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        }, StringComparer.Ordinal);
    }

    private static ProcessPackageManifestDto ReadManifest(IReadOnlyDictionary<string, string> entries) =>
        JsonSerializer.Deserialize<ProcessPackageManifestDto>(
            entries[ProcessPackageFormat.ManifestEntry], ProcessPackageFormat.JsonOptions)!;

    private static byte[] BuildArchive(IReadOnlyDictionary<string, string> entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private static async Task<HttpResponseMessage> PostPackageAsync(
        HttpClient client,
        string path,
        byte[] package,
        ProcessPackageMappingDto? mapping)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(package);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(file, "package", "paket.zip");
        if (mapping is not null)
            content.Add(new StringContent(
                JsonSerializer.Serialize(mapping, ProcessPackageFormat.JsonOptions)), "mapping");

        return await client.PostAsync(path, content);
    }

    private static async Task<ProcessPackagePreviewDto> PreviewAsync(HttpClient client, byte[] package)
    {
        using var response = await PostPackageAsync(client, "/definition/package/preview", package, mapping: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessPackagePreviewDto>>();
        payload!.Successful.Should().BeTrue();
        return payload.Result!;
    }

    private static async Task<ProcessPackageImportResultDto> ImportAsync(
        HttpClient client,
        byte[] package,
        ProcessPackageMappingDto mapping)
    {
        using var response = await PostPackageAsync(client, "/definition/package/import", package, mapping);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            because: await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessPackageImportResultDto>>();
        payload!.Successful.Should().BeTrue();
        return payload.Result!;
    }

    /// <summary>Zuordnung zurück auf dieselben Einträge — für Fälle, in denen nur der Rest zählt.</summary>
    private static async Task<Dictionary<string, string>> MapOntoItselfAsync(
        HttpClient client,
        byte[] package,
        Context.SeededWorkflow seeded,
        bool allowMissing = false)
    {
        var preview = await PreviewAsync(client, package);
        return preview.References
            .Where(option => option.Reference.RequiresMapping)
            .ToDictionary(
                option => option.Reference.Id,
                option => option.SuggestedId
                    ?? (allowMissing
                        ? (option.Reference.Kind == ProcessPackageReferenceKinds.DirectoryGroup
                            ? seeded.GroupId
                            : seeded.UserId).ToString()
                        : throw new InvalidOperationException("Ohne Vorschlag ist der Testaufbau unvollständig.")));
    }

    /// <summary>Isolierte Dateiablage, echte Engine und signierte synthetische Identitäten.</summary>
    private sealed class Context : IDisposable
    {
        private const string Issuer = "https://issuer.test/realms/flowzer";
        private const string Audience = "flowzer-api";
        // Ausschliesslich synthetischer Signaturschluessel fuer den lokalen Test-IdP.
        private static readonly SymmetricSecurityKey SigningKey =
            new(Encoding.UTF8.GetBytes("flowzer-test-only-signing-key-32-bytes-or-more"));

        private readonly string? _previousRoot =
            Environment.GetEnvironmentVariable(FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName);
        private readonly string _root =
            Path.Combine(Path.GetTempPath(), "flowzer-package-test", Guid.NewGuid().ToString("N"));
        private readonly WebApplicationFactory<Program> _factory;

        internal Context()
        {
            Environment.SetEnvironmentVariable(FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName, _root);
            Storage = new TransactionalStorage();
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("TimerScheduler:Enabled", "false");
                builder.UseSetting("UserTaskDeadlines:Enabled", "false");
                builder.UseSetting("ServiceTaskWebhooks:Enabled", "false");
                builder.UseSetting("RateLimiting:Enabled", "false");
                builder.UseSetting("Authentication:Scheme", "JwtBearer");
                builder.UseSetting("Authentication:JwtBearer:Authority", Issuer);
                builder.UseSetting("Authentication:JwtBearer:Audience", Audience);
                builder.UseSetting("Authentication:JwtBearer:RequiredRole", "access");
                builder.UseSetting("Authentication:JwtBearer:Roles:Modeler", "modeler");
                builder.UseSetting("Authentication:JwtBearer:Roles:Operator", "operator");
                builder.UseSetting("Authentication:JwtBearer:Roles:Worker", "worker");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IStorageSystem>();
                    services.RemoveAll<ITransactionalStorageProvider>();
                    services.AddSingleton<IStorageSystem>(Storage);
                    services.AddSingleton<ITransactionalStorageProvider>(new FixedStorageProvider(Storage));
                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    {
                        options.TokenValidationParameters.ValidIssuers = [Issuer];
                        options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                            new OpenIdConnectConfiguration { Issuer = Issuer, SigningKeys = { SigningKey } });
                    });
                });
            });
        }

        internal TransactionalStorage Storage { get; }

        internal sealed record SeededWorkflow(
            string DefinitionId, Guid FormId, Guid UserId, Guid GroupId, Guid ConnectionId = default);

        internal HttpClient CreateClient(bool isModeler)
        {
            var claims = new List<Claim>
            {
                new("sub", Guid.NewGuid().ToString()), new("preferred_username", "tester"), new("roles", "access")
            };
            if (isModeler) claims.Add(new Claim("roles", "modeler"));
            var jwt = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = Issuer, Audience = Audience, Expires = DateTime.UtcNow.AddMinutes(5),
                SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
                Subject = new ClaimsIdentity(claims)
            });
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            return client;
        }

        internal HttpClient CreateAnonymousClient() => _factory.CreateClient();

        internal async Task<byte[]> ExportAsync(string definitionId)
        {
            using var client = CreateClient(isModeler: true);
            using var response = await client.GetAsync($"/definition/meta/{definitionId}/package");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <summary>
        /// Veroeffentlicht einen Verzeichnisstand und liefert die lokalen Kennungen zurueck. Sie
        /// werden beim Publizieren aus den externen Schluesseln abgeleitet und nicht uebernommen;
        /// wer sie raet, testet an der Ablage vorbei.
        /// </summary>
        internal async Task<(Guid UserId, Guid GroupId)> SeedDirectoryAsync(
            string subject,
            string displayName,
            string groupPath)
        {
            var generationId = Guid.NewGuid();
            var completedAt = DateTime.UtcNow;
            (await Storage.IdentityDirectoryStorage.TryStartSync(
                Issuer, generationId, completedAt.AddSeconds(-1), completedAt.AddMinutes(5))).Should().BeTrue();
            await Storage.IdentityDirectoryStorage.PublishSnapshot(new DirectorySnapshot
            {
                GenerationId = generationId,
                Issuer = Issuer,
                CompletedAtUtc = completedAt,
                Users =
                [
                    new DirectoryUser
                    {
                        Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak, Issuer = Issuer,
                        Subject = subject, DisplayName = displayName, IsActive = true
                    }
                ],
                Groups =
                [
                    new DirectoryGroup
                    {
                        Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak, Issuer = Issuer,
                        ExternalId = groupPath, Name = groupPath.Split('/').Last(),
                        Path = groupPath, IsActive = true
                    }
                ]
            });

            var published = (await Storage.IdentityDirectoryStorage.GetActiveSnapshot())!;
            return (published.Users.Single(user => user.Subject == subject).Id,
                published.Groups.Single(group => group.ExternalId == groupPath).Id);
        }

        /// <summary>Ein veröffentlichter Workflow mit Verzeichniszuweisung, Worker und Secret-Name.</summary>
        internal async Task<SeededWorkflow> SeedDeployedWorkflowAsync()
        {
            const string definitionId = "workflow-package";
            var (userId, groupId) = await SeedDirectoryAsync(
                "berta", "Berta Beispiel", "/team/pruefung");

            var formId = Guid.NewGuid();
            await Storage.FormStorage.SaveFormMetaData(new FormMetadata { FormId = formId, Name = "Rechnungsprüfung" });
            await Storage.FormStorage.SaveForm(new Form
            {
                Id = Guid.NewGuid(), FormId = formId, Version = new Model.Version(1, 0),
                FormData = """{"components":[{"type":"textfield","key":"reason"}]}"""
            });

            using var client = CreateClient(isModeler: true);
            using var meta = await client.PostAsJsonAsync("/definition/meta", new BpmnMetaDefinitionDto
            {
                DefinitionId = definitionId, Name = "Rechnungsprüfung"
            });
            meta.StatusCode.Should().Be(HttpStatusCode.OK);

            using var deploy = await client.PostAsync("/definition/deploy", new StringContent(
                DeployableXml(definitionId, userId, groupId), Encoding.UTF8, "application/xml"));
            deploy.StatusCode.Should().Be(HttpStatusCode.OK, because: await deploy.Content.ReadAsStringAsync());

            return new SeededWorkflow(definitionId, formId, userId, groupId);
        }

        /// <summary>Ein nur gespeicherter Workflow mit einem KI-Task — ohne Veröffentlichung.</summary>
        internal async Task<SeededWorkflow> SeedSavedWorkflowAsync()
        {
            const string definitionId = "workflow-package-entwurf";
            var connectionId = Guid.NewGuid();
            await Storage.AiConnectionStorage.TryCreate(new AiConnection(
                connectionId, "Hausmodell", AiProviderKind.OpenAi, AiProcessingLocation.Cloud, null,
                "model-a", "env:FLOWZER_AI_KEY", true, 1, DateTimeOffset.UtcNow, Guid.NewGuid()));

            using var client = CreateClient(isModeler: true);
            using var meta = await client.PostAsJsonAsync("/definition/meta", new BpmnMetaDefinitionDto
            {
                DefinitionId = definitionId, Name = "Entwurf mit KI"
            });
            meta.StatusCode.Should().Be(HttpStatusCode.OK);

            using var save = await client.PostAsync("/definition", new StringContent(
                DraftXml(definitionId, connectionId), Encoding.UTF8, "application/xml"));
            save.StatusCode.Should().Be(HttpStatusCode.OK, because: await save.Content.ReadAsStringAsync());

            return new SeededWorkflow(definitionId, Guid.Empty, Guid.Empty, Guid.Empty, connectionId);
        }

        private static string DeployableXml(string definitionId, Guid userId, Guid groupId) => $$"""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                              xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"
                              id="{{definitionId}}" targetNamespace="test">
              <bpmn:process id="Process_Package" isExecutable="true">
                <bpmn:startEvent id="Start"><bpmn:outgoing>ToCheck</bpmn:outgoing></bpmn:startEvent>
                <bpmn:sequenceFlow id="ToCheck" sourceRef="Start" targetRef="Check" />
                <bpmn:userTask id="Check" name="Rechnung prüfen">
                  <bpmn:extensionElements>
                    <zeebe:formDefinition formKey="Rechnungsprüfung" />
                    <flowzer:taskAssignment mode="directory" assigneeId="{{userId}}"
                                            candidateGroupIds="{{groupId}}" />
                  </bpmn:extensionElements>
                  <bpmn:incoming>ToCheck</bpmn:incoming><bpmn:outgoing>ToBook</bpmn:outgoing>
                </bpmn:userTask>
                <bpmn:sequenceFlow id="ToBook" sourceRef="Check" targetRef="Book" />
                <bpmn:serviceTask id="Book" name="Buchen">
                  <bpmn:extensionElements>
                    <zeebe:taskDefinition type="invoice.check" />
                    <zeebe:ioMapping>
                      <zeebe:input source="secret:INVOICE_TOKEN" target="token" />
                    </zeebe:ioMapping>
                  </bpmn:extensionElements>
                  <bpmn:incoming>ToBook</bpmn:incoming><bpmn:outgoing>ToEnd</bpmn:outgoing>
                </bpmn:serviceTask>
                <bpmn:sequenceFlow id="ToEnd" sourceRef="Book" targetRef="End" />
                <bpmn:endEvent id="End"><bpmn:incoming>ToEnd</bpmn:incoming></bpmn:endEvent>
              </bpmn:process>
            </bpmn:definitions>
            """;

        private static string DraftXml(string definitionId, Guid connectionId) => $$"""
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                              xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"
                              id="{{definitionId}}" targetNamespace="test">
              <bpmn:process id="Process_Draft" isExecutable="true">
                <bpmn:startEvent id="Start"><bpmn:outgoing>ToAi</bpmn:outgoing></bpmn:startEvent>
                <bpmn:sequenceFlow id="ToAi" sourceRef="Start" targetRef="Ai" />
                <bpmn:serviceTask id="Ai" name="Einordnen">
                  <bpmn:extensionElements>
                    <zeebe:taskDefinition type="flowzer.ai.v1" />
                    <flowzer:aiTask contractVersion="1" connectionId="{{connectionId}}" model="model-a"
                        instructionVersion="1" maxInputTokens="4096" maxOutputTokens="1024" timeoutSeconds="60">
                      <flowzer:instruction>Ordne die Anfrage ein.</flowzer:instruction>
                      <flowzer:resultSchema>{"type":"object"}</flowzer:resultSchema>
                    </flowzer:aiTask>
                  </bpmn:extensionElements>
                  <bpmn:incoming>ToAi</bpmn:incoming><bpmn:outgoing>ToEnd</bpmn:outgoing>
                </bpmn:serviceTask>
                <bpmn:sequenceFlow id="ToEnd" sourceRef="Ai" targetRef="End" />
                <bpmn:endEvent id="End"><bpmn:incoming>ToEnd</bpmn:incoming></bpmn:endEvent>
              </bpmn:process>
            </bpmn:definitions>
            """;

        public void Dispose()
        {
            _factory.Dispose();
            Environment.SetEnvironmentVariable(FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName, _previousRoot);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FixedStorageProvider(TransactionalStorage storage) : ITransactionalStorageProvider
    {
        public ITransactionalStorage GetTransactionalStorage() => storage;
    }

    #endregion
}
