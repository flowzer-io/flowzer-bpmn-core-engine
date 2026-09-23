using System.Reflection;
using System.Text.Json;
using FilesystemStorageSystem;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Model;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Controller;
using WebApiEngine.Jobs;
using WebApiEngine.Shared;
using Variables = System.Dynamic.ExpandoObject;

namespace WebApiEngine.Tests;

/// <summary>
/// Ein Auftrag ohne verbleibende Versuche bleibt liegen. Der Betrieb gibt ihn wieder frei — auf
/// Wunsch mit korrigierten Eingaben, denn oft war genau eine falsche Eingabe der Grund.
/// </summary>
[NonParallelizable]
public class ServiceTaskJobRetryTest
{
    private static readonly Guid OperatorUser = Guid.Parse("3F1A55C7-0C2E-4B18-9D44-2E6B7A0C51F3");

    // Testzweck: Nach der Freigabe hat der Auftrag wieder Versuche, und der Worker bekommt beim
    // naechsten Abholen die korrigierten Werte — genau daran haengt der Nutzen der Aktion.
    [Test]
    public async Task Retry_ShouldRestoreAttemptsAndHandTheCorrectedValuesToTheNextWorker()
    {
        using var context = new RetryWorkerContext();
        var job = await context.StartInstanceAndStallTheJob();

        // So kommt die Freigabe tatsaechlich an: als JSON auf dem Endpunkt.
        var request = JsonSerializer.Deserialize<RetryJobRequestDto>(
            """{ "retries": 2, "variables": { "iban": "DE02120300000000202051" } }""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        var action = await context.Controller.RetryJob(job.Id, request);

        action.Result.Should().BeOfType<OkObjectResult>();
        var refetched = await context.JobService.FetchAndLock("zahlung", OperatorUser, "worker-b", 10, TimeSpan.FromMinutes(5));
        var handed = refetched.Should().ContainSingle().Subject;
        handed.Id.Should().Be(job.Id);
        handed.Retries.Should().Be(2);
        var variables = (IDictionary<string, object?>)handed.Variables!;
        variables["iban"].Should().Be("DE02120300000000202051");
        // Ungenannte Eingaben bleiben stehen: Der Betrieb korrigiert einen Wert, er schreibt den
        // Auftrag nicht neu.
        variables["betrag"].Should().Be(42L);
    }

    // Testzweck: Die bisherige Fehlermeldung bleibt als Verlauf stehen. Sie ist der einzige
    // Hinweis darauf, warum jemand eingegriffen hat.
    [Test]
    public async Task Retry_ShouldKeepTheLastErrorMessage()
    {
        using var context = new RetryWorkerContext();
        var job = await context.StartInstanceAndStallTheJob();

        await context.Controller.RetryJob(job.Id, new RetryJobRequestDto { Retries = 1 });

        var stored = await context.GetJob(job.Id);
        stored!.LastErrorMessage.Should().Be("IBAN ungueltig");
        stored.RetryAt.Should().BeNull();
        stored.LockedBy.Should().BeNull();
        stored.LockedUntil.Should().BeNull();
    }

    // Testzweck: Jede Freigabe hinterlaesst Akteur, Zeitpunkt, Versuche und die Namen der
    // korrigierten Eingaben — nie deren Werte. Ohne die Spur waere nicht mehr nachvollziehbar,
    // wer einen Service-Task mit Seiteneffekt ein zweites Mal hat laufen lassen.
    [Test]
    public async Task Retry_ShouldRecordTheActorAndOnlyTheNamesOfTheCorrectedInputs()
    {
        using var context = new RetryWorkerContext();
        var job = await context.StartInstanceAndStallTheJob();

        var request = JsonSerializer.Deserialize<RetryJobRequestDto>(
            """{ "retries": 3, "variables": { "iban": "DE02120300000000202051", "avis": "per Post" } }""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        await context.Controller.RetryJob(job.Id, request);

        var entry = (await context.GetJob(job.Id))!.RetryHistory.Should().ContainSingle().Subject;
        entry.By.Should().Be(OperatorUser);
        entry.Retries.Should().Be(3);
        entry.At.Should().Be(context.Time.GetUtcNow().UtcDateTime);
        entry.CorrectedKeys.Should().Equal("avis", "iban");
    }

    // Testzweck: Ein Auftrag mit verbleibenden Versuchen liegt nicht und braucht keine Freigabe.
    // Sie stillschweigend auszufuehren wuerde einem arbeitenden Worker die Versuche verstellen.
    [Test]
    public async Task Retry_OnAJobThatStillHasAttempts_ShouldReportAConflict()
    {
        using var context = new RetryWorkerContext();
        var job = await context.StartInstance();

        var action = await context.Controller.RetryJob(job.Id, new RetryJobRequestDto { Retries = 1 });

        var response = action.Result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(409);
        response.Value.Should().BeOfType<ProblemDetails>();
    }

    // Testzweck: Haelt noch jemand eine gueltige Sperre auf dem Auftrag, wird nichts geaendert.
    // Diese Lage entsteht in den regulaeren Ablaeufen nicht — Fehlschlag und fachlicher Fehler
    // geben die Sperre frei —, wohl aber nach einem Abbruch mittendrin. Dem Worker die Versuche
    // unter der Hand zurueckzusetzen, waere dann genau der doppelte Lauf, den die Sperre
    // verhindern soll.
    [Test]
    public async Task Retry_OnAJobThatIsStillLocked_ShouldReportAConflict()
    {
        using var context = new RetryWorkerContext();
        var job = await context.StartInstanceAndStallTheJob();
        await context.WithStoredJob(job.Id, stored =>
        {
            stored.LockedBy = "fremder-worker";
            stored.LockedUntil = context.Time.GetUtcNow().UtcDateTime.AddMinutes(5);
        });

        var action = await context.Controller.RetryJob(job.Id, new RetryJobRequestDto { Retries = 1 });

        var response = action.Result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(409);
        (await context.GetJob(job.Id))!.Retries.Should().Be(0);
    }

    // Testzweck: Einen unbekannten Auftrag gibt es nicht mehr; das ist kein Konflikt, sondern
    // eine 404 — der Betrieb soll ihn aus der Stoerungsliste verschwinden sehen.
    [Test]
    public async Task Retry_OnAnUnknownJob_ShouldAnswerWithNotFound()
    {
        using var context = new RetryWorkerContext();

        var action = await context.Controller.RetryJob(Guid.NewGuid(), new RetryJobRequestDto { Retries = 1 });

        var response = action.Result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(404);
        response.Value.Should().BeOfType<ProblemDetails>();
    }

    // Testzweck: Versuche ausserhalb des oeffentlichen Limits werden vor jedem Ablagezugriff als
    // Problem Details abgelehnt; null Versuche waeren eine Freigabe, die nichts freigibt.
    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(101)]
    public async Task Retry_WithRetriesOutsideTheLimit_ShouldAnswerWithBadRequest(int retries)
    {
        var controller = new JobController(null!, null!, null!);

        var action = await controller.RetryJob(Guid.NewGuid(), new RetryJobRequestDto { Retries = retries });

        var response = action.Result.Should().BeOfType<ObjectResult>().Subject;
        response.StatusCode.Should().Be(400);
        response.Value.Should().BeOfType<ProblemDetails>();
    }

