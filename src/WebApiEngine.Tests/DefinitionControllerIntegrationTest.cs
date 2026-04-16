using System.Net;
using System.Net.Http.Json;
using System.Text;
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

namespace WebApiEngine.Tests;

[NonParallelizable]
public class DefinitionControllerIntegrationTest
{
    // Testzweck: Prüft, dass für eine deployte Plain-Start-Definition direkt über die API eine Instanz gestartet und persistiert werden kann.
    [Test]
    public async Task StartInstance_ShouldCreatePersistedInstance_ForDeployedPlainStartDefinition()
    {
        const string definitionId = "workflow-ui-start";
        var deployedDefinitionGuid = Guid.NewGuid();
        var storage = TestStorage.Create();
        storage.DefinitionStorageSeed.MetaDefinitions.Add(new ExtendedBpmnMetaDefinition
        {
            DefinitionId = definitionId,
            Name = "Workflow UI Start"
        });
        storage.DefinitionStorageSeed.Definitions.Add(new BpmnDefinition
        {
            Id = deployedDefinitionGuid,
            DefinitionId = definitionId,
            Hash = "hash-start",
            SavedByUser = Guid.NewGuid(),
            SavedOn = DateTime.UtcNow,
            Version = new Model.Version(1, 0),
            IsActive = true,
            DeployedOn = DateTime.UtcNow
        });
        storage.DefinitionStorageSeed.Binaries[deployedDefinitionGuid] = CreatePlainStartXml(definitionId);

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/definition/meta/{definitionId}/instance", new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        payload.Result.Should().NotBeNull();
        payload.Result!.RelatedDefinitionId.Should().Be(definitionId);
        payload.Result.RelatedDefinitionName.Should().Be("Workflow UI Start");
        payload.Result.State.Should().Be(ProcessInstanceStateDto.Completed);
        storage.InstanceStorageSeed.Instances.Should().ContainKey(payload.Result.InstanceId);
    }

    // Testzweck: Prüft, dass UI-Direktstarts für rein nachrichtengetriebene Prozesse kontrolliert mit BadRequest abgelehnt werden.
    [Test]
    public async Task StartInstance_ShouldReturnBadRequest_WhenDefinitionHasNoPlainStartEvent()
    {
        const string definitionId = "workflow-message-start-only";
        var deployedDefinitionGuid = Guid.NewGuid();
        var storage = TestStorage.Create();
        storage.DefinitionStorageSeed.MetaDefinitions.Add(new ExtendedBpmnMetaDefinition
        {
            DefinitionId = definitionId,
            Name = "Message only"
        });
        storage.DefinitionStorageSeed.Definitions.Add(new BpmnDefinition
        {
            Id = deployedDefinitionGuid,
            DefinitionId = definitionId,
            Hash = "hash-message",
            SavedByUser = Guid.NewGuid(),
            SavedOn = DateTime.UtcNow,
            Version = new Model.Version(1, 0),
            IsActive = true,
            DeployedOn = DateTime.UtcNow
        });
        storage.DefinitionStorageSeed.Binaries[deployedDefinitionGuid] = CreateMessageStartXml(definitionId);

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/definition/meta/{definitionId}/instance", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("cannot be started directly from the UI");
    }

