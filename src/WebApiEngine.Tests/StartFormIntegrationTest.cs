using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Model;
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Das Startformular am Startereignis: Abruf über <c>GET /definition/meta/{id}/start-form</c>
/// und Start mit den ausgefüllten Werten über <c>POST /definition/meta/{id}/instance</c>.
/// </summary>
[NonParallelizable]
public class StartFormIntegrationTest
{
    private const string DefinitionId = "urlaub";

    // Testzweck: Ohne Startformular antwortet der Abruf mit 204. Die Konsole soll daran
    // erkennen, dass sie den Workflow ohne Dialog starten kann.
    [Test]
    public async Task GetStartForm_ShouldReturnNoContent_WhenTheWorkflowHasNoStartForm()
    {
        var storage = TestStorage.Create();
        SeedWorkflow(storage, startFormKey: null);

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/definition/meta/{DefinitionId}/start-form");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // Testzweck: Ein Form-Key ohne Version liefert die neueste Fassung des Bestandsformulars.
    [Test]
    public async Task GetStartForm_ShouldReturnTheLatestVersion_ForAStoredForm()
    {
        var storage = TestStorage.Create();
        SeedWorkflow(storage, startFormKey: "Urlaubsantrag");
        SeedStoredForm(storage, "Urlaubsantrag", ("1.0", "{\"version\":\"1.0\"}"), ("1.1", "{\"version\":\"1.1\"}"));

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/definition/meta/{DefinitionId}/start-form");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload!.Successful.Should().BeTrue();
        payload.Result!.FormData.Should().Be("{\"version\":\"1.1\"}");
    }

    // Testzweck: Ein Form-Key mit Version liefert genau diese Fassung — ein Workflow soll sein
    // Startformular festhalten können, auch wenn der Bestand weiterzieht.
    [Test]
    public async Task GetStartForm_ShouldReturnThePinnedVersion_WhenTheFormKeyNamesOne()
    {
        var storage = TestStorage.Create();
        SeedWorkflow(storage, startFormKey: "Urlaubsantrag:1.0");
        SeedStoredForm(storage, "Urlaubsantrag", ("1.0", "{\"version\":\"1.0\"}"), ("1.1", "{\"version\":\"1.1\"}"));

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/definition/meta/{DefinitionId}/start-form");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload!.Result!.FormData.Should().Be("{\"version\":\"1.0\"}");
    }

    // Testzweck: Ein Form-Key darf die Kennung des Bestandsformulars nennen. Der Modeler legt
    // sie als `formId` ab; wird sie nur als Name gesucht, antwortet der Abruf 400, obwohl das
    // Formular existiert.
    [Test]
    public async Task GetStartForm_ShouldReturnTheForm_WhenTheFormKeyIsItsId()
    {
        var storage = TestStorage.Create();
        var formId = SeedStoredForm(storage, "Urlaubsantrag", ("1.0", "{\"version\":\"1.0\"}"), ("1.1", "{\"version\":\"1.1\"}"));
        SeedWorkflow(storage, startFormKey: formId.ToString());

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/definition/meta/{DefinitionId}/start-form");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload!.Successful.Should().BeTrue();
        payload.Result!.FormData.Should().Be("{\"version\":\"1.1\"}");
    }

    // Testzweck: Auch hinter einer Kennung darf eine Version stehen — sonst hinge die
    // Bedeutung des Suffix daran, ob der Modeler Name oder Kennung geschrieben hat.
    [Test]
    public async Task GetStartForm_ShouldReturnThePinnedVersion_WhenTheFormKeyIsAnIdWithVersion()
    {
        var storage = TestStorage.Create();
        var formId = SeedStoredForm(storage, "Urlaubsantrag", ("1.0", "{\"version\":\"1.0\"}"), ("1.1", "{\"version\":\"1.1\"}"));
        SeedWorkflow(storage, startFormKey: $"{formId}:1.0");

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/definition/meta/{DefinitionId}/start-form");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload!.Result!.FormData.Should().Be("{\"version\":\"1.0\"}");
    }

    // Testzweck: Eine Kennung, zu der es kein Formular gibt, wird als fachlicher Fehler
    // gemeldet und nicht als leeres Formular.
    [Test]
    public async Task GetStartForm_ShouldReturnBadRequest_ForAnUnknownFormId()
    {
        var unbekannt = Guid.NewGuid();
        var storage = TestStorage.Create();
        SeedWorkflow(storage, startFormKey: unbekannt.ToString());

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/definition/meta/{DefinitionId}/start-form");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain(unbekannt.ToString());
    }

    // Testzweck: Ein im Workflow eingebettetes Formular kommt aus dem Diagramm der deployten
    // Version — es steht in keinem Bestand und wäre sonst unerreichbar.
    [Test]
    public async Task GetStartForm_ShouldReturnTheEmbeddedForm_FromTheDeployedDiagram()
    {
        var storage = TestStorage.Create();
        SeedWorkflow(storage, startFormKey: "camunda-forms:bpmn:StartForm_Urlaub", embeddedFormId: "StartForm_Urlaub");

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/definition/meta/{DefinitionId}/start-form");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload!.Result!.FormData.Should().Be(EmbeddedSchema);
        payload.Result.Version.Should().BeNull();
    }

    // Testzweck: Ein Form-Key, der auf kein Formular zeigt, wird als fachlicher Fehler
    // gemeldet — sonst öffnete die Konsole einen leeren Dialog.
    [Test]
    public async Task GetStartForm_ShouldReturnBadRequest_ForAnUnknownFormName()
    {
        var storage = TestStorage.Create();
        SeedWorkflow(storage, startFormKey: "GibtEsNicht");

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/definition/meta/{DefinitionId}/start-form");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("GibtEsNicht");
    }

    // Testzweck: Ein Prozess mit mehreren Direkteinstiegen wird abgelehnt, statt eines davon
    // zu raten. Welches Startformular gälte, wäre sonst Zufall.
    [Test]
    public async Task GetStartForm_ShouldReturnBadRequest_WhenTheProcessHasSeveralStartEntries()
    {
        var storage = TestStorage.Create();
        SeedWorkflow(storage, startFormKey: "Urlaubsantrag", secondStartFormKey: "Krankmeldung");

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/definition/meta/{DefinitionId}/start-form");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("multiple plain start entries");
    }

    // Testzweck: Ein unbekannter Workflow ist kein leeres Formular, sondern 404.
    [Test]
    public async Task GetStartForm_ShouldReturnNotFound_ForAnUnknownWorkflow()
    {
        var storage = TestStorage.Create();

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/definition/meta/gibt-es-nicht/start-form");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload!.Successful.Should().BeFalse();
    }

    // Testzweck: Ohne deployte Version gibt es kein Startformular — dieselbe Ablehnung wie
    // beim Start, damit die Konsole nicht erst ein Formular zeigt und dann am Start scheitert.
    [Test]
    public async Task GetStartForm_ShouldReturnBadRequest_WhenNoVersionIsDeployed()
    {
        var storage = TestStorage.Create();
        storage.DefinitionStorageSeed.Metas.Add(new ExtendedBpmnMetaDefinition
        {
            DefinitionId = DefinitionId,
            Name = "Urlaubsantrag"
        });

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/definition/meta/{DefinitionId}/start-form");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<FormDto>>();
        payload!.ErrorMessage.Should().Contain("No deployed definition");
    }

    // Testzweck: Die Werte aus dem Startformular werden die Startvariablen der Instanz.
    [Test]
    public async Task StartInstance_ShouldKeepTheSentVariablesAsInstanceVariables()
    {
        var storage = TestStorage.Create();
        SeedWorkflow(storage, startFormKey: "Urlaubsantrag");
        SeedStoredForm(storage, "Urlaubsantrag", ("1.0", "{}"));

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        // Bewusst als roher JSON-Rumpf: Ein anonymes Objekt wuerde der Testserializer
        // camelCase schreiben und damit den Namen der Variablen aendern.
        var response = await client.PostAsync(
            $"/definition/meta/{DefinitionId}/instance",
            new StringContent("""{"variables":{"Antragsteller":"Christian"}}""", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>();
        payload!.Successful.Should().BeTrue();
        payload.Result!.Tokens
            .Select(token => VariableValue(token, "Antragsteller"))
            .Should().Contain("Christian");
    }

    // Testzweck: Hat der Workflow ein Startformular, ist ein Start ohne Werte ein Fehler —
    // sonst liefe die Instanz ohne die Angaben los, die sie fachlich braucht.
    [Test]
    public async Task StartInstance_ShouldReturnBadRequest_WhenTheStartFormIsNotFilledIn()
    {
        var storage = TestStorage.Create();
        SeedWorkflow(storage, startFormKey: "Urlaubsantrag");

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/definition/meta/{DefinitionId}/instance", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("requires its start form");
    }

    // Testzweck: Ein leeres Wertobjekt ist eine Antwort und wird angenommen. Ein Formular kann
    // ausschliesslich aus bedingt sichtbaren Feldern bestehen; der Server prüft die
    // Pflichtfelder bewusst nicht.
    [Test]
    public async Task StartInstance_ShouldAcceptAnEmptyVariablesObject_WhenTheWorkflowHasAStartForm()
    {
        var storage = TestStorage.Create();
        SeedWorkflow(storage, startFormKey: "Urlaubsantrag");

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            $"/definition/meta/{DefinitionId}/instance",
            new { variables = new { } });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Ein Workflow ohne Startformular startet weiterhin ohne Rumpf. Bestehende
    // Aufrufer dürfen von dieser Erweiterung nichts merken.
    [Test]
    public async Task StartInstance_ShouldStillWorkWithoutABody_WhenTheWorkflowHasNoStartForm()
    {
        var storage = TestStorage.Create();
        SeedWorkflow(storage, startFormKey: null);

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"/definition/meta/{DefinitionId}/instance", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private const string EmbeddedSchema = """{"display":"form","components":[]}""";

    private static string? VariableValue(TokenDto token, string name)
    {
        var source = (IDictionary<string, object?>?)token.Variables ?? token.OutputData;
        return source is not null && source.TryGetValue(name, out var value) ? value?.ToString() : null;
    }

    /// <summary>Legt ein Bestandsformular an und liefert dessen Kennung — der Form-Key darf sie
    /// statt des Namens nennen.</summary>
    private static Guid SeedStoredForm(TestStorage storage, string name, params (string Version, string Data)[] versions)
    {
        var formId = Guid.NewGuid();
        storage.FormStorageSeed.FormMetadatas.Add(new FormMetadata { FormId = formId, Name = name });
        foreach (var (version, data) in versions)
        {
            storage.FormStorageSeed.Forms.Add(new Form
            {
                Id = Guid.NewGuid(),
                FormId = formId,
                Version = Model.Version.FromString(version),
                FormData = data
            });
        }

        return formId;
    }

    private static void SeedWorkflow(
        TestStorage storage,
        string? startFormKey,
        string? embeddedFormId = null,
        string? secondStartFormKey = null)
    {
        var definitionGuid = Guid.NewGuid();
        storage.DefinitionStorageSeed.Metas.Add(new ExtendedBpmnMetaDefinition
        {
            DefinitionId = DefinitionId,
            Name = "Urlaubsantrag"
        });
        storage.DefinitionStorageSeed.Deployed[DefinitionId] = new BpmnDefinition
        {
            Id = definitionGuid,
            DefinitionId = DefinitionId,
            Hash = "hash",
            SavedByUser = Guid.NewGuid(),
            IsActive = true,
            Version = new Model.Version(1, 0)
        };
        storage.DefinitionStorageSeed.Binaries[definitionGuid] =
            BuildWorkflowXml(startFormKey, embeddedFormId, secondStartFormKey);
    }

    private static string BuildWorkflowXml(string? startFormKey, string? embeddedFormId, string? secondStartFormKey)
    {
        var processExtensions = embeddedFormId is null
            ? string.Empty
            : $"""
                   <bpmn:extensionElements>
                     <zeebe:userTaskForm id="{embeddedFormId}">{EmbeddedSchema}</zeebe:userTaskForm>
                   </bpmn:extensionElements>
               """;

        var secondStart = secondStartFormKey is null
            ? string.Empty
            : $"""
                   <bpmn:startEvent id="StartEvent_2">
                     <bpmn:extensionElements>
                       <zeebe:formDefinition formKey="{secondStartFormKey}" />
                     </bpmn:extensionElements>
                   </bpmn:startEvent>
               """;

        var startExtensions = startFormKey is null
            ? string.Empty
            : $"""
                     <bpmn:extensionElements>
                       <zeebe:formDefinition formKey="{startFormKey}" />
                     </bpmn:extensionElements>
               """;

        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                                  id="{DefinitionId}" targetNamespace="http://bpmn.io/schema/bpmn">
                  <bpmn:process id="Process_Urlaub" isExecutable="true">
                {processExtensions}
                    <bpmn:startEvent id="StartEvent_1">
                {startExtensions}
                      <bpmn:outgoing>Flow_1</bpmn:outgoing>
                    </bpmn:startEvent>
                {secondStart}
                    <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="EndEvent_1" />
                    <bpmn:endEvent id="EndEvent_1">
                      <bpmn:incoming>Flow_1</bpmn:incoming>
                    </bpmn:endEvent>
                  </bpmn:process>
                </bpmn:definitions>
                """;
    }

    private sealed class TestWebApplicationFactory(TestStorage storage) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
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
            });
        }
    }

    private sealed class TestTransactionalStorageProvider(TestStorage storage) : ITransactionalStorageProvider
    {
        public ITransactionalStorage GetTransactionalStorage() => storage;
    }

    private sealed class TestStorage(TestFormStorage formStorage) : ITransactionalStorage
    {
        public static TestStorage Create() => new(new TestFormStorage());

        public TestFormStorage FormStorageSeed { get; } = formStorage;
        public SeedDefinitionStorage DefinitionStorageSeed { get; } = new();
        public IDefinitionStorage DefinitionStorage => DefinitionStorageSeed;
        public IMessageSubscriptionStorage SubscriptionStorage { get; } = new NoOpMessageSubscriptionStorage();
        public CollectingInstanceStorage InstanceStorageSeed { get; } = new();
        public IInstanceStorage InstanceStorage => InstanceStorageSeed;
        public IFormStorage FormStorage { get; } = formStorage;
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

    private sealed class TestFormStorage : IFormStorage
    {
        public List<FormMetadata> FormMetadatas { get; } = [];
        public List<Form> Forms { get; } = [];

        public Task SaveFormMetaData(FormMetadata formMetadata)
        {
            FormMetadatas.Add(formMetadata);
            return Task.CompletedTask;
        }

        public Task<FormMetadata> GetFormMetaData(Guid formId) =>
            Task.FromResult(FormMetadatas.Single(metadata => metadata.FormId == formId));

        public Task<IEnumerable<FormMetadata>> GetFormMetadatas() => Task.FromResult(FormMetadatas.AsEnumerable());

        public Task UpdateFormMetaData(FormMetadata formMetaData) => Task.CompletedTask;

        public Task DeleteFormMetaData(Guid formId)
        {
            FormMetadatas.RemoveAll(metadata => metadata.FormId == formId);
            return Task.CompletedTask;
        }

        public Task SaveForm(Form form)
        {
            Forms.Add(form);
            return Task.CompletedTask;
        }

        public Task<Form> GetForm(Guid id) => Task.FromResult(Forms.Single(form => form.Id == id));

        public Task<IEnumerable<Form>> GetForms(Guid formId) =>
            Task.FromResult(Forms.Where(form => form.FormId == formId).AsEnumerable());

        public Task DeleteForm(Guid id)
        {
            Forms.RemoveAll(form => form.Id == id);
            return Task.CompletedTask;
        }

        public Task<Model.Version> GetMaxVersion(Guid formId) =>
            Task.FromResult(Forms.Where(form => form.FormId == formId)
                .Select(form => form.Version)
                .DefaultIfEmpty(new Model.Version())
                .Max()!);
    }

    private sealed class SeedDefinitionStorage : IDefinitionStorage
    {
        public List<ExtendedBpmnMetaDefinition> Metas { get; } = [];
        public Dictionary<string, BpmnDefinition> Deployed { get; } = [];
        public Dictionary<Guid, string> Binaries { get; } = [];

        public Task<string> GetBinary(Guid guid) => Task.FromResult(Binaries[guid]);
        public Task<Guid[]> GetAllBinaryDefinitions() => Task.FromResult(Binaries.Keys.ToArray());
        public Task<BpmnDefinition[]> GetAllDefinitions() => Task.FromResult(Deployed.Values.ToArray());
        public Task StoreDefinition(BpmnDefinition definition) => Task.CompletedTask;
        public Task StoreBinary(Guid guid, string data) => Task.CompletedTask;
        public Task DeleteBinary(Guid guid) => Task.CompletedTask;
        public Task DeleteDefinition(Guid id) => Task.CompletedTask;
        public Task<Model.Version?> GetMaxVersionId(string modelId) => Task.FromResult<Model.Version?>(null);
        public Task<BpmnDefinition> GetDefinitionById(Guid id) => throw new NotSupportedException();
        public Task<BpmnDefinition> GetLatestDefinition(string definitionId) => throw new NotSupportedException();

        public Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId) =>
            Task.FromResult(Deployed.GetValueOrDefault(definitionDefinitionId));

        public Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions() => Task.FromResult(Metas.ToArray());
        public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition) => Task.CompletedTask;
        public Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition) => Task.CompletedTask;
        public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id) => throw new NotSupportedException();
    }

    private sealed class NoOpMessageSubscriptionStorage : IMessageSubscriptionStorage
    {
        public Task<IEnumerable<MessageSubscription>> GetAllMessageSubscriptions() => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(string messageName, string? correlationKey, Guid? messageInstanceId) => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(Guid instanceId) => Task.FromResult(Enumerable.Empty<MessageSubscription>());
        public Task AddMessageSubscription(MessageSubscription messageSubscription) => Task.CompletedTask;
        public Task RemoveProcessMessageSubscriptionsByProcessInstanceId(Guid instanceId) => Task.CompletedTask;
        public Task RemoveAllProcessMessageSubscriptionsWithNoInstancedId(string metaDefinitionId) => Task.CompletedTask;
        public Task RemoveAllProcessSignalSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
        public void AddSignalSubscription(SignalSubscription signalSubscription) { }
        public Task<IEnumerable<SignalSubscription>> GetSignalSubscriptions(Guid instanceId) => Task.FromResult(Enumerable.Empty<SignalSubscription>());
        public void RemoveProcessSingalSubscriptionsByProcessInstanceId(Guid instanceId) { }
        public Task<IEnumerable<UserTaskSubscription>> GetAllUserTasks(Guid instanceId) => Task.FromResult(Enumerable.Empty<UserTaskSubscription>());
        public Task<IEnumerable<ExtendedUserTaskSubscription>> GetAllUserTasksExtended(Guid userId) => Task.FromResult(Enumerable.Empty<ExtendedUserTaskSubscription>());
        public Task AddUserTaskSubscription(UserTaskSubscription userTasks) => Task.CompletedTask;
        public Task RemoveUserTaskSubscription(Guid userTaskSubscriptionId) => Task.CompletedTask;
        public void RemoveAllUserTaskSubscriptionsByInstanceId(Guid instanceId) { }
        public Task RemoveAllUserTaskSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
        public Task<IEnumerable<TimerSubscription>> GetAllTimerSubscriptions() => Task.FromResult(Enumerable.Empty<TimerSubscription>());
        public Task<IEnumerable<TimerSubscription>> GetTimerSubscriptions(Guid instanceId) => Task.FromResult(Enumerable.Empty<TimerSubscription>());
        public Task AddTimerSubscription(TimerSubscription timerSubscription) => Task.CompletedTask;
        public Task RemoveTimerSubscription(Guid timerSubscriptionId) => Task.CompletedTask;
        public Task RemoveProcessTimerSubscriptionsByProcessInstanceId(Guid instanceId) => Task.CompletedTask;
        public Task RemoveAllProcessTimerSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
    }

    private sealed class CollectingInstanceStorage : IInstanceStorage
    {
        public Dictionary<Guid, ProcessInstanceInfo> Instances { get; } = [];

        public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId) =>
            Task.FromResult(Instances[processInstanceId]);

        public Task AddOrUpdateInstance(ProcessInstanceInfo processInstance)
        {
            Instances[processInstance.InstanceId] = processInstance;
            return Task.CompletedTask;
        }

        public Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances() =>
            Task.FromResult(Instances.Values.Where(instance => !instance.IsFinished));

        public Task<IEnumerable<ProcessInstanceInfo>> GetAllInstances() =>
            Task.FromResult(Instances.Values.AsEnumerable());
    }
}
