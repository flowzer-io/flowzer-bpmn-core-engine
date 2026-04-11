using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Model;
using StorageSystem;
using WebApiEngine.Shared;
using BpmnServiceTask = BPMN.Activities.ServiceTask;

namespace WebApiEngine.Tests;

public class InstanceControllerIntegrationTest
{
    [Test]
    public async Task GetSignalSubscriptions_ShouldReturnSubscriptionsForInstance()
    {
        var instanceId = Guid.NewGuid();
        var storage = TestStorage.CreateWithInstance(instanceId);
        storage.SubscriptionStorageSeed.SignalSubscriptions.Add(
            new SignalSubscription(
                "InvoiceReceived",
                "Process_Invoice",
                "invoice-process",
                storage.DefinitionId,
                instanceId));

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/instance/{instanceId}/subscription/signals");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<SignalSubscriptionDto[]>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        payload.Result.Should().ContainSingle();
        payload.Result![0].Signal.Should().Be("InvoiceReceived");
        payload.Result[0].ProcessInstanceId.Should().Be(instanceId);
    }

    [Test]
    public async Task GetServiceSubscriptions_ShouldReturnActiveServiceTaskTokens()
    {
        var instanceId = Guid.NewGuid();
        var storage = TestStorage.CreateWithInstance(instanceId);

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/instance/{instanceId}/subscription/services");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<TokenDto[]>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        payload.Result.Should().ContainSingle();
        payload.Result![0].CurrentFlowNodeId.Should().Be("Activity_ServiceTask");
        payload.Result[0].State.Should().Be(FlowNodeStateDto.Active);
    }

