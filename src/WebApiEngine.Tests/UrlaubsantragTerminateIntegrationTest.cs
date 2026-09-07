using System.Dynamic;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FilesystemStorageSystem;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Das Beispiel „Urlaubsantrag" im Original, über die API durchgespielt. Es prüft die
/// Zusage, die das Modell gibt: Sagt eine der drei gleichzeitigen Prüfungen „nein", ist
/// der Vorgang zu Ende — und niemand hat noch etwas zu tun. Ein Testmodell würde hier
/// nicht genügen: Der Abbruch soll genau in dem Beispiel greifen, das ausgeliefert wird.
/// </summary>
[NonParallelizable]
public class UrlaubsantragTerminateIntegrationTest
{
    private const string DefinitionId = "flowzer-urlaubsantrag";

    /// <summary>Dieselbe Entwicklerkennung, die auch der Demo-Worker des Beispiels benutzt.</summary>
    private static readonly Guid TestUserId = Guid.Parse("D266F2B6-E96E-4D4A-9C20-C8E541394DF0");

    // Testzweck: Ein „nein" in der ersten Pruefung beendet den ganzen Vorgang, sobald die
    // Ablehnung hinausgegangen ist. Ohne das abbrechende Ende blieben die fachliche
    // Entscheidung und der Auftrag an die Vertretungspruefung offen — Arbeit an einem
    // Antrag, der bereits abgelehnt ist.
    [Test]
    public async Task RejectingOneCheck_ShouldTerminateTheInstance_AndClearTheOtherTasksAndJobs()
    {
        using var context = new UrlaubsantragContext();
        using var client = context.CreateClient();
        await context.SeedExample(client);

        var instanceId = await context.StartInstance(client);

        // Ausgangslage: zwei Aufgaben für Menschen, ein Auftrag für einen Worker.
        var openTasks = await context.OpenUserTasks(client, instanceId);
        openTasks.Select(task => task.Name)
            .Should().BeEquivalentTo("Urlaubstage prüfen", "Urlaub fachlich entscheiden");
        (await context.AllJobs(client, instanceId)).Select(job => job.Type)
            .Should().BeEquivalentTo("urlaub-vertretung-pruefen");

        await context.CompleteUserTask(
            client,
            openTasks.Single(task => task.Name == "Urlaubstage prüfen"),
            new Dictionary<string, object?>
            {
                ["pruefungBestanden"] = "nein",
                ["pruefwert"] = "3",
                ["pruefkommentar"] = "Nur noch 3 Resttage im Konto."
            });

        // „Ablehnung mitteilen" ist ein Service-Task: Das abbrechende Ende liegt dahinter,
        // nicht davor. Bis die Nachricht hinaus ist, laeuft der Vorgang also noch — und die
        // beiden anderen Pruefungen stehen weiter offen. Das ist der Punkt, an dem die
        // Ablehnung ein zweites Mal ausgeloest werden koennte (siehe README des Beispiels).
        (await context.GetInstance(client, instanceId)).State.Should().Be(ProcessInstanceStateDto.Waiting);
        (await context.OpenUserTasks(client, instanceId)).Select(task => task.Name)
            .Should().BeEquivalentTo("Urlaub fachlich entscheiden");

        var ablehnung = (await context.FetchJobs(client, "urlaub-ablehnung-mitteilen"))
            .Should().ContainSingle().Subject;

        // Der Grund muss ankommen, obwohl erst eine der drei Entscheidungen gefallen ist.
        // Die Werte reisen als JSON; verglichen wird deshalb ihre Textform.
        ablehnung.Variables["tageAusreichend"]?.ToString().Should().Be("nein");
        ablehnung.Variables["lohnbuchhaltungKommentar"]?.ToString().Should().Be("Nur noch 3 Resttage im Konto.");

        await context.CompleteJob(client, ablehnung.Id, new Dictionary<string, object?>
        {
            ["ablehnungsgrund"] = "Nur noch 3 Resttage im Konto."
        });

        (await context.GetInstance(client, instanceId)).State.Should().Be(ProcessInstanceStateDto.Terminated);

        // Die fachliche Entscheidung ist weg, obwohl niemand sie angefasst hat.
        (await context.OpenUserTasks(client, instanceId)).Should().BeEmpty();

        // Und der Auftrag an die Vertretungspruefung ebenso — weder in der Betriebssicht
        // noch fuer einen Worker.
        (await context.AllJobs(client, instanceId)).Should().BeEmpty();
        (await context.FetchJobs(client, "urlaub-vertretung-pruefen")).Should().BeEmpty();
    }

