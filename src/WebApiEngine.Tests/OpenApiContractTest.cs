using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace WebApiEngine.Tests;

/// <summary>
/// Haelt den API-Vertrag fest. Der Schnappschuss unter <c>docs/openapi.json</c> ist die
/// verabredete Aussenansicht; weicht die erzeugte Beschreibung davon ab, war entweder die
/// Aenderung gewollt und der Schnappschuss gehoert aktualisiert, oder sie war ein Versehen.
/// Ohne diesen Vergleich faellt eine stille Vertragsaenderung erst beim Client auf.
/// </summary>
[NonParallelizable]
public class OpenApiContractTest
{
    private const string SnapshotPath = "docs/openapi.json";

    // Testzweck: Die erzeugte OpenAPI-Beschreibung entspricht dem eingecheckten Schnappschuss.
    [Test]
    public async Task GeneratedOpenApiDocument_ShouldMatchTheCommittedSnapshot()
    {
        var current = await FetchDocument();
        var snapshotFile = ResolveSnapshotFile();

        // Ein gewollter Vertragswechsel wird hier neu festgeschrieben, statt den Test von Hand
        // nachzupflegen: scripts/ci/update-openapi-snapshot.sh setzt diese Variable.
        if (Environment.GetEnvironmentVariable("FLOWZER_UPDATE_OPENAPI_SNAPSHOT") == "1")
        {
            snapshotFile.Directory!.Create();
            await File.WriteAllTextAsync(snapshotFile.FullName, Normalize(current) + Environment.NewLine);
            Assert.Pass($"Schnappschuss aktualisiert: {snapshotFile.FullName}");
        }

        snapshotFile.Exists.Should().BeTrue(
            $"der Schnappschuss {SnapshotPath} gehoert ins Repository; erzeugen mit scripts/ci/update-openapi-snapshot.sh");

        var expected = Normalize(await File.ReadAllTextAsync(snapshotFile.FullName));
        var actual = Normalize(current);

        actual.Should().Be(expected,
            "die API-Beschreibung hat sich geaendert. War das gewollt, den Schnappschuss mit "
            + "scripts/ci/update-openapi-snapshot.sh neu erzeugen und mit einchecken.");
    }

    // Testzweck: Alle Fachendpunkte liefern denselben Umschlag. Ein Client soll Erfolg und
    // Fehler nicht je Endpunkt anders lesen muessen.
    [Test]
    public async Task AllJsonEndpoints_ShouldAnswerWithTheCommonEnvelope()
    {
        using var document = JsonDocument.Parse(await FetchDocument());

        // Ausnahmen mit Begruendung: XML liefert ein Dokument, Health ist ein Probe-Endpunkt
        // fuer Orchestratoren mit eigenem, schlankem Vertrag.
        string[] exceptions = [
            "/bff/session",
            "/bff/csrf",
            "/definition/xml/{guid}",
            "/health",
            "/health/ready"
        ];

        var offenders = new List<string>();
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (exceptions.Contains(path.Name))
            {
                continue;
            }

            foreach (var operation in path.Value.EnumerateObject())
            {
                if (!operation.Value.TryGetProperty("responses", out var responses)
                    || !responses.TryGetProperty("200", out var ok)
                    || !ok.TryGetProperty("content", out var content)
                    || !content.TryGetProperty("application/json", out var json)
                    || !json.TryGetProperty("schema", out var schema))
                {
                    continue;
                }

                // Swashbuckle benennt generische Typen als "<T>ApiStatusResult"; der Umschlag
                // ohne Nutzlast heisst schlicht "ApiStatusResult".
                var reference = ResolveSchemaName(schema);
                if (reference is not null && !reference.EndsWith("ApiStatusResult", StringComparison.Ordinal))
                {
                    offenders.Add($"{operation.Name.ToUpperInvariant()} {path.Name} -> {reference}");
                }
            }
        }