    /// <summary>
    /// Kleine Test-Factory, die die produktiven Storage-Services durch einen in-memory Test-Store ersetzt.
    /// Damit werden die echten Controller, das Routing und die Serialisierung gegen einen realen Testserver geprüft.
    /// </summary>
    private sealed class TestWebApplicationFactory(TestStorage storage) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
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
        public ITransactionalStorage GetTransactionalStorage()
        {
            return storage;
        }
    }

    /// <summary>
    /// In-memory Test-Storage für API-Integrationstests. Nur die tatsächlich benötigten Pfade sind implementiert.
    /// Nicht genutzte Methoden werfen bewusst, damit ein Test schnell zeigt, wenn sich die API-Abhängigkeiten ändern.
    /// </summary>
    private sealed class TestStorage : ITransactionalStorage
    {
        public Guid DefinitionId { get; }
        public TestDefinitionStorage DefinitionStorageSeed { get; }
        public TestMessageSubscriptionStorage SubscriptionStorageSeed { get; }
        public TestInstanceStorage InstanceStorageSeed { get; }

        private TestStorage(
            Guid definitionId,
            TestDefinitionStorage definitionStorage,
            TestMessageSubscriptionStorage subscriptionStorage,
            TestInstanceStorage instanceStorage)
        {
            DefinitionId = definitionId;
            DefinitionStorageSeed = definitionStorage;
            SubscriptionStorageSeed = subscriptionStorage;
            InstanceStorageSeed = instanceStorage;
        }

        public static TestStorage CreateWithInstance(Guid instanceId)
        {
            var definitionId = Guid.NewGuid();
            var definitionStorage = new TestDefinitionStorage(
                new BpmnMetaDefinition
                {
                    DefinitionId = "invoice-process",
                    Name = "Invoice Process"
                });

            var serviceTaskToken = new Token
            {
                ProcessInstanceId = instanceId,
                CurrentBaseElement = new BpmnServiceTask
                {
                    Id = "Activity_ServiceTask",
                    Name = "Notify accounting",
                    Implementation = "notify-accounting"
                },
                ActiveBoundaryEvents = [],
                State = FlowNodeState.Active
            };

            var instanceStorage = new TestInstanceStorage(
                new ProcessInstanceInfo
                {
                    InstanceId = instanceId,
                    metaDefinitionId = "invoice-process",
                    DefinitionId = definitionId,
                    ProcessId = "Process_Invoice",
                    Tokens = [serviceTaskToken],
                    IsFinished = false,
                    State = ProcessInstanceState.Running,
                    MessageSubscriptionCount = 0,
                    SignalSubscriptionCount = 1,
                    UserTaskSubscriptionCount = 0,
                    ServiceSubscriptionCount = 1
                });

            var subscriptionStorage = new TestMessageSubscriptionStorage();
            var storage = new TestStorage(definitionId, definitionStorage, subscriptionStorage, instanceStorage);

            return storage;
        }

        public IDefinitionStorage DefinitionStorage => DefinitionStorageSeed;
        public IMessageSubscriptionStorage SubscriptionStorage => SubscriptionStorageSeed;
        public IInstanceStorage InstanceStorage => InstanceStorageSeed;
        public IFormStorage FormStorage { get; } = new TestFormStorage();

        public void CommitChanges()
        {
        }

        public void RollbackTransaction()
        {
        }

        public void Dispose()
        {
        }

        private TestStorage()
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestDefinitionStorage(BpmnMetaDefinition metaDefinition) : IDefinitionStorage
    {
        public Task<string> GetBinary(Guid guid) => throw new NotSupportedException();
        public Task<Guid[]> GetAllBinaryDefinitions() => Task.FromResult(Array.Empty<Guid>());
        public Task<BpmnDefinition[]> GetAllDefinitions() => Task.FromResult(Array.Empty<BpmnDefinition>());
        public Task StoreDefinition(BpmnDefinition definition) => Task.CompletedTask;
        public Task StoreBinary(Guid guid, string data) => Task.CompletedTask;
        public Task<Model.Version?> GetMaxVersionId(string modelId) => Task.FromResult<Model.Version?>(null);
        public Task<BpmnDefinition> GetDefinitionById(Guid id) => throw new NotSupportedException();
        public Task<BpmnDefinition> GetLatestDefinition(string definitionId) => throw new NotSupportedException();
        public Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId) => Task.FromResult<BpmnDefinition?>(null);
        public Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions() => Task.FromResult(Array.Empty<ExtendedBpmnMetaDefinition>());
        public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition) => Task.CompletedTask;
        public Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition) => Task.CompletedTask;
        public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id) => Task.FromResult(metaDefinition);
    }

    private sealed class TestMessageSubscriptionStorage : IMessageSubscriptionStorage
    {
        public List<SignalSubscription> SignalSubscriptions { get; } = [];

        public Task<IEnumerable<MessageSubscription>> GetAllMessageSubscriptions() =>
            Task.FromResult(Enumerable.Empty<MessageSubscription>());

        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(string messageName, string? correlationKey, Guid? messageInstanceId) =>
            Task.FromResult(Enumerable.Empty<MessageSubscription>());

        public Task<IEnumerable<MessageSubscription>> GetMessageSubscription(Guid instanceId) =>
            Task.FromResult(Enumerable.Empty<MessageSubscription>());

        public Task AddMessageSubscription(MessageSubscription messageSubscription) => Task.CompletedTask;
        public Task RemoveProcessMessageSubscriptionsByProcessInstanceId(Guid instanceId) => Task.CompletedTask;
        public Task RemoveAllProcessMessageSubscriptionsWithNoInstancedId(string metaDefinitionId) => Task.CompletedTask;
        public Task RemoveAllProcessSignalSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
        public void AddSignalSubscription(SignalSubscription signalSubscription) => SignalSubscriptions.Add(signalSubscription);

        public Task<IEnumerable<SignalSubscription>> GetSignalSubscriptions(Guid instanceId)
        {
            return Task.FromResult(
                SignalSubscriptions
                    .Where(subscription => subscription.ProcessInstanceId == instanceId)
                    .AsEnumerable());
        }

        public void RemoveProcessSingalSubscriptionsByProcessInstanceId(Guid instanceId)
        {
            SignalSubscriptions.RemoveAll(subscription => subscription.ProcessInstanceId == instanceId);
        }

        public Task<IEnumerable<UserTaskSubscription>> GetAllUserTasks(Guid instanceId) =>
            Task.FromResult(Enumerable.Empty<UserTaskSubscription>());

        public Task<IEnumerable<ExtendedUserTaskSubscription>> GetAllUserTasksExtended(Guid userId) =>
            Task.FromResult(Enumerable.Empty<ExtendedUserTaskSubscription>());

        public Task AddUserTaskSubscription(UserTaskSubscription userTasks) => Task.CompletedTask;
        public Task RemoveUserTaskSubscription(Guid userTaskSubscriptionId) => Task.CompletedTask;
        public void RemoveAllUserTaskSubscriptionsByInstanceId(Guid instanceId) { }
        public Task RemoveAllUserTaskSubscriptionsWithNoInstanceId(string relatedDefinitionId) => Task.CompletedTask;
    }

    private sealed class TestInstanceStorage(ProcessInstanceInfo instance) : IInstanceStorage
    {
        public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId)
        {
            return processInstanceId == instance.InstanceId
                ? Task.FromResult(instance)
                : throw new KeyNotFoundException($"Unknown process instance: {processInstanceId}");
        }

        public Task AddOrUpdateInstance(ProcessInstanceInfo processInstanceInfo) => Task.CompletedTask;

        public Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances() =>
            Task.FromResult<IEnumerable<ProcessInstanceInfo>>([instance]);
    }

    private sealed class TestFormStorage : IFormStorage
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
        public Task<Model.Version> GetMaxVersion(Guid formId) => Task.FromResult(new Model.Version(1, 0));
    }
}
