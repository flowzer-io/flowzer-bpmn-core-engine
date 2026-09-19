using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FilesystemStorageSystem;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Der Entscheidungskatalog ueber HTTP: anlegen, versionieren, lesen, loeschen und der
/// Trockenlauf.
/// </summary>
[NonParallelizable]
public sealed class DecisionControllerIntegrationTest
{
    // Testzweck: Eine angelegte Datei steht mit ihrer Kennung aus dmn:definitions/@id im
    // Katalog — mit ihren enthaltenen Entscheidungen, aber ohne das XML.
    [Test]
    public async Task Create_ShouldStoreTheFileUnderTheIdFromTheDmnDocument()
    {
        using var context = new Context();
        using var client = context.CreateClient();

        var created = await Read<DecisionDefinitionDto>(await client.PostAsJsonAsync("/decision",
            new SaveDecisionDefinitionRequestDto { Xml = DecisionFixtures.RabattDmn() }));

        created.DecisionDefinitionId.Should().Be(DecisionFixtures.RabattDefinitionId);
        created.Name.Should().Be("Rabattstufen");
        created.Version.Should().Be(1);
        created.Decisions.Should().ContainSingle()
            .Which.DecisionId.Should().Be(DecisionFixtures.RabattDecisionId);

        var catalog = await Read<DecisionDefinitionDto[]>(await client.GetAsync("/decision"));
        catalog.Should().ContainSingle().Which.DecisionDefinitionId
            .Should().Be(DecisionFixtures.RabattDefinitionId);
    }

    // Testzweck: Die Konsole ist gegen genau diese JSON-Namen gebaut. Sie stehen hier roh im
    // Test, damit eine Umbenennung im DTO nicht erst im Browser auffaellt — und damit
    // deployedBy auch dann im Dokument steht, wenn niemand bekannt ist.
    [Test]
    public async Task Catalog_ShouldUseTheJsonNamesTheConsoleExpects()
    {
        using var context = new Context();
        using var client = context.CreateClient();
        await Create(client);

        using var payload = JsonDocument.Parse(
            await (await client.GetAsync("/decision")).Content.ReadAsStringAsync());
        var entry = payload.RootElement.GetProperty("result").EnumerateArray().Single();

        using (new AssertionScope())
        {
            entry.GetProperty("decisionDefinitionId").GetString().Should().Be(DecisionFixtures.RabattDefinitionId);
            entry.GetProperty("name").GetString().Should().Be("Rabattstufen");
            entry.GetProperty("version").GetInt32().Should().Be(1);
            entry.GetProperty("deployedAt").GetString().Should().NotBeNullOrWhiteSpace();
            entry.TryGetProperty("deployedBy", out _).Should().BeTrue();
            var decision = entry.GetProperty("decisions").EnumerateArray().Single();
            decision.GetProperty("decisionId").GetString().Should().Be(DecisionFixtures.RabattDecisionId);
            decision.GetProperty("name").GetString().Should().Be("Rabattstufe");
            entry.TryGetProperty("xml", out _).Should().BeFalse("der Katalog liefert das Dokument nicht mit");
        }
    }

    // Testzweck: Ohne id im DMN vergibt der Server eine Kennung; ohne Namen im Request und
    // ohne @name im Dokument steht die Kennung als Anzeigename.
    [Test]
    public async Task Create_ShouldAssignAnIdAndAName_WhenTheDocumentCarriesNeither()
    {
        using var context = new Context();
        using var client = context.CreateClient();
        var xml = DecisionFixtures.RabattDmn().Replace(" id=\"rabatt\"", "").Replace(" name=\"Rabattstufen\"", "");

        var created = await Read<DecisionDefinitionDto>(await client.PostAsJsonAsync("/decision",
            new SaveDecisionDefinitionRequestDto { Xml = xml }));

        created.DecisionDefinitionId.Should().StartWith("decision_");
        created.Name.Should().Be(created.DecisionDefinitionId);
    }

