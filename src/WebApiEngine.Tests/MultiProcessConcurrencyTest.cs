using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Model;
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Konkurrenztests fuer den Mehrprozessbetrieb: zwei getrennte API-Hosts auf einer
/// PostgreSQL-Datenbank. Die prozesslokalen Sperren beider Hosts helfen hier nicht mehr —
/// was haelt, haelt in der Datenbank.
///
/// Jede Invariante laeuft <see cref="Rounds"/> Runden, damit ein Rennen nicht zufaellig
/// einmal gut ausgeht. Die Laufzeit der Klasse bleibt dabei unter zwei Minuten.
/// </summary>
[NonParallelizable]
public sealed partial class MultiProcessConcurrencyTest
{
    /// <summary>Wiederholungen je Invariante. Hoeher heisst sicherer und langsamer.</summary>
    private const int Rounds = 20;

    private static readonly Guid UserId = Guid.Parse("b2f9d0c6-2f0f-4f3a-9a2e-2b0f9c7a51d4");

    private MultiProcessCluster? _cluster;
    private HttpClient _firstClient = null!;
    private HttpClient _secondClient = null!;

    private MultiProcessCluster Cluster => _cluster!;
    private MultiProcessApiHost First => Cluster.First;
    private MultiProcessApiHost Second => Cluster.Second;

    [OneTimeSetUp]
    public async Task StartCluster()
    {
        _cluster = await MultiProcessCluster.TryStartAsync();
        if (_cluster is null)
        {
            Assert.Ignore("PostgreSQL-Container nicht verfuegbar (Docker fehlt?).");
            return;
        }

        _firstClient = First.CreateAuthenticatedClient(UserId);
        _secondClient = Second.CreateAuthenticatedClient(UserId);
    }

    [OneTimeTearDown]
    public async Task StopCluster()
    {
        _firstClient?.Dispose();
        _secondClient?.Dispose();
        if (_cluster is not null)
        {
            await _cluster.DisposeAsync();
        }
    }

    [SetUp]
    public Task ClearDatabase() => Cluster.ClearAsync();

    // Testzweck: Zwei API-Hosts holen gleichzeitig Auftraege desselben Typs. Kein Auftrag darf
    // zweimal vergeben werden, obwohl die Vergabesperre beider Hosts prozesslokal ist.
    [Test]
    public async Task JobFetch_ShouldHandOutEveryJobOnce_WhenBothHostsFetchConcurrently()
    {
        var definition = await DeployAsync(First, MultiProcessWorkflows.ServiceTaskDefinitionId,
            MultiProcessWorkflows.ServiceTask(), formName: null);

        var handedOut = new List<Guid>();
        for (var round = 0; round < Rounds; round++)
        {
            await First.Engine.StartProcessInstance(definition.DefinitionId);

            var claimed = await RaceAsync(
                () => FetchJobsAsync(_firstClient, $"worker-a-{round}"),
                () => FetchJobsAsync(_secondClient, $"worker-b-{round}"));

            // Je Runde wartet genau ein neuer Auftrag; die frueheren sind noch verliehen.
            claimed.Sum(jobs => jobs.Length).Should().Be(1,
                "ein fälliger Auftrag darf nur an einen der beiden Hosts gehen");
            handedOut.AddRange(claimed.SelectMany(jobs => jobs).Select(job => job.Id));
        }

        handedOut.Should().OnlyHaveUniqueItems("kein Auftrag darf zweimal vergeben werden");
        handedOut.Should().HaveCount(Rounds);
    }