    // Testzweck: Sagen alle drei Prüfungen „ja", läuft der Vorgang normal weiter. Sonst wäre
    // nicht belegt, dass der Abbruch nur am „nein" hängt und nicht am Umbau der Zweige.
    [Test]
    public async Task ApprovingAllThreeChecks_ShouldOpenTheApprovalTasksAndJobs()
    {
        using var context = new UrlaubsantragContext();
        using var client = context.CreateClient();
        await context.SeedExample(client);

        var instanceId = await context.StartInstance(client);
        var openTasks = await context.OpenUserTasks(client, instanceId);

        await context.CompleteUserTask(
            client,
            openTasks.Single(task => task.Name == "Urlaubstage prüfen"),
            new Dictionary<string, object?>
            {
                ["pruefungBestanden"] = "ja",
                ["pruefwert"] = "22",
                ["pruefkommentar"] = string.Empty
            });

        await context.CompleteUserTask(
            client,
            openTasks.Single(task => task.Name == "Urlaub fachlich entscheiden"),
            new Dictionary<string, object?>
            {
                ["entscheidung"] = "freigegeben",
                ["begruendung"] = string.Empty
            });

        // Der Service-Task geht den Weg eines echten Workers: holen und zurückmelden.
        var vertretung = (await context.FetchJobs(client, "urlaub-vertretung-pruefen")).Should().ContainSingle().Subject;
        await context.CompleteJob(client, vertretung.Id, new Dictionary<string, object?>
        {
            ["vertretungFrei"] = "ja"
        });

        var instance = await context.GetInstance(client, instanceId);
        instance.State.Should().Be(ProcessInstanceStateDto.Waiting);

        (await context.OpenUserTasks(client, instanceId)).Select(task => task.Name)
            .Should().BeEquivalentTo("Urlaub in LexOffice eintragen");
        (await context.AllJobs(client, instanceId)).Select(job => job.Type)
            .Should().BeEquivalentTo("urlaub-genehmigung-mitteilen", "urlaub-tickytask-eintragen");
    }

    /// <summary>
    /// Spielt das Beispiel in eine leere dateibasierte Ablage ein und redet mit der API so,
    /// wie es <c>examples/urlaubsantrag/import.mjs</c> und der Demo-Worker tun.
    /// </summary>
    private sealed class UrlaubsantragContext : IDisposable
    {
        private readonly string? _previousStorageRoot;
        private readonly string _storageRoot;
        private readonly WebApplicationFactory<Program> _factory;

        /// <summary>Formularname = Form-Key im Modell → Datei neben der Testanwendung.</summary>
        private static readonly (string Name, string File)[] Forms =
        [
            ("Urlaubsantrag", "urlaubsantrag.json"),
            ("Prüfung", "pruefung.json"),
            ("Freigabe", "freigabe.json"),
            ("Erledigung bestätigen", "erledigung.json"),
            ("Kenntnisnahme", "kenntnisnahme.json")
        ];

        public UrlaubsantragContext()
        {
            _previousStorageRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            _storageRoot = Path.Combine(
                Path.GetTempPath(),
                "flowzer-urlaubsantrag-terminate-test",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_storageRoot);
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _storageRoot);

            _factory = new UrlaubsantragFactory();
        }