        offenders.Should().BeEmpty();
    }

    // Testzweck: Der Browservertrag des BFF bleibt explizit und darf nicht versehentlich in
    // den Legacy-ApiStatusResult-Umschlag oder in unbeschriebene Statuscodes zurückfallen.
    [Test]
    public async Task BffEndpoints_ShouldExposeTheDocumentedResponses()
    {
        using var document = JsonDocument.Parse(await FetchDocument());
        var paths = document.RootElement.GetProperty("paths");

        var login = GetOperation(paths, "/bff/login", "get");
        GetResponse(login, "302").Should().NotBeNull();
        GetResponse(login, "404").Should().NotBeNull();
        GetProblemResponse(login, "400").Should().Be("#/components/schemas/ProblemDetails");

        var session = GetOperation(paths, "/bff/session", "get");
        GetResponseSchema(session, "200").Should().Be("#/components/schemas/BffSessionDto");
        GetResponse(session, "401").Should().NotBeNull();
        GetResponse(session, "404").Should().NotBeNull();

        var csrf = GetOperation(paths, "/bff/csrf", "get");
        GetResponseSchema(csrf, "200").Should().Be("#/components/schemas/BffCsrfDto");
        GetResponse(csrf, "401").Should().NotBeNull();
        GetResponse(csrf, "404").Should().NotBeNull();

        var logout = GetOperation(paths, "/bff/logout", "post");
        GetResponse(logout, "204").Should().NotBeNull();
        GetProblemResponse(logout, "400").Should().Be("#/components/schemas/ProblemDetails");
        GetResponse(logout, "401").Should().NotBeNull();
        GetResponse(logout, "404").Should().NotBeNull();
    }

    // Testzweck: Status und manueller Keycloak-Abgleich besitzen einen expliziten
    // Operatorvertrag; Fehler verwenden Problem Details und der Status enthaelt keine Secrets.
    [Test]
    public async Task IdentityDirectoryEndpoints_ShouldExposeOnlyTheOperationalContract()
    {
        using var document = JsonDocument.Parse(await FetchDocument());
        var root = document.RootElement;
        var paths = root.GetProperty("paths");

        var status = GetOperation(paths, "/identity-directory/status", "get");
        GetResponseSchema(status, "200").Should().Be("#/components/schemas/IdentityDirectoryStatusDtoApiStatusResult");
        GetProblemResponse(status, "503").Should().Be("#/components/schemas/ProblemDetails");

        var synchronization = GetOperation(paths, "/identity-directory/sync", "post");
        GetResponseSchema(synchronization, "202").Should().Be("#/components/schemas/IdentityDirectoryStatusDtoApiStatusResult");
        GetProblemResponse(synchronization, "404").Should().Be("#/components/schemas/ProblemDetails");
        GetProblemResponse(synchronization, "409").Should().Be("#/components/schemas/ProblemDetails");

        var statusSchema = root.GetProperty("components").GetProperty("schemas")
            .GetProperty("IdentityDirectoryStatusDto").GetProperty("properties");
        statusSchema.TryGetProperty("issuer", out _).Should().BeFalse();
        statusSchema.TryGetProperty("serverUrl", out _).Should().BeFalse();
        statusSchema.TryGetProperty("clientSecret", out _).Should().BeFalse();
    }

    // Testzweck: Der hostneutrale Task-Deep-Link bleibt als datensparsamer Umschlag
    // beschrieben und verbirgt fremde wie unbekannte Aufgaben mit Problem Details.
    [Test]
    public async Task UserTaskDetailEndpoint_ShouldExposeTheEmbeddingContract()
    {
        using var document = JsonDocument.Parse(await FetchDocument());
        var detail = GetOperation(
            document.RootElement.GetProperty("paths"), "/UserTask/{userTaskId}", "get");

        GetResponseSchema(detail, "200").Should()
            .Be("#/components/schemas/ExtendedUserTaskSubscriptionDtoApiStatusResult");
        GetProblemResponse(detail, "404").Should().Be("#/components/schemas/ProblemDetails");
    }

    private static string? ResolveSchemaName(JsonElement schema)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            return reference.GetString()?.Split('/').Last();
        }

        // Arrays und Nullable-Wrapper zeigen ueber allOf/items auf das eigentliche Schema.
        if (schema.TryGetProperty("allOf", out var allOf) && allOf.GetArrayLength() > 0)
        {
            return ResolveSchemaName(allOf[0]);
        }

        return null;
    }

    private static JsonElement GetOperation(JsonElement paths, string path, string method)
    {
        paths.TryGetProperty(path, out var pathItem).Should().BeTrue($"der BFF-Endpunkt {path} muss beschrieben sein");
        pathItem.TryGetProperty(method, out var operation).Should().BeTrue($"{method.ToUpperInvariant()} {path} muss beschrieben sein");
        return operation;
    }

    private static JsonElement? GetResponse(JsonElement operation, string statusCode)
    {
        var responses = operation.GetProperty("responses");
        return responses.TryGetProperty(statusCode, out var response) ? response : null;
    }

    private static string? GetResponseSchema(JsonElement operation, string statusCode)
    {
        var response = GetResponse(operation, statusCode);
        if (response is null || !response.Value.TryGetProperty("content", out var content))
        {
            return null;
        }

        foreach (var mediaType in content.EnumerateObject())
        {
            if (mediaType.Value.TryGetProperty("schema", out var schema)
                && schema.TryGetProperty("$ref", out var reference))
            {
                return reference.GetString();
            }
        }

        return null;
    }

    private static string? GetProblemResponse(JsonElement operation, string statusCode)
    {
        var response = GetResponse(operation, statusCode);
        if (response is null || !response.Value.TryGetProperty("content", out var content)
            || !content.TryGetProperty("application/problem+json", out var mediaType)
            || !mediaType.TryGetProperty("schema", out var schema)
            || !schema.TryGetProperty("$ref", out var reference))
        {
            return null;
        }

        return reference.GetString();
    }

    private static async Task<string> FetchDocument()
    {
        await using var factory = new OpenApiFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static FileInfo ResolveSnapshotFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "core-engine.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("der Test laeuft innerhalb des Repositories");
        return new FileInfo(Path.Combine(directory!.FullName, SnapshotPath));
    }

    /// <summary>Zeilenenden und abschliessende Leerzeichen sollen den Vergleich nicht stoeren.</summary>
    private static string Normalize(string document) =>
        JsonSerializer.Serialize(
            JsonSerializer.Deserialize<JsonElement>(document),
            new JsonSerializerOptions { WriteIndented = true });

    private sealed class OpenApiFactory : WebApplicationFactory<Program>
    {
        private readonly string? _previousStorageRoot;
        private readonly string _storageRoot;

        public OpenApiFactory()
        {
            _previousStorageRoot = Environment.GetEnvironmentVariable(FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName);
            _storageRoot = Path.Combine(Path.GetTempPath(), "flowzer-openapi-test", Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName, _storageRoot);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Die Beschreibung wird nur im Development-Modus ausgeliefert.
            builder.UseSetting(WebHostDefaults.EnvironmentKey, "Development");
            builder.UseSetting("TimerScheduler:Enabled", "false");
            builder.UseSetting("RateLimiting:Enabled", "false");
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
            {
                return;
            }

            Environment.SetEnvironmentVariable(FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName, _previousStorageRoot);
            if (Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }
    }
}