    // Testzweck: Abschluss auf dem einen und Fehlschlag auf dem anderen Host treffen denselben
    // Auftrag. Genau einer gewinnt, der andere bekommt 409; der Endzustand von Auftrag und
    // Instanz passt zum Gewinner.
    [Test]
    public async Task JobCompleteAgainstFail_ShouldLeaveExactlyOneWinner_WhenBothHostsAct()
    {
        var definition = await DeployAsync(First, MultiProcessWorkflows.ServiceTaskDefinitionId,
            MultiProcessWorkflows.ServiceTask(), formName: null);

        for (var round = 0; round < Rounds; round++)
        {
            var instance = await First.Engine.StartProcessInstance(definition.DefinitionId);
            // Dieselbe Worker-Kennung auf beiden Hosts: genau der Fall hinter einem Lastverteiler.
            const string workerId = "mp-worker";
            var jobs = await FetchJobsAsync(_firstClient, workerId);
            jobs.Should().ContainSingle();
            var jobId = jobs[0].Id;

            var responses = await RaceAsync(
                () => PostAsync(_firstClient, $"/job/{jobId}/complete",
                    new CompleteJobRequestDto { WorkerId = workerId }),
                () => PostAsync(_secondClient, $"/job/{jobId}/fail",
                    new FailJobRequestDto { WorkerId = workerId, ErrorMessage = "multi-process" }));

            var completed = responses[0] == HttpStatusCode.OK;
            var failed = responses[1] == HttpStatusCode.OK;
            (completed ^ failed).Should().BeTrue(
                $"genau ein Aufruf darf gewinnen, Antworten waren {responses[0]} und {responses[1]}");
            responses.Should().Contain(
                status => status == HttpStatusCode.Conflict || status == HttpStatusCode.NotFound,
                "der unterlegene Aufruf meldet den Besitzkonflikt");

            var stored = await First.Storage.InstanceStorage.GetProcessInstance(instance.InstanceId);
            var remainingJobs = await Cluster.CountAsync("service_task_jobs",
                $"process_instance_id = '{instance.InstanceId}'");
            if (completed)
            {
                stored.IsFinished.Should().BeTrue("der Abschluss fuehrt den Token weiter");
                remainingJobs.Should().Be(0, "ein abgeschlossener Auftrag darf nicht wieder auferstehen");
            }
            else
            {
                stored.IsFinished.Should().BeFalse("ein gescheiterter Auftrag laesst die Instanz warten");
                remainingJobs.Should().Be(1);
            }
        }
    }

