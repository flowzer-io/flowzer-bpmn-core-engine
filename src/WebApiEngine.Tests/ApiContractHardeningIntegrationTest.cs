using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BPMN.Common;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Model;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.Shared;
using Variables = System.Dynamic.ExpandoObject;

namespace WebApiEngine.Tests;

/// <summary>
/// Integrationstests fuer API-Vertraege, die von einem reinen REST-Client (ohne .NET-Kenntnis
/// der DTOs) korrekt konsumierbar sein muessen: Content-Types, Statuscodes fuer Modellfehler
/// und die Auflösung von User-Task-Formularen auf dem Server.
/// </summary>
[NonParallelizable]
public class ApiContractHardeningIntegrationTest
{
    private static readonly Guid TestUserId = Guid.Parse("9AB0E5C5-A5B4-4F87-A857-EB821D12AF6E");

    // Testzweck: Ein Client, der `Accept: application/json` sendet, muss die BPMN-Definition trotzdem
    // als rohes XML mit passendem Content-Type erhalten. Ueber `Ok(string)` lieferte die
    // Content-Negotiation zuvor ein JSON-String-Literal, das kein XML-Parser lesen kann.
    [Test]
    public async Task GetDefinitionXml_ShouldReturnRawXmlWithXmlContentType_EvenWhenClientAcceptsJson()
    {
        var storage = new TestStorage();
        var definitionId = Guid.NewGuid();
        const string xml = """
                           <?xml version="1.0" encoding="UTF-8"?>
                           <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Definitions_1" targetNamespace="http://bpmn.io/schema/bpmn">
                             <bpmn:process id="Process_1" isExecutable="true" />
                           </bpmn:definitions>
                           """;
        storage.Binaries[definitionId] = xml;

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await client.GetAsync($"/definition/xml/{definitionId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/xml");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be(xml);
    }