    // Testzweck: Ein angemeldeter Worker erfaehrt von der Freigabe auf demselben Weg wie von
    // einem neuen Auftrag — ueber den Hintergrunddienst, der freie Auftraege meldet. Ohne das
    // wartete ein Worker ohne eigenen Abfragetakt vergeblich auf die wieder freigegebene Arbeit.
    [Test]
    public async Task Retry_ShouldMakeTheJobAnnouncedToAWebhookAgain()
    {
        using var context = new RetryWorkerContext();
        var job = await context.StartInstanceAndStallTheJob();
        var webhookOptions = new FlowzerWebhookOptions { AllowedHosts = ["93.184.216.34"] };
        var webhookService = new ServiceTaskWebhookService(context.Provider, webhookOptions, context.Time);
        (await webhookService.Register("zahlung", "https://93.184.216.34/hook", null, null, OperatorUser))
            .Error.Should().BeNull();
        var deliveries = new RecordingHttpClientFactory();
        var notifier = new ServiceTaskWebhookNotifier(
            context.Provider,
            webhookService,
            deliveries,
            webhookOptions,
            context.Time,
            NullLogger<ServiceTaskWebhookNotifier>.Instance);

        // Ein liegen gebliebener Auftrag ist keine freie Arbeit; dafuer wird niemand gerufen.
        await notifier.NotifyPendingJobs(CancellationToken.None);
        deliveries.Payloads.Should().BeEmpty();

        await context.Controller.RetryJob(job.Id, new RetryJobRequestDto { Retries = 1 });
        await notifier.NotifyPendingJobs(CancellationToken.None);

        deliveries.Payloads.Should().ContainSingle().Which.Should().Contain(job.Id.ToString());
    }

    /// <summary>Nimmt die Benachrichtigungen entgegen, statt sie tatsaechlich zu verschicken.</summary>
    private sealed class RecordingHttpClientFactory : IHttpClientFactory
    {
        private readonly RecordingHandler _handler = new();

        public List<string> Payloads => _handler.Payloads;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);