    // Testzweck: Prüft, dass ein fehlgeschlagener Deploy-Versuch keine halb persistierte Definitionsversion zurücklässt.
    [Test]
    public async Task DeployDefinition_ShouldCleanupStoredVersion_WhenSubscriptionSetupFails()
    {
        const string definitionId = "workflow-deploy-cleanup";
        var storage = TestStorage.Create(throwOnMessageSubscriptionAdd: true);

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/definition/deploy",
            new StringContent(CreateMessageStartXml(definitionId), Encoding.UTF8, "text/plain"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<BpmnDefinitionDto>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeFalse();
        payload.ErrorMessage.Should().Contain("message subscriptions");
        storage.DefinitionStorageSeed.Definitions.Should().BeEmpty();
        storage.DefinitionStorageSeed.Binaries.Should().BeEmpty();
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
                services.RemoveAll<ICurrentUserContextAccessor>();

                services.AddSingleton<IStorageSystem>(storage);
                services.AddSingleton<ITransactionalStorageProvider>(new TestTransactionalStorageProvider(storage));
                services.AddSingleton<ICurrentUserContextAccessor>(new StubCurrentUserContextAccessor());
            });
        }
    }

    private sealed class TestTransactionalStorageProvider(TestStorage storage) : ITransactionalStorageProvider
    {
        public ITransactionalStorage GetTransactionalStorage()
        {
            return storage;
        }
    }

    private sealed class StubCurrentUserContextAccessor : ICurrentUserContextAccessor
    {
        public CurrentUserContext GetCurrentUser()
        {
            return new CurrentUserContext(Guid.Parse("9AB0E5C5-A5B4-4F87-A857-EB821D12AF6E"), "test", false);
        }
    }

    private sealed class TestStorage : ITransactionalStorage
    {
        private readonly TestDefinitionStorage _definitionStorage;
        private readonly TestSubscriptionStorage _subscriptionStorage;
        private readonly TestInstanceStorage _instanceStorage;

        private TestStorage(
            TestDefinitionStorage definitionStorage,
            TestSubscriptionStorage subscriptionStorage,
            TestInstanceStorage instanceStorage)
        {
            _definitionStorage = definitionStorage;
            _subscriptionStorage = subscriptionStorage;
            _instanceStorage = instanceStorage;
            DefinitionStorageSeed = definitionStorage;
            SubscriptionStorageSeed = subscriptionStorage;
            InstanceStorageSeed = instanceStorage;
        }

        public static TestStorage Create(bool throwOnMessageSubscriptionAdd = false)
        {
            var definitionStorage = new TestDefinitionStorage();
            var subscriptionStorage = new TestSubscriptionStorage(throwOnMessageSubscriptionAdd);
            var instanceStorage = new TestInstanceStorage();

            return new TestStorage(definitionStorage, subscriptionStorage, instanceStorage);
        }

        public TestDefinitionStorage DefinitionStorageSeed { get; }
        public TestSubscriptionStorage SubscriptionStorageSeed { get; }
        public TestInstanceStorage InstanceStorageSeed { get; }

        public IDefinitionStorage DefinitionStorage => _definitionStorage;
        public IMessageSubscriptionStorage SubscriptionStorage => _subscriptionStorage;
        public IInstanceStorage InstanceStorage => _instanceStorage;
        public IFormStorage FormStorage { get; } = new NoOpFormStorage();

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

    private sealed class TestDefinitionStorage : IDefinitionStorage
    {
        public List<BpmnDefinition> Definitions { get; } = [];
        public Dictionary<Guid, string> Binaries { get; } = [];
        public List<ExtendedBpmnMetaDefinition> MetaDefinitions { get; } = [];

        public Task StoreBinary(Guid guid, string data)
        {
            Binaries[guid] = data;
            return Task.CompletedTask;
        }

        public Task<string> GetBinary(Guid guid)
        {
            return Task.FromResult(Binaries[guid]);
        }

        public Task DeleteBinary(Guid guid)
        {
            Binaries.Remove(guid);
            return Task.CompletedTask;
        }

        public Task<Guid[]> GetAllBinaryDefinitions()
        {
            return Task.FromResult(Binaries.Keys.ToArray());
        }

        public Task<BpmnDefinition[]> GetAllDefinitions()
        {
            return Task.FromResult(Definitions.ToArray());
        }

        public Task StoreDefinition(BpmnDefinition definition)
        {
            Definitions.RemoveAll(existing => existing.Id == definition.Id);
            Definitions.Add(definition);
            return Task.CompletedTask;
        }

        public Task DeleteDefinition(Guid id)
        {
            Definitions.RemoveAll(definition => definition.Id == id);
            return Task.CompletedTask;
        }

        public Task<Model.Version?> GetMaxVersionId(string modelId)
        {
            var versions = Definitions
                .Where(definition => definition.DefinitionId == modelId)
                .Select(definition => definition.Version)
                .ToArray();

            return Task.FromResult<Model.Version?>(versions.Length == 0 ? null : versions.Max());
        }

        public Task<BpmnDefinition> GetDefinitionById(Guid id)
        {
            return Task.FromResult(Definitions.Single(definition => definition.Id == id));
        }

        public Task<BpmnDefinition> GetLatestDefinition(string definitionId)
        {
            return Task.FromResult(Definitions
                .Where(definition => definition.DefinitionId == definitionId)
                .MaxBy(definition => definition.Version)!);
        }

        public Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId)
        {
            return Task.FromResult(Definitions.SingleOrDefault(definition =>
                definition.DefinitionId == definitionDefinitionId &&
                definition.IsActive));
        }

        public Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions()
        {
            return Task.FromResult(MetaDefinitions.ToArray());
        }

        public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition)
        {
            MetaDefinitions.RemoveAll(existing => existing.DefinitionId == metaDefinition.DefinitionId);
            MetaDefinitions.Add(new ExtendedBpmnMetaDefinition
            {
                DefinitionId = metaDefinition.DefinitionId,
                Name = metaDefinition.Name,
                Description = metaDefinition.Description
            });
            return Task.CompletedTask;
        }

        public Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition)
        {
            MetaDefinitions.RemoveAll(existing => existing.DefinitionId == metaDefinition.DefinitionId);
            MetaDefinitions.Add(new ExtendedBpmnMetaDefinition
            {
                DefinitionId = metaDefinition.DefinitionId,
                Name = metaDefinition.Name,
                Description = metaDefinition.Description
            });
            return Task.CompletedTask;
        }

        public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id)
        {
            var metadata = MetaDefinitions.Single(existing => existing.DefinitionId == id);
            return Task.FromResult<BpmnMetaDefinition>(new BpmnMetaDefinition
            {
                DefinitionId = metadata.DefinitionId,
                Name = metadata.Name,
                Description = metadata.Description
            });
        }
    }

    private sealed class TestSubscriptionStorage(bool throwOnMessageSubscriptionAdd) : IMessageSubscriptionStorage
    {
        public List<MessageSubscription> MessageSubscriptions { get; } = [];

        public Task<IEnumerable<MessageSubscription>> GetAllMessageSubscriptions() =>
            Task.FromResult(MessageSubscriptions.AsEnumerable());

        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(string messageName, string? correlationKey, Guid? messageInstanceId) =>
            Task.FromResult(Enumerable.Empty<MessageSubscription>());

        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(Guid instanceId) =>
            Task.FromResult(MessageSubscriptions.Where(subscription => subscription.ProcessInstanceId == instanceId).AsEnumerable());

        public Task AddMessageSubscription(MessageSubscription messageSubscription)
        {
            if (throwOnMessageSubscriptionAdd)
            {
                throw new InvalidOperationException("Adding message subscriptions failed.");
            }

            MessageSubscriptions.Add(messageSubscription);
            return Task.CompletedTask;
        }

        public Task RemoveProcessMessageSubscriptionsByProcessInstanceId(Guid instanceId)
        {
            MessageSubscriptions.RemoveAll(subscription => subscription.ProcessInstanceId == instanceId);
            return Task.CompletedTask;
        }

        public Task RemoveAllProcessMessageSubscriptionsWithNoInstancedId(string metaDefinitionId)
        {
            MessageSubscriptions.RemoveAll(subscription =>
                subscription.ProcessInstanceId == null &&
                subscription.RelatedDefinitionId == metaDefinitionId);
            return Task.CompletedTask;
        }

        public Task RemoveAllProcessSignalSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
        public void AddSignalSubscription(SignalSubscription signalSubscription) { }
        public Task<IEnumerable<SignalSubscription>> GetSignalSubscriptions(Guid instanceId) =>
            Task.FromResult(Enumerable.Empty<SignalSubscription>());
        public void RemoveProcessSingalSubscriptionsByProcessInstanceId(Guid instanceId) { }
        public Task<IEnumerable<UserTaskSubscription>> GetAllUserTasks(Guid instanceId) =>
            Task.FromResult(Enumerable.Empty<UserTaskSubscription>());
        public Task<IEnumerable<ExtendedUserTaskSubscription>> GetAllUserTasksExtended(Guid userId) =>
            Task.FromResult(Enumerable.Empty<ExtendedUserTaskSubscription>());
        public Task AddUserTaskSubscription(UserTaskSubscription userTasks) => Task.CompletedTask;
        public Task RemoveUserTaskSubscription(Guid userTaskSubscriptionId) => Task.CompletedTask;
        public void RemoveAllUserTaskSubscriptionsByInstanceId(Guid instanceId) { }
        public Task RemoveAllUserTaskSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
        public Task<IEnumerable<TimerSubscription>> GetAllTimerSubscriptions() =>
            Task.FromResult(Enumerable.Empty<TimerSubscription>());
        public Task<IEnumerable<TimerSubscription>> GetTimerSubscriptions(Guid instanceId) =>
            Task.FromResult(Enumerable.Empty<TimerSubscription>());
        public Task AddTimerSubscription(TimerSubscription timerSubscription) => Task.CompletedTask;
        public Task RemoveTimerSubscription(Guid timerSubscriptionId) => Task.CompletedTask;
        public Task RemoveProcessTimerSubscriptionsByProcessInstanceId(Guid instanceId) => Task.CompletedTask;
        public Task RemoveAllProcessTimerSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
    }

    private sealed class TestInstanceStorage : IInstanceStorage
    {
        public Dictionary<Guid, ProcessInstanceInfo> Instances { get; } = [];

        public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId)
        {
            return Task.FromResult(Instances[processInstanceId]);
        }

        public Task AddOrUpdateInstance(ProcessInstanceInfo processInstanceInfo)
        {
            Instances[processInstanceInfo.InstanceId] = processInstanceInfo;
            return Task.CompletedTask;
        }

        public Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances() =>
            Task.FromResult(Instances.Values.Where(instance => !instance.IsFinished).AsEnumerable());

        public Task<IEnumerable<ProcessInstanceInfo>> GetAllInstances() =>
            Task.FromResult(Instances.Values.AsEnumerable());
    }

    private sealed class NoOpFormStorage : IFormStorage
    {
        public Task SaveFormMetaData(FormMetadata formMetadata) => Task.CompletedTask;
        public Task<FormMetadata> GetFormMetaData(Guid formId) => throw new NotSupportedException();
        public Task<IEnumerable<FormMetadata>> GetFormMetadatas() => Task.FromResult(Enumerable.Empty<FormMetadata>());
        public Task UpdateFormMetaData(FormMetadata formMetaData) => Task.CompletedTask;
        public Task DeleteFormMetaData(Guid formId) => Task.CompletedTask;
        public Task SaveForm(Form form) => Task.CompletedTask;
        public Task<Form> GetForm(Guid id) => throw new NotSupportedException();
        public Task<IEnumerable<Form>> GetForms(Guid formId) => Task.FromResult(Enumerable.Empty<Form>());
        public Task DeleteForm(Guid id) => Task.CompletedTask;
        public Task<Model.Version> GetMaxVersion(Guid formId) => Task.FromResult(new Model.Version());
    }

    private static string CreatePlainStartXml(string definitionId)
    {
        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                  id="{definitionId}"
                                  targetNamespace="http://bpmn.io/schema/bpmn">
                  <bpmn:process id="Process_{definitionId}" isExecutable="true">
                    <bpmn:startEvent id="StartEvent_1">
                      <bpmn:outgoing>Flow_1</bpmn:outgoing>
                    </bpmn:startEvent>
                    <bpmn:endEvent id="EndEvent_1">
                      <bpmn:incoming>Flow_1</bpmn:incoming>
                    </bpmn:endEvent>
                    <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="EndEvent_1" />
                  </bpmn:process>
                  <bpmndi:BPMNDiagram id="BPMNDiagram_1">
                    <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="Process_{definitionId}">
                      <bpmndi:BPMNShape id="StartEvent_1_di" bpmnElement="StartEvent_1">
                        <dc:Bounds x="179" y="99" width="36" height="36" />
                      </bpmndi:BPMNShape>
                      <bpmndi:BPMNShape id="EndEvent_1_di" bpmnElement="EndEvent_1">
                        <dc:Bounds x="332" y="99" width="36" height="36" />
                      </bpmndi:BPMNShape>
                      <bpmndi:BPMNEdge id="Flow_1_di" bpmnElement="Flow_1">
                        <di:waypoint x="215" y="117" />
                        <di:waypoint x="332" y="117" />
                      </bpmndi:BPMNEdge>
                    </bpmndi:BPMNPlane>
                  </bpmndi:BPMNDiagram>
                </bpmn:definitions>
                """;
    }

    private static string CreateMessageStartXml(string definitionId)
    {
        return $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                                  id="{definitionId}"
                                  targetNamespace="http://bpmn.io/schema/bpmn">
                  <bpmn:message id="Message_1" name="StartMessage" />
                  <bpmn:process id="Process_{definitionId}" isExecutable="true">
                    <bpmn:startEvent id="StartEvent_Message">
                      <bpmn:outgoing>Flow_1</bpmn:outgoing>
                      <bpmn:messageEventDefinition id="MessageEventDefinition_1" messageRef="Message_1" />
                    </bpmn:startEvent>
                    <bpmn:endEvent id="EndEvent_1">
                      <bpmn:incoming>Flow_1</bpmn:incoming>
                    </bpmn:endEvent>
                    <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_Message" targetRef="EndEvent_1" />
                  </bpmn:process>
                  <bpmndi:BPMNDiagram id="BPMNDiagram_1">
                    <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="Process_{definitionId}">
                      <bpmndi:BPMNShape id="StartEvent_Message_di" bpmnElement="StartEvent_Message">
                        <dc:Bounds x="179" y="99" width="36" height="36" />
                      </bpmndi:BPMNShape>
                      <bpmndi:BPMNShape id="EndEvent_1_di" bpmnElement="EndEvent_1">
                        <dc:Bounds x="332" y="99" width="36" height="36" />
                      </bpmndi:BPMNShape>
                      <bpmndi:BPMNEdge id="Flow_1_di" bpmnElement="Flow_1">
                        <di:waypoint x="215" y="117" />
                        <di:waypoint x="332" y="117" />
                      </bpmndi:BPMNEdge>
                    </bpmndi:BPMNPlane>
                  </bpmndi:BPMNDiagram>
                </bpmn:definitions>
                """;
    }
}