    // Testzweck: Ein fachlicher Modellfehler zur Laufzeit (hier: exklusives Gateway ohne passende
    // Bedingung und ohne Standardfluss) ist kein Serverdefekt. Er muss als 422 mit lesbarer
    // Meldung ankommen, damit Modellierende ihn in der Oberflaeche diagnostizieren koennen,
    // statt als maskierter 500 "unexpected server error".
    [Test]
    public async Task UserTaskCompletion_ShouldReturnUnprocessableEntity_WhenModelFailsAtRuntime()
    {
        var storage = new TestStorage();
        var instanceId = Guid.NewGuid();
        var (userTaskToken, tokens) = CreateInstanceWaitingAtUserTaskBeforeBrokenGateway(instanceId);
        var definitionId = Guid.NewGuid();
        storage.Instances.Add(new ProcessInstanceInfo
        {
            InstanceId = instanceId,
            metaDefinitionId = "broken-gateway",
            DefinitionId = definitionId,
            ProcessId = "Process_BrokenGateway",
            Tokens = tokens,
            IsFinished = false,
            State = ProcessInstanceState.Waiting,
            MessageSubscriptionCount = 0,
            SignalSubscriptionCount = 0,
            UserTaskSubscriptionCount = 1,
            ServiceSubscriptionCount = 0
        });

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/usertask", new UserTaskResultDto
        {
            FlowNodeId = "UserTask_Review",
            TokenId = userTaskToken.Id,
            ProcessInstanceId = instanceId
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("Exclusive Gateway");
    }

    // Testzweck: Eine wartende Instanz muss sich ueber die API abbrechen lassen: Tokens werden
    // terminiert, offene User-Task-Subscriptions entfernt, die Instanz gilt als beendet.
    [Test]
    public async Task CancelInstance_ShouldTerminateWaitingInstance_AndRemoveOpenSubscriptions()
    {
        var storage = new TestStorage();
        var instanceId = Guid.NewGuid();
        var (_, tokens) = CreateInstanceWaitingAtUserTaskBeforeBrokenGateway(instanceId);
        storage.Instances.Add(CreateWaitingInstance(instanceId, tokens));

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/instance/{instanceId}/cancel", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>();
        payload!.Successful.Should().BeTrue();
        payload.Result!.State.Should().Be(ProcessInstanceStateDto.Terminated);
        payload.Result.FinishedAt.Should().NotBeNull();
        storage.Instances.Single().IsFinished.Should().BeTrue();
        storage.RemovedUserTaskSubscriptionInstanceIds.Should().Contain(instanceId);
        storage.RemovedMessageSubscriptionInstanceIds.Should().Contain(instanceId);
        storage.RemovedSignalSubscriptionInstanceIds.Should().Contain(instanceId);
        storage.RemovedTimerSubscriptionInstanceIds.Should().Contain(instanceId);
        storage.AddedUserTaskSubscriptions.Should().BeEmpty("a cancelled instance must not re-register user tasks");
    }

    // Testzweck: Eine bereits beendete Instanz kann nicht erneut abgebrochen werden; das ist ein
    // Zustandskonflikt (409), kein Serverfehler und kein stilles OK.
    [Test]
    public async Task CancelInstance_ShouldReturnConflict_WhenInstanceIsAlreadyFinished()
    {
        var storage = new TestStorage();
        var instanceId = Guid.NewGuid();
        var (_, tokens) = CreateInstanceWaitingAtUserTaskBeforeBrokenGateway(instanceId);
        foreach (var token in tokens)
        {
            token.State = FlowNodeState.Completed;
        }
        var instance = CreateWaitingInstance(instanceId, tokens);
        instance.IsFinished = true;
        instance.State = ProcessInstanceState.Completed;
        storage.Instances.Add(instance);

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/instance/{instanceId}/cancel", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("already finished");
    }

    // Testzweck: Abbruch ist eine Verwaltungsaktion und verlangt wie Deploy und User-Task-Abschluss
    // einen aufgeloesten Benutzerkontext (401 ausserhalb von Development ohne Anmeldung).
    [Test]
    public async Task CancelInstance_ShouldReturnUnauthorized_WhenNoUserContextOutsideDevelopment()
    {
        await using var factory = new TestWebApplicationFactory(new TestStorage(), environmentName: "Production", useFixedUser: false);
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/instance/{Guid.NewGuid()}/cancel", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Testzweck: Abbruch einer unbekannten Instanz ist 404.
    [Test]
    public async Task CancelInstance_ShouldReturnNotFound_WhenInstanceDoesNotExist()
    {
        await using var factory = new TestWebApplicationFactory(new TestStorage());
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/instance/{Guid.NewGuid()}/cancel", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static ProcessInstanceInfo CreateWaitingInstance(Guid instanceId, List<Token> tokens)
    {
        return new ProcessInstanceInfo
        {
            InstanceId = instanceId,
            metaDefinitionId = "broken-gateway",
            DefinitionId = Guid.NewGuid(),
            ProcessId = "Process_BrokenGateway",
            Tokens = tokens,
            IsFinished = false,
            State = ProcessInstanceState.Waiting,
            MessageSubscriptionCount = 0,
            SignalSubscriptionCount = 0,
            UserTaskSubscriptionCount = 1,
            ServiceSubscriptionCount = 0
        };
    }

    // Testzweck: Die Formular-Auflösung (Form-Key -> neueste Version) gehoert in die API. Ohne
    // Versionsangabe im Form-Key muss die hoechste gespeicherte Version geliefert werden.
    [Test]
    public async Task GetUserTaskForm_ShouldReturnLatestFormVersion_WhenFormKeyHasNoVersion()
    {
        var storage = new TestStorage();
        var formId = Guid.NewGuid();
        storage.FormMetadatas.Add(new FormMetadata { FormId = formId, Name = "Approval" });
        storage.Forms.Add(new Form { Id = Guid.NewGuid(), FormId = formId, Version = new Model.Version(1, 0), FormData = "{\"v\":1}" });
        var latest = new Form { Id = Guid.NewGuid(), FormId = formId, Version = new Model.Version(2, 0), FormData = "{\"v\":2}" };
        storage.Forms.Add(latest);
        var subscription = AddUserTaskSubscription(storage, formKey: "Approval");

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/usertask/{subscription.Id}/form");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        payload.Result!.Id.Should().Be(latest.Id);
        payload.Result.FormData.Should().Be("{\"v\":2}");
    }

    // Testzweck: Ein Form-Key der Form `Name:Major.Minor` muss genau diese Version liefern,
    // auch wenn eine neuere Version existiert.
    [Test]
    public async Task GetUserTaskForm_ShouldReturnRequestedVersion_WhenFormKeyContainsVersion()
    {
        var storage = new TestStorage();
        var formId = Guid.NewGuid();
        storage.FormMetadatas.Add(new FormMetadata { FormId = formId, Name = "Approval" });
        var requested = new Form { Id = Guid.NewGuid(), FormId = formId, Version = new Model.Version(1, 0), FormData = "{\"v\":1}" };
        storage.Forms.Add(requested);
        storage.Forms.Add(new Form { Id = Guid.NewGuid(), FormId = formId, Version = new Model.Version(2, 0), FormData = "{\"v\":2}" });
        var subscription = AddUserTaskSubscription(storage, formKey: "Approval:1.0");

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/usertask/{subscription.Id}/form");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload!.Result!.Id.Should().Be(requested.Id);
    }

    // Testzweck: Ein Formularname darf selbst Doppelpunkte enthalten. Nur ein Suffix, das eine
    // Version ist, gilt als Versionsangabe.
    [Test]
    public async Task GetUserTaskForm_ShouldTreatColonInNameAsPartOfName_WhenSuffixIsNoVersion()
    {
        var storage = new TestStorage();
        var formId = Guid.NewGuid();
        storage.FormMetadatas.Add(new FormMetadata { FormId = formId, Name = "Pruefung: Detail" });
        var form = new Form { Id = Guid.NewGuid(), FormId = formId, Version = new Model.Version(1, 0), FormData = "{}" };
        storage.Forms.Add(form);
        var subscription = AddUserTaskSubscription(storage, formKey: "Pruefung: Detail");

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/usertask/{subscription.Id}/form");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload!.Result!.Id.Should().Be(form.Id);
    }

    // Testzweck: Ein Definitionsupload jenseits der Obergrenze wird als 413 abgelehnt, bevor der
    // Body geparst wird. Ohne Limit koennte ein einzelner Request Speicher und die Engine-Sperre
    // beliebig lange binden.
    [Test]
    public async Task UploadDefinition_ShouldReturnPayloadTooLarge_WhenBodyExceedsLimit()
    {
        var storage = new TestStorage();
        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();
        var oversized = new string('x', WebApiEngine.Controller.FlowzerControllerBase.MaxRawContentBytes + 1);

        var response = await client.PostAsync("/definition", new StringContent(oversized));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        storage.Binaries.Should().BeEmpty();
    }

    // Testzweck: Ein unbekannter Form-Key ist ein Modellierungsfehler des Workflows und muss als
    // 400 mit sprechender Meldung zurueckkommen, nicht als leeres Formular oder 500.
    [Test]
    public async Task GetUserTaskForm_ShouldReturnBadRequest_WhenFormDoesNotExist()
    {
        var storage = new TestStorage();
        var subscription = AddUserTaskSubscription(storage, formKey: "DoesNotExist");

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/usertask/{subscription.Id}/form");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("DoesNotExist");
    }

    // Testzweck: Eine unbekannte User-Task-Id liefert 404 statt eines Fehlers im Auflösungspfad.
    [Test]
    public async Task GetUserTaskForm_ShouldReturnNotFound_WhenUserTaskDoesNotExist()
    {
        var storage = new TestStorage();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/usertask/{Guid.NewGuid()}/form");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Ohne aufgeloesten Benutzerkontext darf der Formular-Endpunkt ausserhalb von
    // Development nichts liefern (401), analog zu den uebrigen User-Task-Pfaden.
    [Test]
    public async Task GetUserTaskForm_ShouldReturnUnauthorized_WhenNoUserContextOutsideDevelopment()
    {
        var storage = new TestStorage();
        var subscription = AddUserTaskSubscription(storage, formKey: "Approval");

        await using var factory = new TestWebApplicationFactory(storage, environmentName: "Production", useFixedUser: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/usertask/{subscription.Id}/form");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Testzweck: Die User-Task-Liste muss Form-Key, Faelligkeit, Wiedervorlage und Prioritaet aus dem
    // BPMN-Modell flach im DTO liefern, damit Clients nicht das dynamische Flow-Element parsen.
    [Test]
    public async Task GetAllUserTasks_ShouldExposeFormKeyAndScheduleFromModel()
    {
        var storage = new TestStorage();
        AddUserTaskSubscription(storage, formKey: "Approval:1.0", dueDate: "2026-10-01T10:00:00Z", followUpDate: "2026-09-28T10:00:00Z");

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/usertask");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ExtendedUserTaskSubscriptionDto[]>>();
        var dto = payload!.Result.Should().ContainSingle().Subject;
        dto.FormKey.Should().Be("Approval:1.0");
        dto.DueDate.Should().Be("2026-10-01T10:00:00Z");
        dto.FollowUpDate.Should().Be("2026-09-28T10:00:00Z");
    }

    private static ExtendedUserTaskSubscription AddUserTaskSubscription(
        TestStorage storage,
        string formKey,
        string? dueDate = null,
        string? followUpDate = null)
    {
        var instanceId = Guid.NewGuid();
        var subscription = new ExtendedUserTaskSubscription
        {
            Id = Guid.NewGuid(),
            Name = "Review",
            Token = new Token
            {
                ProcessInstanceId = instanceId,
                CurrentBaseElement = new BPMN.HumanInteraction.UserTask
                {
                    Id = "UserTask_Review",
                    Name = "Review",
                    Implementation = formKey,
                    FlowzerDueDate = dueDate,
                    FlowzerFollowUpDate = followUpDate
                },
                ActiveBoundaryEvents = [],
                State = FlowNodeState.Active
            },
            ProcessInstanceId = instanceId,
            MetaDefinitionId = "review-process",
            DefinitionId = Guid.NewGuid(),
            ProcessId = "Process_Review",
            DefinitionMetaName = "Review Process",
            DefinitionVersion = new Model.Version(1, 0)
        };
        storage.UserTaskSubscriptions.Add(subscription);
        return subscription;
    }

    /// <summary>
    /// Baut eine Instanz, die an einem User-Task wartet. Der nachfolgende exklusive Gateway hat
    /// nur bedingte Ausgaenge, die niemals zutreffen, und keinen Standardfluss.
    /// </summary>
    private static (Token UserTaskToken, List<Token> Tokens) CreateInstanceWaitingAtUserTaskBeforeBrokenGateway(Guid instanceId)
    {
        var userTask = new BPMN.HumanInteraction.UserTask { Id = "UserTask_Review", Name = "Review", Implementation = "Approval" };
        var gateway = new BPMN.Gateways.ExclusiveGateway { Id = "Gateway_Broken", Name = "Broken" };
        var endA = new BPMN.Events.EndEvent { Id = "End_A", Name = "A" };
        var endB = new BPMN.Events.EndEvent { Id = "End_B", Name = "B" };
        var process = new BPMN.Process.Process
        {
            Id = "Process_BrokenGateway",
            Name = "Broken gateway",
            DefinitionsId = "Definitions_BrokenGateway",
            IsExecutable = true,
            FlowElements =
            [
                userTask,
                gateway,
                endA,
                endB,
                new SequenceFlow { Id = "Flow_ToGateway", Name = "", SourceRef = userTask, TargetRef = gateway },
                new SequenceFlow { Id = "Flow_A", Name = "", SourceRef = gateway, TargetRef = endA, FlowzerCondition = "=Approved=\"never-a\"" },
                new SequenceFlow { Id = "Flow_B", Name = "", SourceRef = gateway, TargetRef = endB, FlowzerCondition = "=Approved=\"never-b\"" }
            ]
        };

        var masterToken = new Token
        {
            ProcessInstanceId = instanceId,
            CurrentBaseElement = process,
            ActiveBoundaryEvents = [],
            State = FlowNodeState.Active,
            Variables = new Variables()
        };
        var userTaskToken = new Token
        {
            ProcessInstanceId = instanceId,
            ParentTokenId = masterToken.Id,
            CurrentBaseElement = userTask,
            ActiveBoundaryEvents = [],
            State = FlowNodeState.Active
        };

        return (userTaskToken, [masterToken, userTaskToken]);
    }

    private sealed class TestWebApplicationFactory(
        TestStorage storage,
        string environmentName = "Development",
        bool useFixedUser = true) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(WebHostDefaults.EnvironmentKey, environmentName);
            builder.ConfigureAppConfiguration((_, configBuilder) =>
            {
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TimerScheduler:Enabled"] = "false"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IStorageSystem>();
                services.RemoveAll<ITransactionalStorageProvider>();
                services.AddSingleton<IStorageSystem>(storage);
                services.AddSingleton<ITransactionalStorageProvider>(new TestTransactionalStorageProvider(storage));

                if (useFixedUser)
                {
                    services.RemoveAll<ICurrentUserContextAccessor>();
                    services.AddSingleton<ICurrentUserContextAccessor>(new FixedCurrentUserContextAccessor());
                }
            });
        }
    }

    private sealed class FixedCurrentUserContextAccessor : ICurrentUserContextAccessor
    {
        public CurrentUserContext GetCurrentUser() => new(TestUserId, "test", false);
    }

    private sealed class TestTransactionalStorageProvider(TestStorage storage) : ITransactionalStorageProvider
    {
        public ITransactionalStorage GetTransactionalStorage() => storage;
    }

    /// <summary>
    /// In-memory Storage. Nur die fuer diese Tests benoetigten Pfade sind implementiert; alles andere
    /// wirft bewusst, damit ein Test sofort zeigt, wenn sich die API-Abhaengigkeiten aendern.
    /// </summary>
    private sealed class TestStorage : ITransactionalStorage
    {
        public Dictionary<Guid, string> Binaries { get; } = [];
        public List<ProcessInstanceInfo> Instances { get; } = [];
        public List<ExtendedUserTaskSubscription> UserTaskSubscriptions { get; } = [];
        public List<FormMetadata> FormMetadatas { get; } = [];
        public List<Form> Forms { get; } = [];
        public List<Guid> RemovedUserTaskSubscriptionInstanceIds { get; } = [];
        public List<Guid> RemovedMessageSubscriptionInstanceIds { get; } = [];
        public List<Guid> RemovedSignalSubscriptionInstanceIds { get; } = [];
        public List<Guid> RemovedTimerSubscriptionInstanceIds { get; } = [];
        public List<UserTaskSubscription> AddedUserTaskSubscriptions { get; } = [];

        public IDefinitionStorage DefinitionStorage => new TestDefinitionStorage(this);
        public IMessageSubscriptionStorage SubscriptionStorage => new TestSubscriptionStorage(this);
        public IInstanceStorage InstanceStorage => new TestInstanceStorage(this);
        public IFormStorage FormStorage => new TestFormStorage(this);
        public IServiceTaskStorage ServiceTaskStorage { get; } = new InMemoryServiceTaskStorage();

        public void CommitChanges()
        {
        }

        public void RollbackTransaction()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class TestDefinitionStorage(TestStorage storage) : IDefinitionStorage
    {
        public Task StoreBinary(Guid guid, string data)
        {
            storage.Binaries[guid] = data;
            return Task.CompletedTask;
        }

        public Task<string> GetBinary(Guid guid) =>
            storage.Binaries.TryGetValue(guid, out var data)
                ? Task.FromResult(data)
                : throw new FileNotFoundException($"Binary definition {guid} was not found.");

        public Task<Guid[]> GetAllBinaryDefinitions() => Task.FromResult(storage.Binaries.Keys.ToArray());
        public Task<BpmnDefinition[]> GetAllDefinitions() => Task.FromResult(Array.Empty<BpmnDefinition>());
        public Task StoreDefinition(BpmnDefinition definition) => throw new NotSupportedException();
        public Task<Model.Version?> GetMaxVersionId(string modelId) => throw new NotSupportedException();
        public Task<BpmnDefinition> GetDefinitionById(Guid id) => throw new NotSupportedException();
        public Task<BpmnDefinition> GetLatestDefinition(string definitionId) => throw new NotSupportedException();
        public Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId) => throw new NotSupportedException();
        public Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions() => Task.FromResult(Array.Empty<ExtendedBpmnMetaDefinition>());
        public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition) => throw new NotSupportedException();
        public Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition) => throw new NotSupportedException();
        public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id) => throw new NotSupportedException();
    }

    private sealed class TestSubscriptionStorage(TestStorage storage) : IMessageSubscriptionStorage
    {
        public Task<IEnumerable<MessageSubscription>> GetAllMessageSubscriptions() => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(string messageName, string? correlationKey, Guid? instanceId) => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(Guid instanceId) => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task AddMessageSubscription(MessageSubscription messageSubscription) => Task.CompletedTask;
        public Task RemoveProcessMessageSubscriptionsByProcessInstanceId(Guid instanceId)
        {
            storage.RemovedMessageSubscriptionInstanceIds.Add(instanceId);
            return Task.CompletedTask;
        }

        public Task RemoveAllProcessMessageSubscriptionsWithNoInstancedId(string metaDefinitionId) => Task.CompletedTask;
        public Task RemoveAllProcessSignalSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;

        public void AddSignalSubscription(SignalSubscription signalSubscription)
        {
        }

        public Task<IEnumerable<SignalSubscription>> GetSignalSubscriptions(Guid instanceId) => Task.FromResult(Enumerable.Empty<SignalSubscription>());

        public void RemoveProcessSingalSubscriptionsByProcessInstanceId(Guid instanceId)
        {
            storage.RemovedSignalSubscriptionInstanceIds.Add(instanceId);
        }

        public Task<IEnumerable<UserTaskSubscription>> GetAllUserTasks(Guid instanceId) =>
            Task.FromResult(storage.UserTaskSubscriptions.Where(subscription => subscription.ProcessInstanceId == instanceId).Cast<UserTaskSubscription>());

        public Task<IEnumerable<ExtendedUserTaskSubscription>> GetAllUserTasksExtended(Guid userId) =>
            Task.FromResult(storage.UserTaskSubscriptions.AsEnumerable());

        public Task AddUserTaskSubscription(UserTaskSubscription userTasks)
        {
            storage.AddedUserTaskSubscriptions.Add(userTasks);
            return Task.CompletedTask;
        }

        public Task RemoveUserTaskSubscription(Guid userTaskSubscriptionId) => Task.CompletedTask;

        public void RemoveAllUserTaskSubscriptionsByInstanceId(Guid instanceId)
        {
            storage.RemovedUserTaskSubscriptionInstanceIds.Add(instanceId);
        }

        public Task RemoveAllUserTaskSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
        public Task<IEnumerable<TimerSubscription>> GetAllTimerSubscriptions() => Task.FromResult(Enumerable.Empty<TimerSubscription>());
        public Task<IEnumerable<TimerSubscription>> GetTimerSubscriptions(Guid instanceId) => Task.FromResult(Enumerable.Empty<TimerSubscription>());
        public Task AddTimerSubscription(TimerSubscription timerSubscription) => Task.CompletedTask;
        public Task RemoveTimerSubscription(Guid timerSubscriptionId) => Task.CompletedTask;

        public Task RemoveProcessTimerSubscriptionsByProcessInstanceId(Guid instanceId)
        {
            storage.RemovedTimerSubscriptionInstanceIds.Add(instanceId);
            return Task.CompletedTask;
        }

        public Task RemoveAllProcessTimerSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
    }

    private sealed class TestInstanceStorage(TestStorage storage) : IInstanceStorage
    {
        public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId) =>
            Task.FromResult(storage.Instances.SingleOrDefault(instance => instance.InstanceId == processInstanceId)
                            ?? throw new FileNotFoundException($"Process instance {processInstanceId} was not found."));

        public Task AddOrUpdateInstance(ProcessInstanceInfo processInstanceInfo)
        {
            storage.Instances.RemoveAll(existing => existing.InstanceId == processInstanceInfo.InstanceId);
            storage.Instances.Add(processInstanceInfo);
            return Task.CompletedTask;
        }

        public Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances() => Task.FromResult(storage.Instances.Where(instance => !instance.IsFinished));
        public Task<IEnumerable<ProcessInstanceInfo>> GetAllInstances() => Task.FromResult(storage.Instances.AsEnumerable());
    }

    private sealed class TestFormStorage(TestStorage storage) : IFormStorage
    {
        public Task SaveFormMetaData(FormMetadata formMetadata) => throw new NotSupportedException();
        public Task<FormMetadata> GetFormMetaData(Guid formId) => throw new NotSupportedException();
        public Task<IEnumerable<FormMetadata>> GetFormMetadatas() => Task.FromResult(storage.FormMetadatas.AsEnumerable());
        public Task UpdateFormMetaData(FormMetadata formMetaData) => throw new NotSupportedException();
        public Task DeleteFormMetaData(Guid formId) => throw new NotSupportedException();
        public Task SaveForm(Form form) => throw new NotSupportedException();
        public Task<Form> GetForm(Guid id) => throw new NotSupportedException();
        public Task<IEnumerable<Form>> GetForms(Guid formId) => Task.FromResult(storage.Forms.Where(form => form.FormId == formId));
        public Task DeleteForm(Guid id) => throw new NotSupportedException();
        public Task<Model.Version> GetMaxVersion(Guid formId) => throw new NotSupportedException();
    }
}