        private sealed class RecordingHandler : HttpMessageHandler
        {
            public List<string> Payloads { get; } = [];

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
            {
                Payloads.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            }
        }
    }

    // Testzweck: Die Freigabe ist eine Betriebsentscheidung und keine Worker-Rueckmeldung. Sie
    // laesst einen Service-Task mit Seiteneffekt erneut laufen und darf dabei Prozessdaten
    // aendern; deshalb ueberschreibt der Endpunkt die Worker-Klassenregel mit der Betriebsrolle.
    [Test]
    public void Retry_ShouldRequireTheOperatorRole()
    {
        var method = typeof(JobController).GetMethod(nameof(JobController.RetryJob))!;

        method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
        method.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(FlowzerPolicies.Operator);
        typeof(JobController).GetCustomAttribute<AuthorizeAttribute>()!.Policy
            .Should().Be(FlowzerPolicies.Worker);
    }

    private sealed class RetryWorkerContext : IDisposable
    {
        private readonly string? _originalRoot;
        private readonly string _tempRoot;

        public RetryWorkerContext()
        {
            _originalRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            _tempRoot = Path.Combine(Path.GetTempPath(), "flowzer-job-retry-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _tempRoot);

            Time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
            Provider = new FileSystemTransactionalStorageProvider();
            BusinessLogic = new BpmnBusinessLogic(Provider);
            JobService = new ServiceTaskJobService(
                Provider,
                BusinessLogic,
                Time,
                NullLogger<ServiceTaskJobService>.Instance);
            Controller = new JobController(
                JobService,
                new ServiceTaskWebhookService(Provider, new FlowzerWebhookOptions(), Time),
                new OperatorUserContextAccessor());
        }

        public FakeTimeProvider Time { get; }
        public FileSystemTransactionalStorageProvider Provider { get; }
        public BpmnBusinessLogic BusinessLogic { get; }
        public ServiceTaskJobService JobService { get; }
        public JobController Controller { get; }

        public async Task<ServiceTaskJob?> GetJob(Guid jobId)
        {
            using var storage = Provider.GetTransactionalStorage();
            return await storage.ServiceTaskStorage.GetJob(jobId);
        }

        /// <summary>Setzt einen Zustand, den die regulaeren Wege nicht erzeugen.</summary>
        public async Task WithStoredJob(Guid jobId, Action<ServiceTaskJob> change)
        {
            using var storage = Provider.GetTransactionalStorage();
            var job = (await storage.ServiceTaskStorage.GetJob(jobId))!;
            change(job);
            await storage.ServiceTaskStorage.SaveJob(job);
            storage.CommitChanges();
        }

        /// <summary>Startet die Instanz; der Auftrag wartet dann mit seinem einen Versuch.</summary>
        public async Task<ServiceTaskJob> StartInstance()
        {
            await DeployProcess();
            await BusinessLogic.StartProcessInstance("Definitions_Retry", BuildStartVariables());
            using var storage = Provider.GetTransactionalStorage();
            return (await storage.ServiceTaskStorage.GetJobs()).Single();
        }

        /// <summary>Startet die Instanz und laesst den Auftrag mit verbrauchten Versuchen liegen.</summary>
        public async Task<ServiceTaskJob> StartInstanceAndStallTheJob()
        {
            var job = await StartInstance();
            await JobService.FetchAndLock("zahlung", OperatorUser, "worker-a", 10, TimeSpan.FromMinutes(5));
            var failed = await JobService.Fail(job.Id, OperatorUser, "worker-a", "IBAN ungueltig", 0, TimeSpan.Zero);
            failed.Should().Be(JobOperationResult.Ok);
            return job;
        }

        private static Variables BuildStartVariables()
        {
            var variables = new Variables();
            var writable = (IDictionary<string, object?>)variables;
            writable["betrag"] = 42L;
            writable["iban"] = "DE00000000000000000000";
            return variables;
        }

        private async Task DeployProcess()
        {
            var definition = new BpmnDefinition
            {
                Id = Guid.NewGuid(),
                DefinitionId = "Definitions_Retry",
                Hash = "hash",
                SavedByUser = OperatorUser,
                SavedOn = Time.GetUtcNow().UtcDateTime,
                Version = new Model.Version(1, 0),
                IsActive = false
            };

            using (var storage = Provider.GetTransactionalStorage())
            {
                await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
                {
                    DefinitionId = definition.DefinitionId,
                    Name = "Zahlung"
                });
                await storage.DefinitionStorage.StoreDefinition(definition);
                await storage.DefinitionStorage.StoreBinary(definition.Id, ProcessXml);
            }

            await BusinessLogic.DeployDefinition(definition);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _originalRoot);
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
    }

    private sealed class OperatorUserContextAccessor : ICurrentUserContextAccessor
    {
        public CurrentUserContext GetCurrentUser() => new(OperatorUser, "betrieb", false);
    }

    private const string ProcessXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                          id="Definitions_Retry" targetNamespace="http://bpmn.io/schema/bpmn">
          <bpmn:process id="Process_Retry" isExecutable="true">
            <bpmn:startEvent id="StartEvent_1">
              <bpmn:outgoing>Flow_1</bpmn:outgoing>
            </bpmn:startEvent>
            <bpmn:serviceTask id="ServiceTask_1" name="Zahlung ausloesen">
              <bpmn:extensionElements>
                <zeebe:taskDefinition type="zahlung" />
              </bpmn:extensionElements>
              <bpmn:incoming>Flow_1</bpmn:incoming>
              <bpmn:outgoing>Flow_2</bpmn:outgoing>
            </bpmn:serviceTask>
            <bpmn:endEvent id="EndEvent_1">
              <bpmn:incoming>Flow_2</bpmn:incoming>
            </bpmn:endEvent>
            <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="ServiceTask_1" />
            <bpmn:sequenceFlow id="Flow_2" sourceRef="ServiceTask_1" targetRef="EndEvent_1" />
          </bpmn:process>
        </bpmn:definitions>
        """;
}