    // Testzweck: Dieselbe Kennung ein zweites Mal anzulegen ist ein Konflikt — eine neue
    // Fassung entsteht ausschliesslich ueber PUT.
    [Test]
    public async Task Create_ShouldRejectAnAlreadyKnownId()
    {
        using var context = new Context();
        using var client = context.CreateClient();
        await Create(client);

        var response = await client.PostAsJsonAsync("/decision",
            new SaveDecisionDefinitionRequestDto { Xml = DecisionFixtures.RabattDmn() });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // Testzweck: Ein PUT legt die naechste Version an; die juengste Version ist die, die mit
    // dem XML geliefert wird, und die Versionsliste kennt beide Staende.
    [Test]
    public async Task Update_ShouldAppendTheNextVersion()
    {
        using var context = new Context();
        using var client = context.CreateClient();
        await Create(client);

        var second = await Read<DecisionDefinitionDto>(await client.PutAsJsonAsync(
            $"/decision/{DecisionFixtures.RabattDefinitionId}",
            new SaveDecisionDefinitionRequestDto
            {
                Name = "Rabattstufen 2026",
                Xml = DecisionFixtures.RabattDmn(name: "Zweite Fassung")
            }));

        second.Version.Should().Be(2);
        second.Name.Should().Be("Rabattstufen 2026");

        var latest = await Read<DecisionDefinitionDetailDto>(
            await client.GetAsync($"/decision/{DecisionFixtures.RabattDefinitionId}"));
        latest.Version.Should().Be(2);
        latest.Xml.Should().Contain("Zweite Fassung");

        var versions = await Read<DecisionDefinitionVersionDto[]>(
            await client.GetAsync($"/decision/{DecisionFixtures.RabattDefinitionId}/versions"));
        versions.Select(version => version.Version).Should().Equal(1, 2);

        var first = await Read<DecisionDefinitionDetailDto>(
            await client.GetAsync($"/decision/{DecisionFixtures.RabattDefinitionId}/versions/1"));
        first.Xml.Should().Contain("Rabattstufen").And.NotContain("Zweite Fassung");
    }

    // Testzweck: Eine unbekannte Kennung ist ein 404 und kein stillschweigend angelegter
    // Katalogeintrag.
    [Test]
    public async Task Update_ShouldReportAnUnknownDefinitionAsNotFound()
    {
        using var context = new Context();
        using var client = context.CreateClient();

        var response = await client.PutAsJsonAsync("/decision/gibtesnicht",
            new SaveDecisionDefinitionRequestDto { Xml = DecisionFixtures.RabattDmn() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Ein kaputtes DMN wird beim Speichern mit 422 und der Meldung des DMN-Kerns
    // abgewiesen — nicht als 500 und nicht als scheinbar gespeicherte Datei.
    [Test]
    public async Task Create_ShouldRejectABrokenDmnDocumentWithAValidationProblem()
    {
        using var context = new Context();
        using var client = context.CreateClient();

        var response = await client.PostAsJsonAsync("/decision",
            new SaveDecisionDefinitionRequestDto { Xml = DecisionFixtures.KaputtesDmn });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
        (await Read<DecisionDefinitionDto[]>(await client.GetAsync("/decision"))).Should().BeEmpty();
    }

    // Testzweck: Eine Entscheidungsdatei, die kein Workflow benutzt, laesst sich loeschen.
    [Test]
    public async Task Delete_ShouldRemoveAnUnusedDefinition()
    {
        using var context = new Context();
        using var client = context.CreateClient();
        await Create(client);

        var response = await client.DeleteAsync($"/decision/{DecisionFixtures.RabattDefinitionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Read<DecisionDefinitionDto[]>(await client.GetAsync("/decision"))).Should().BeEmpty();
        (await client.GetAsync($"/decision/{DecisionFixtures.RabattDefinitionId}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Ruft ein deployter Workflow eine ihrer Entscheidungen auf, bleibt die Datei
    // stehen und die Antwort nennt den Workflow. Sonst liefe dessen Business-Rule-Task
    // danach in DECISION_NOT_FOUND.
    [Test]
    public async Task Delete_ShouldRefuseWhenADeployedWorkflowCallsTheDecision()
    {
        using var context = new Context();
        using var client = context.CreateClient();
        await Create(client);
        await context.DeployWorkflow("workflow-rabatt", DecisionFixtures.RabattWorkflow());

        var response = await client.DeleteAsync($"/decision/{DecisionFixtures.RabattDefinitionId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult>();
        payload!.ErrorMessage.Should().Contain("workflow-rabatt");
        (await Read<DecisionDefinitionDto[]>(await client.GetAsync("/decision"))).Should().ContainSingle();
    }

    // Testzweck: Eine unbekannte Kennung meldet 404 statt eines stillen Erfolgs.
    [Test]
    public async Task Delete_ShouldReportAnUnknownDefinitionAsNotFound()
    {
        using var context = new Context();
        using var client = context.CreateClient();

        (await client.DeleteAsync("/decision/gibtesnicht")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Der Trockenlauf rechnet die juengste Fassung und nennt Wert und getroffene
    // Regel — ohne Instanz und ohne etwas zu speichern.
    [Test]
    public async Task Evaluate_ShouldReturnTheValueAndTheMatchedRules()
    {
        DecisionFixtures.RequireFeelEngine();
        using var context = new Context();
        using var client = context.CreateClient();
        await Create(client);

        var result = await Read<DecisionEvaluationDto>(await client.PostAsJsonAsync(
            $"/decision/{DecisionFixtures.RabattDefinitionId}/evaluate",
            new { decisionId = DecisionFixtures.RabattDecisionId, variables = new { jahresumsatz = 120000 } }));

        result.DecisionId.Should().Be(DecisionFixtures.RabattDecisionId);
        result.MatchedRules.Should().ContainSingle().Which.Should().Be("rabattGross");
        JsonSerializer.Serialize(result.Value).Should().Contain("gold");
        result.RequiredResults.Should().BeEmpty();
    }

    // Testzweck: Eine Tabelle, die ihrer eigenen Trefferregel widerspricht, beantwortet der
    // Trockenlauf mit 422 und der Meldung des DMN-Kerns statt mit einem Serverfehler.
    [Test]
    public async Task Evaluate_ShouldReportAHitPolicyViolationAsAValidationProblem()
    {
        DecisionFixtures.RequireFeelEngine();
        using var context = new Context();
        using var client = context.CreateClient();
        await Read<DecisionDefinitionDto>(await client.PostAsJsonAsync("/decision",
            new SaveDecisionDefinitionRequestDto { Xml = DecisionFixtures.KonfliktDmn() }));

        var response = await client.PostAsJsonAsync("/decision/konflikt/evaluate",
            new { decisionId = "konfliktstufe", variables = new { jahresumsatz = 120000 } });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    // Testzweck: Der Trockenlauf an einer unbekannten Datei ist ein 404.
    [Test]
    public async Task Evaluate_ShouldReportAnUnknownDefinitionAsNotFound()
    {
        using var context = new Context();
        using var client = context.CreateClient();

        var response = await client.PostAsJsonAsync("/decision/gibtesnicht/evaluate",
            new { decisionId = "irgendeine", variables = new { } });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static async Task<DecisionDefinitionDto> Create(HttpClient client) =>
        await Read<DecisionDefinitionDto>(await client.PostAsJsonAsync("/decision",
            new SaveDecisionDefinitionRequestDto { Xml = DecisionFixtures.RabattDmn() }));

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
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"flowzer-decision-api-{Guid.NewGuid():N}");
        private readonly TestWebApplicationFactory _factory;
        private readonly TransactionalStorage _storage;

        public Context()
        {
            _previousRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _root);
            _storage = new TransactionalStorage();
            _factory = new TestWebApplicationFactory(_storage);
        }

        public HttpClient CreateClient()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Flowzer-UserId", Guid.NewGuid().ToString());
            return client;
        }

        /// <summary>Deployt einen Workflow ueber dieselbe Engine wie die API.</summary>
        public async Task DeployWorkflow(string metaDefinitionId, string xml)
        {
            var engine = _factory.Services.GetRequiredService<BpmnBusinessLogic>();
            var definition = new BpmnDefinition
            {
                Id = Guid.NewGuid(),
                DefinitionId = metaDefinitionId,
                Version = new Model.Version(1, 0),
                Hash = "test",
                SavedByUser = Guid.NewGuid(),
                SavedOn = DateTime.UtcNow,
                IsActive = false
            };

            await _storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            {
                DefinitionId = metaDefinitionId,
                Name = metaDefinitionId
            });
            await _storage.DefinitionStorage.StoreDefinition(definition);
            await _storage.DefinitionStorage.StoreBinary(definition.Id, xml);
            await engine.DeployDefinition(definition);
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