    // Testzweck: Beide Hosts schliessen dieselbe Aufgabe gleichzeitig ab. Genau ein Abschluss
    // gewinnt und die Instanz laeuft genau einmal weiter — keine doppelten Folge-Tokens.
    [Test]
    public async Task UserTaskCompletion_ShouldAdvanceInstanceOnce_WhenBothHostsComplete()
    {
        var definition = await DeployAsync(First, MultiProcessWorkflows.UserTaskDefinitionId,
            MultiProcessWorkflows.UserTask());

        for (var round = 0; round < Rounds; round++)
        {
            var instance = await First.Engine.StartProcessInstance(definition.DefinitionId);
            var task = (await First.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Single();
            var result = new UserTaskResultDto
            {
                FlowNodeId = "Review",
                TokenId = task.Token.Id,
                ProcessInstanceId = instance.InstanceId
            };

            var responses = await RaceAsync(
                () => PostAsync(_firstClient, "/usertask", result),
                () => PostAsync(_secondClient, "/usertask", result));

            responses.Count(status => status == HttpStatusCode.OK).Should().Be(1,
                $"genau ein Abschluss darf gewinnen, Antworten waren {responses[0]} und {responses[1]}");
            responses.Should().Contain(HttpStatusCode.NotFound);

            var stored = await First.Storage.InstanceStorage.GetProcessInstance(instance.InstanceId);
            stored.IsFinished.Should().BeTrue();
            stored.Tokens.Count(token => token.CurrentFlowNode?.Id == "End").Should().Be(1,
                "der Abschluss darf den Folgepfad nur einmal erzeugen");
            (await First.Storage.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Should().BeEmpty();
        }
    }

    // Testzweck: Dieselbe Nachricht trifft gleichzeitig auf beiden Hosts ein. Ein wartendes
    // Catch-Event wird genau einmal bedient; der zweite Aufruf findet keine Anmeldung mehr.
    [Test]
    public async Task Message_ShouldServeWaitingCatchEventOnce_WhenBothHostsCorrelate()
    {
        var definition = await DeployAsync(First, MultiProcessWorkflows.MessageCatchDefinitionId,
            MultiProcessWorkflows.MessageCatch(), formName: null);

        for (var round = 0; round < Rounds; round++)
        {
            var instance = await First.Engine.StartProcessInstance(definition.DefinitionId);
            // Der Laufzeitvertrag adressiert ein wartendes Ereignis ueber Name, Korrelations-
            // schluessel und Instanz; ohne die Instanz traefe die Nachricht nur Startereignisse.
            var message = new MessageDto
            {
                Name = MultiProcessWorkflows.CatchMessageName,
                CorrelationKey = MultiProcessWorkflows.CatchCorrelationKey,
                InstanceId = instance.InstanceId
            };

            var responses = await RaceAsync(
                () => PostAsync(_firstClient, "/message", message),
                () => PostAsync(_secondClient, "/message", message));

            responses.Count(status => status == HttpStatusCode.OK).Should().Be(1,
                $"nur ein Aufruf darf das wartende Ereignis bedienen, Antworten waren {responses[0]} und {responses[1]}");

            var stored = await First.Storage.InstanceStorage.GetProcessInstance(instance.InstanceId);
            stored.IsFinished.Should().BeTrue();
            stored.Tokens.Count(token => token.CurrentFlowNode?.Id == "End").Should().Be(1);
            (await First.Storage.SubscriptionStorage.GetMessageSubscription(instance.InstanceId))
                .Should().BeEmpty();
        }
    }

    // Testzweck: Ein Nachrichten-Startereignis ohne Idempotenzschluessel startet je Aufruf eine
    // Instanz. Zwei gleichzeitige Nachrichten erzeugen deshalb zwei Instanzen — das ist der
    // vertraglich richtige Ausgang und keine Mehrprozesslücke.
    [Test]
    public async Task MessageStart_ShouldStartOneInstancePerCall_WhenBothHostsPublish()
    {
        await DeployAsync(First, MultiProcessWorkflows.MessageStartDefinitionId,
            MultiProcessWorkflows.MessageStart(), formName: null);
        var message = new MessageDto { Name = MultiProcessWorkflows.StartMessageName };

        var responses = await RaceAsync(
            () => PostAsync(_firstClient, "/message", message),
            () => PostAsync(_secondClient, "/message", message));

        responses.Should().AllBeEquivalentTo(HttpStatusCode.OK);
        (await Cluster.CountAsync("instances")).Should().Be(2,
            "ohne Idempotenzschluessel ist jede Nachricht ein eigener Start");
    }

    /// <summary>Deployt eine Definition ueber die Engine desselben Hosts und liefert sie zurueck.</summary>
    private static async Task<BpmnDefinition> DeployAsync(
        MultiProcessApiHost host, string definitionId, string xml, string? formName = "Approval")
    {
        var storage = host.Storage;
        if (formName is not null)
        {
            await FormTestSeed.StoreAsync(storage, formName);
        }

        if ((await storage.DefinitionStorage.GetAllMetaDefinitions())
            .All(meta => meta.DefinitionId != definitionId))
        {
            await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            {
                DefinitionId = definitionId,
                Name = definitionId
            });
        }

        var previous = await storage.DefinitionStorage.GetMaxVersionId(definitionId);
        var definition = new BpmnDefinition
        {
            Id = Guid.NewGuid(),
            DefinitionId = definitionId,
            Hash = "multi-process",
            SavedByUser = UserId,
            SavedOn = DateTime.UtcNow,
            Version = previous is null ? new Model.Version(1, 0) : new Model.Version(previous.Major + 1, 0),
            IsActive = false
        };
        await storage.DefinitionStorage.StoreDefinition(definition);
        await storage.DefinitionStorage.StoreBinary(definition.Id, xml);
        await host.Engine.DeployDefinition(definition);
        return definition;
    }

    private static async Task<ServiceTaskJobDto[]> FetchJobsAsync(HttpClient client, string workerId)
    {
        using var response = await client.PostAsJsonAsync("/job/fetch", new FetchJobsRequestDto
        {
            Type = MultiProcessWorkflows.ServiceTaskType,
            WorkerId = workerId,
            MaxJobs = 10,
            LockSeconds = 300
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ApiStatusResult<ServiceTaskJobDto[]>>();
        return body?.Result ?? [];
    }

    private static async Task<HttpStatusCode> PostAsync(HttpClient client, string path, object payload)
    {
        using var response = await client.PostAsJsonAsync(path, payload);
        return response.StatusCode;
    }

    private static async Task<HttpStatusCode> PostXmlAsync(HttpClient client, string path, string xml)
    {
        using var content = new StringContent(xml, Encoding.UTF8, "application/xml");
        using var response = await client.PostAsync(path, content);
        return response.StatusCode;
    }

    /// <summary>
    /// Startet alle Aufrufe an derselben Schranke, damit sie sich wirklich begegnen. Ohne die
    /// Schranke liefe der erste Aufruf regelmaessig fertig, bevor der zweite beginnt.
    /// </summary>
    private static async Task<T[]> RaceAsync<T>(params Func<Task<T>>[] operations)
    {
        using var barrier = new Barrier(operations.Length);
        var tasks = operations
            .Select(operation => Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await operation();
            }))
            .ToArray();
        return await Task.WhenAll(tasks);
    }

    /// <summary>Wie <see cref="RaceAsync{T}"/>, faengt aber Ausnahmen als Ergebnis ab.</summary>
    private static Task<Exception?[]> RaceCatchingAsync(params Func<Task>[] operations) =>
        RaceAsync(operations.Select<Func<Task>, Func<Task<Exception?>>>(operation => async () =>
        {
            try
            {
                await operation();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }).ToArray());
}
