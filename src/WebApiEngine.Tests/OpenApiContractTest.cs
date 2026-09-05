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
        string[] exceptions = ["/definition/xml/{guid}", "/health", "/health/ready"];

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