        /// <summary>
        /// Meldet sich wie die Beispielskripte mit der technischen Kennung an. Ohne sie gilt
        /// der Systembenutzer als „nicht aufgeloest", und Deployen waere nicht erlaubt.
        /// </summary>
        public HttpClient CreateClient()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Flowzer-UserId", TestUserId.ToString());
            return client;
        }

        public async Task SeedExample(HttpClient client)
        {
            foreach (var (name, file) in Forms)
            {
                var formId = Guid.NewGuid();
                await Post(client, $"/form/meta/{formId}", new FormMetaDataDto { FormId = formId, Name = name });
                await Post(client, "/form", new FormDto
                {
                    FormId = formId,
                    FormData = await File.ReadAllTextAsync(Path.Combine("examples", "formulare", file))
                });
            }

            await Post(client, "/definition/meta", new BpmnMetaDefinitionDto
            {
                DefinitionId = DefinitionId,
                Name = "Urlaubsantrag"
            });

            var xml = await File.ReadAllTextAsync(Path.Combine("examples", "urlaubsantrag.bpmn"));
            var deployed = await client.PostAsync(
                "/definition/deploy",
                new StringContent(xml, Encoding.UTF8, "application/xml"));
            await EnsureSuccessful(deployed, "/definition/deploy");
        }

        public async Task<Guid> StartInstance(HttpClient client)
        {
            // Bewusst als roher JSON-Rumpf: Ein anonymes Objekt wuerde der Testserializer
            // camelCase schreiben und damit die Namen der Prozessvariablen aendern.
            const string variables = """
                                     {"variables":{
                                       "mitarbeiter":"Christian Maaß",
                                       "art":"erholung",
                                       "von":"2026-10-05",
                                       "bis":"2026-10-16",
                                       "arbeitstage":10,
                                       "vertretung":"Melli",
                                       "bemerkung":"",
                                       "vorgang":"Christian Maaß · Erholungsurlaub · 05.10.2026 bis 16.10.2026"
                                     }}
                                     """;

            var response = await client.PostAsync(
                $"/definition/meta/{DefinitionId}/instance",
                new StringContent(variables, Encoding.UTF8, "application/json"));
            await EnsureSuccessful(response, "instance start");

            var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>();
            return payload!.Result!.InstanceId;
        }

        public async Task<ProcessInstanceInfoDto> GetInstance(HttpClient client, Guid instanceId)
        {
            var response = await client.GetAsync($"/instance/{instanceId}");
            await EnsureSuccessful(response, $"/instance/{instanceId}");
            var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>();
            return payload!.Result!;
        }

        public async Task<ExtendedUserTaskSubscriptionDto[]> OpenUserTasks(HttpClient client, Guid instanceId)
        {
            var response = await client.GetAsync("/usertask");
            await EnsureSuccessful(response, "/usertask");
            var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>>();
            return payload!.Result!.Where(task => task.ProcessInstanceId == instanceId).ToArray();
        }

        public async Task CompleteUserTask(
            HttpClient client,
            ExtendedUserTaskSubscriptionDto task,
            IDictionary<string, object?> data)
        {
            var response = await client.PostAsJsonAsync("/usertask", new UserTaskResultDto
            {
                FlowNodeId = task.Token.CurrentFlowNodeId!,
                TokenId = task.Token.Id,
                ProcessInstanceId = task.ProcessInstanceId,
                Data = ToExpando(data)
            });
            await EnsureSuccessful(response, $"/usertask ({task.Name})");
        }

        /// <summary>
        /// Alle Auftraege der Instanz aus der Betriebssicht — auch gesperrte. Ueber
        /// <c>/job/fetch</c> allein liesse sich „der Auftrag ist weg" nicht belegen: Ein
        /// gesperrter Auftrag wird dort ebenfalls nicht ausgeliefert.
        /// </summary>
        public async Task<ServiceTaskJobDto[]> AllJobs(HttpClient client, Guid instanceId)
        {
            var response = await client.GetAsync("/job");
            await EnsureSuccessful(response, "/job");
            var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ServiceTaskJobDto[]>>();
            return payload!.Result!.Where(job => job.ProcessInstanceId == instanceId).ToArray();
        }

        public async Task<ServiceTaskJobDto[]> FetchJobs(HttpClient client, string type)
        {
            var response = await client.PostAsJsonAsync("/job/fetch", new FetchJobsRequestDto
            {
                Type = type,
                WorkerId = "urlaub-test-worker",
                MaxJobs = 10,
                LockSeconds = 60
            });
            await EnsureSuccessful(response, "/job/fetch");
            var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ServiceTaskJobDto[]>>();
            return payload!.Result!;
        }

        public async Task CompleteJob(HttpClient client, Guid jobId, IDictionary<string, object?> variables)
        {
            var response = await client.PostAsJsonAsync($"/job/{jobId}/complete", new CompleteJobRequestDto
            {
                WorkerId = "urlaub-test-worker",
                Variables = ToExpando(variables)
            });
            await EnsureSuccessful(response, $"/job/{jobId}/complete");
        }

        private static ExpandoObject ToExpando(IDictionary<string, object?> values)
        {
            var expando = new ExpandoObject();
            var target = (IDictionary<string, object?>)expando;
            foreach (var (key, value) in values)
            {
                target[key] = value;
            }

            return expando;
        }

        private static async Task Post<T>(HttpClient client, string path, T body)
        {
            var response = await client.PostAsJsonAsync(path, body);
            await EnsureSuccessful(response, path);
        }

        /// <summary>
        /// Ein Fehlschlag beim Einspielen wuerde sonst erst viel spaeter als „keine Aufgabe
        /// gefunden" auffallen. Die Meldung der API gehoert in die Testausgabe.
        /// </summary>
        private static async Task EnsureSuccessful(HttpResponseMessage response, string what)
        {
            var body = await response.Content.ReadAsStringAsync();
            response.StatusCode.Should().Be(HttpStatusCode.OK, "{0} antwortete: {1}", what, body);
            body.Should().NotContain("\"successful\":false", "{0} antwortete: {1}", what, body);
        }

        public void Dispose()
        {
            _factory.Dispose();
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previousStorageRoot);

            if (Directory.Exists(_storageRoot))
            {
                Directory.Delete(_storageRoot, recursive: true);
            }
        }
    }

    private sealed class UrlaubsantragFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Der technische Benutzer-Header gilt nur in der Entwicklung; ohne ihn liesse sich
            // das Beispiel hier nicht deployen.
            builder.UseSetting(WebHostDefaults.EnvironmentKey, "Development");
            // Ohne Zeitgeber und ohne Drosselung: Der Test misst den Ablauf, nicht den Betrieb.
            builder.UseSetting("TimerScheduler:Enabled", "false");
            builder.UseSetting("RateLimiting:Enabled", "false");
        }
    }
}
