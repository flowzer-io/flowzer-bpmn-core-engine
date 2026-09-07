using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Ordner und die daran haengende Delegation, end-to-end ueber HTTP.
///
/// Der eigentliche Zweck der Funktion steht hier auf dem Pruefstand: dass jemand ohne die
/// Anwendungsrolle fuers Modellieren in *seinem* Ordner arbeiten kann und nur dort.
/// </summary>
[NonParallelizable]
public class FolderControllerIntegrationTest
{
    private const string Issuer = "https://issuer.test/realms/flowzer";
    private const string Audience = "flowzer-api";
    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("flowzer-integration-test-signing-key-with-32-bytes+"));

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // Testzweck: Ordner auf oberster Ebene gehoeren niemandem im Besonderen und bleiben
    // deshalb der Anwendungsrolle vorbehalten. Ohne diese Grenze koennte jeder Zugelassene
    // den Katalog um beliebige Wurzeln erweitern.
    [Test]
    public async Task CreatingARootFolder_ShouldRequireTheModelerRole()
    {
        await using var factory = new FolderTestFactory();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken());
        var ohneRolle = await client.PostAsJsonAsync("/folder", new { name = "Finanzen" });

        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(roles: ["modeler"]));
        var mitRolle = await client.PostAsJsonAsync("/folder", new { name = "Finanzen" });

        ohneRolle.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ohneRolle.Headers.GetValues("X-Flowzer-Access-Denied").Should().ContainSingle().Which.Should().Be("capability");
        mitRolle.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Das ist der Kern der Delegation — wer die Fachverantwortung fuer einen Ordner
    // bekommt, kann darin arbeiten, ohne die Anwendungsrolle fuers Modellieren zu tragen. Und
    // ausserhalb dieses Ordners weiterhin nicht.
    [Test]
    public async Task AStewardOfAFolder_ShouldWorkInsideItWithoutTheModelerRole()
    {
        await using var factory = new FolderTestFactory();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(roles: ["modeler"]));
        var finanzen = await CreateFolder(client, "Finanzen");
        await Delegate(client, finanzen.Id, ("group", "/abteilungen/einkauf", "steward"));

        // Ab hier ohne jede Anwendungsrolle, nur mit der Gruppenmitgliedschaft.
        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(groups: ["/abteilungen/einkauf"]));

        var unterordner = await client.PostAsJsonAsync("/folder", new { name = "Beschaffung", parentId = finanzen.Id });
        var imOrdner = await client.PostAsync($"/definition/new?name=Beschaffungsantrag&folderId={finanzen.Id}", content: null);
        var aufWurzelebene = await client.PostAsync("/definition/new?name=Woanders", content: null);
        var fremderOrdner = await client.PostAsJsonAsync("/folder", new { name = "Personal" });

        unterordner.StatusCode.Should().Be(HttpStatusCode.OK, "die Fachverantwortung schliesst Unterordner ein");
        imOrdner.StatusCode.Should().Be(HttpStatusCode.OK, "im eigenen Ordner darf angelegt werden");
        aufWurzelebene.StatusCode.Should().Be(HttpStatusCode.Forbidden, "die oberste Ebene bleibt der Anwendungsrolle vorbehalten");
        fremderOrdner.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // Testzweck: Bearbeiten und Delegieren sind zwei verschiedene Rechte. Wer nur bearbeiten
    // darf, darf nicht bestimmen, wer sonst noch darf — sonst waere die Unterscheidung
    // zwischen den beiden Rollen wirkungslos.
    [Test]
    public async Task AnEditor_ShouldEditButNotDelegate()
    {
        await using var factory = new FolderTestFactory();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(roles: ["modeler"]));
        var finanzen = await CreateFolder(client, "Finanzen");
        await Delegate(client, finanzen.Id, ("user", "bert", "editor"));

        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(userName: "bert"));

        var anlegen = await client.PostAsync($"/definition/new?name=Rechnungsfreigabe&folderId={finanzen.Id}", content: null);
        var delegieren = await client.PutAsJsonAsync($"/folder/{finanzen.Id}/assignments",
            new { assignments = new[] { new { subjectKind = "user", subject = "bert", role = "steward" } } });
        var umbenennen = await client.PutAsJsonAsync($"/folder/{finanzen.Id}", new { name = "Finanzwesen" });

        anlegen.StatusCode.Should().Be(HttpStatusCode.OK);
        delegieren.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        umbenennen.StatusCode.Should().Be(HttpStatusCode.Forbidden, "der Ordner selbst gehoert der Fachverantwortung");
    }

    // Testzweck: Ein Recht gilt auch fuer Unterordner, und die Oberflaeche muss sehen koennen,
    // woher es kommt. Ohne die geerbten Zeilen wirkte eine Berechtigung, die man nirgends
    // findet, wie ein Fehler.
    [Test]
    public async Task Folders_ShouldReportInheritedAssignmentsAndTheOwnRights()
    {
        await using var factory = new FolderTestFactory();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(roles: ["modeler"]));
        var finanzen = await CreateFolder(client, "Finanzen");
        await Delegate(client, finanzen.Id, ("user", "anna", "steward"));
        var beschaffung = await CreateFolder(client, "Beschaffung", finanzen.Id);

        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(userName: "anna"));
        var folders = await ReadResult<WorkflowFolderDto[]>(await client.GetAsync("/folder"));

        var unterordner = folders!.Single(folder => folder.Id == beschaffung.Id);
        unterordner.MayEdit.Should().BeTrue();
        unterordner.MayDelegate.Should().BeTrue();
        unterordner.Assignments.Should().BeEmpty("die Zuweisung haengt am uebergeordneten Ordner");
        unterordner.InheritedAssignments.Should().ContainSingle()
            .Which.InheritedFromName.Should().Be("Finanzen");
    }

    // Testzweck: Ein Ordner mit Inhalt darf nicht mit einem Klick verschwinden — daran haengt
    // Arbeit, die die Ablage beim Loeschen gar nicht sieht.
    [Test]
    public async Task DeletingAFolder_ShouldRequireItToBeEmpty()
    {
        await using var factory = new FolderTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(roles: ["modeler"]));

        var finanzen = await CreateFolder(client, "Finanzen");
        await client.PostAsync($"/definition/new?name=Beschaffungsantrag&folderId={finanzen.Id}", content: null);

        var mitInhalt = await client.DeleteAsync($"/folder/{finanzen.Id}");

        var leer = await CreateFolder(client, "Leer");
        var ohneInhalt = await client.DeleteAsync($"/folder/{leer.Id}");

        mitInhalt.StatusCode.Should().Be(HttpStatusCode.Conflict);
        ohneInhalt.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Ein Ordner in seinem eigenen Ast waere vom Baum abgeschnitten und aus der
    // Oberflaeche nicht mehr erreichbar.
    [Test]
    public async Task MovingAFolderIntoItsOwnSubtree_ShouldBeRejected()
    {
        await using var factory = new FolderTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(roles: ["modeler"]));

        var finanzen = await CreateFolder(client, "Finanzen");
        var beschaffung = await CreateFolder(client, "Beschaffung", finanzen.Id);

        var response = await client.PutAsJsonAsync($"/folder/{finanzen.Id}",
            new { name = "Finanzen", parentId = beschaffung.Id });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // Testzweck: Verschieben braucht die Berechtigung an beiden Enden. Nur das Ziel zu pruefen
    // erlaubte es, fremde Workflows in den eigenen Ordner zu holen.
    [Test]
    public async Task MovingAWorkflow_ShouldRequirePermissionOnBothSides()
    {
        await using var factory = new FolderTestFactory();
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(roles: ["modeler"]));
        var fremd = await CreateFolder(client, "Personal");
        var eigen = await CreateFolder(client, "Finanzen");
        await Delegate(client, eigen.Id, ("user", "anna", "editor"));

        var fremderWorkflow = await ReadResult<BpmnMetaDefinitionDto>(
            await client.PostAsync($"/definition/new?name=Onboarding&folderId={fremd.Id}", content: null));
        var eigenerWorkflow = await ReadResult<BpmnMetaDefinitionDto>(
            await client.PostAsync($"/definition/new?name=Beschaffung&folderId={eigen.Id}", content: null));

        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(userName: "anna"));

        var holen = await client.PutAsync($"/definition/meta/{fremderWorkflow!.DefinitionId}/folder?folderId={eigen.Id}", content: null);
        var wegschieben = await client.PutAsync($"/definition/meta/{eigenerWorkflow!.DefinitionId}/folder?folderId={fremd.Id}", content: null);
        var loeschen = await client.DeleteAsync($"/definition/meta/{fremderWorkflow.DefinitionId}");

        holen.StatusCode.Should().Be(HttpStatusCode.Forbidden, "die Herkunft gehoert jemand anderem");
        wegschieben.StatusCode.Should().Be(HttpStatusCode.Forbidden, "das Ziel gehoert jemand anderem");
        loeschen.StatusCode.Should().Be(HttpStatusCode.Forbidden, "ein fremder Workflow bleibt fremd");
    }

    // Testzweck: Der Katalog nennt den Ordner jedes Workflows — ohne ihn koennte die
    // Oberflaeche die Kacheln nicht auf die Ordner verteilen.
    [Test]
    public async Task TheCatalogue_ShouldCarryTheFolderOfEachWorkflow()
    {
        await using var factory = new FolderTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(roles: ["modeler"]));

        var finanzen = await CreateFolder(client, "Finanzen");
        await client.PostAsync($"/definition/new?name=Beschaffungsantrag&folderId={finanzen.Id}", content: null);
        await client.PostAsync("/definition/new?name=Ohne%20Ordner", content: null);

        var catalogue = await ReadResult<ExtendedBpmnMetaDefinitionDto[]>(await client.GetAsync("/definition/meta"));

        catalogue.Should().NotBeNull();
        catalogue!.Single(entry => entry.Name == "Beschaffungsantrag").FolderId.Should().Be(finanzen.Id);
        catalogue.Single(entry => entry.Name == "Ohne Ordner").FolderId.Should().BeNull();
    }

    // Testzweck: Das Umbenennen eines Workflows darf ihn nicht nebenbei aus seinem Ordner
    // holen. Frueher fehlte das Feld im Rumpf und das Speichern haette es geleert.
    [Test]
    public async Task RenamingAWorkflow_ShouldKeepItsFolder()
    {
        await using var factory = new FolderTestFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(CreateToken(roles: ["modeler"]));

        var finanzen = await CreateFolder(client, "Finanzen");
        var workflow = await ReadResult<BpmnMetaDefinitionDto>(
            await client.PostAsync($"/definition/new?name=Beschaffung&folderId={finanzen.Id}", content: null));

        await client.PutAsJsonAsync("/definition/meta",
            new { definitionId = workflow!.DefinitionId, name = "Beschaffung neu", folderId = (Guid?)null });

        var catalogue = await ReadResult<ExtendedBpmnMetaDefinitionDto[]>(await client.GetAsync("/definition/meta"));

        catalogue!.Single().FolderId.Should().Be(finanzen.Id);
    }

    private static async Task<WorkflowFolderDto> CreateFolder(HttpClient client, string name, Guid? parentId = null)
    {
        var response = await client.PostAsJsonAsync("/folder", new { name, parentId });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "der Ordner {0} wird als Testvoraussetzung gebraucht", name);
        return (await ReadResult<WorkflowFolderDto>(response))!;
    }

    private static async Task Delegate(HttpClient client, Guid folderId, params (string Kind, string Subject, string Role)[] assignments)
    {
        var response = await client.PutAsJsonAsync($"/folder/{folderId}/assignments", new
        {
            assignments = assignments.Select(entry => new { subjectKind = entry.Kind, subject = entry.Subject, role = entry.Role })
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK, "die Delegation ist die Testvoraussetzung");
    }

    private static async Task<T?> ReadResult<T>(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<T>>(Json);
        return payload is null ? default : payload.Result;
    }

    private static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

    private static string CreateToken(string[]? roles = null, string[]? groups = null, string? userName = null)
    {
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()) };
        claims.AddRange((roles ?? []).Select(role => new Claim("roles", role)));
        claims.AddRange((groups ?? []).Select(group => new Claim("groups", group)));
        if (userName is not null)
        {
            claims.Add(new Claim("preferred_username", userName));
        }

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
            Subject = new ClaimsIdentity(claims)
        });
    }

    /// <summary>
    /// Wie die Rollen-Tests: echte Policies und echte Dateiablage, nur die OIDC-Discovery ist
    /// durch statische Metadaten mit dem Testschluessel ersetzt.
    /// </summary>
    private sealed class FolderTestFactory : WebApplicationFactory<Program>
    {
        private readonly string? _previousStorageRoot;
        private readonly string _storageRoot;

        public FolderTestFactory()
        {
            _previousStorageRoot = Environment.GetEnvironmentVariable(FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName);
            _storageRoot = Path.Combine(Path.GetTempPath(), "flowzer-folder-test", Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName, _storageRoot);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(WebHostDefaults.EnvironmentKey, "Production");
            builder.UseSetting("TimerScheduler:Enabled", "false");
            builder.UseSetting("RateLimiting:Enabled", "false");
            builder.UseSetting("Authentication:Scheme", "JwtBearer");
            builder.UseSetting("Authentication:JwtBearer:Authority", Issuer);
            builder.UseSetting("Authentication:JwtBearer:Audience", Audience);
            builder.UseSetting("Authentication:JwtBearer:Roles:Modeler", "modeler");
            builder.UseSetting("Authentication:JwtBearer:Roles:Operator", "operator");

            builder.ConfigureServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                        new OpenIdConnectConfiguration
                        {
                            Issuer = Issuer,
                            SigningKeys = { SigningKey }
                        });
                });
            });
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
