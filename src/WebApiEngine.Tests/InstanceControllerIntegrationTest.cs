using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Model;
using StorageSystem;
using WebApiEngine.Shared;
using BpmnServiceTask = BPMN.Activities.ServiceTask;

namespace WebApiEngine.Tests;

public class InstanceControllerIntegrationTest
{
    // Testzweck: Deckt den Fall „Get Signal Subscriptions Should Return Subscriptions For Instance“ ab.
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

    // Testzweck: Deckt den Fall „Get Service Subscriptions Should Return Active Service Task Tokens“ ab.
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
        payload.Result.Should().OnlyContain(token => token.State == FlowNodeStateDto.Active);
    }

    // Testzweck: Deckt den Fall „Get Timer Subscriptions Should Return Timers For Instance“ ab.
    [Test]
    public async Task GetTimerSubscriptions_ShouldReturnTimersForInstance()
    {
        var instanceId = Guid.NewGuid();
        var storage = TestStorage.CreateWithInstance(instanceId);
        storage.SubscriptionStorageSeed.TimerSubscriptions.Add(new TimerSubscription
        {
            DueAt = DateTime.UtcNow.AddMinutes(3),
            FlowNodeId = "TimerCatch_1",
            Kind = TimerSubscriptionKind.IntermediateCatchEvent,
            ProcessId = "Process_Invoice",
            RelatedDefinitionId = "invoice-process",
            DefinitionId = storage.DefinitionId,
            ProcessInstanceId = instanceId,
            TokenId = Guid.NewGuid()
        });

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/instance/{instanceId}/subscription/timers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<TimerSubscriptionDto[]>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        payload.Result.Should().ContainSingle();
        payload.Result![0].FlowNodeId.Should().Be("TimerCatch_1");
        payload.Result[0].ProcessInstanceId.Should().Be(instanceId);
        payload.Result[0].Kind.Should().Be(nameof(TimerSubscriptionKind.IntermediateCatchEvent));
    }

    // Testzweck: Deckt den Fall „Get All Instances Should Include Finished Instances For Done And Error Filters“ ab.
    [Test]
    public async Task GetAllInstances_ShouldIncludeFinishedInstances_ForDoneAndErrorFilters()
    {
        var activeInstanceId = Guid.NewGuid();
        var completedInstanceId = Guid.NewGuid();
        var failedInstanceId = Guid.NewGuid();
        var storage = TestStorage.CreateWithInstances(activeInstanceId, completedInstanceId, failedInstanceId);

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/instance");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<List<ProcessInstanceInfoDto>>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        payload.Result.Should().NotBeNull();
        payload.Result!.Select(instance => instance.InstanceId).Should().BeEquivalentTo(
            [activeInstanceId, completedInstanceId, failedInstanceId]);
        payload.Result.Should().Contain(instance => instance.State == ProcessInstanceStateDto.Completed);
        payload.Result.Should().Contain(instance => instance.State == ProcessInstanceStateDto.Failed);
    }

    /// <summary>
    /// Kleine Test-Factory, die die produktiven Storage-Services durch einen in-memory Test-Store ersetzt.
    /// Damit werden die echten Controller, das Routing und die Serialisierung gegen einen realen Testserver geprüft.
    /// </summary>
    private sealed class TestWebApplicationFactory(TestStorage storage) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
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

            var completedServiceTaskToken = new Token
            {
                ProcessInstanceId = instanceId,
                CurrentBaseElement = new BpmnServiceTask
                {
                    Id = "Activity_ServiceTask_Completed",
                    Name = "Completed notification",
                    Implementation = "notify-accounting-completed"
                },
                ActiveBoundaryEvents = [],
                State = FlowNodeState.Completed
            };

            var instanceStorage = new TestInstanceStorage(
                new ProcessInstanceInfo
                {
                    InstanceId = instanceId,
                    metaDefinitionId = "invoice-process",
                    DefinitionId = definitionId,
                    ProcessId = "Process_Invoice",
                    Tokens = [serviceTaskToken, completedServiceTaskToken],
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

        public static TestStorage CreateWithInstances(Guid activeInstanceId, Guid completedInstanceId, Guid failedInstanceId)
        {
            var definitionId = Guid.NewGuid();
            var definitionStorage = new TestDefinitionStorage(
                new BpmnMetaDefinition
                {
                    DefinitionId = "invoice-process",
                    Name = "Invoice Process"
                });

            var instanceStorage = new TestInstanceStorage(
                CreateProcessInstance(activeInstanceId, definitionId, ProcessInstanceState.Running, false),
                CreateProcessInstance(completedInstanceId, definitionId, ProcessInstanceState.Completed, true),
                CreateProcessInstance(failedInstanceId, definitionId, ProcessInstanceState.Failed, true));

            var subscriptionStorage = new TestMessageSubscriptionStorage();
            return new TestStorage(definitionId, definitionStorage, subscriptionStorage, instanceStorage);
        }

        private static ProcessInstanceInfo CreateProcessInstance(
            Guid instanceId,
            Guid definitionId,
            ProcessInstanceState state,
            bool isFinished)
        {
            return new ProcessInstanceInfo
            {
                InstanceId = instanceId,
                metaDefinitionId = "invoice-process",
                DefinitionId = definitionId,
                ProcessId = "Process_Invoice",
                Tokens = [],
                IsFinished = isFinished,
                State = state,
                MessageSubscriptionCount = 0,
                SignalSubscriptionCount = 0,
                UserTaskSubscriptionCount = 0,
                ServiceSubscriptionCount = 0
            };
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
        public List<TimerSubscription> TimerSubscriptions { get; } = [];

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

        public Task<IEnumerable<TimerSubscription>> GetAllTimerSubscriptions() =>
            Task.FromResult(TimerSubscriptions.AsEnumerable());

        public Task<IEnumerable<TimerSubscription>> GetTimerSubscriptions(Guid instanceId) =>
            Task.FromResult(TimerSubscriptions.Where(subscription => subscription.ProcessInstanceId == instanceId).AsEnumerable());

        public Task AddTimerSubscription(TimerSubscription timerSubscription)
        {
            TimerSubscriptions.Add(timerSubscription);
            return Task.CompletedTask;
        }

        public Task RemoveTimerSubscription(Guid timerSubscriptionId)
        {
            TimerSubscriptions.RemoveAll(subscription => subscription.Id == timerSubscriptionId);
            return Task.CompletedTask;
        }

        public Task RemoveProcessTimerSubscriptionsByProcessInstanceId(Guid instanceId)
        {
            TimerSubscriptions.RemoveAll(subscription => subscription.ProcessInstanceId == instanceId);
            return Task.CompletedTask;
        }

        public Task RemoveAllProcessTimerSubscriptionsWithNoInstanceId(string relatedDefinitionId)
        {
            TimerSubscriptions.RemoveAll(subscription =>
                subscription.ProcessInstanceId == null &&
                string.Equals(subscription.RelatedDefinitionId, relatedDefinitionId, StringComparison.Ordinal));
            return Task.CompletedTask;
        }
    }

    private sealed class TestInstanceStorage(params ProcessInstanceInfo[] instances) : IInstanceStorage
    {
        private readonly Dictionary<Guid, ProcessInstanceInfo> _instances = instances.ToDictionary(instance => instance.InstanceId);

        public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId)
        {
            return _instances.TryGetValue(processInstanceId, out var instance)
                ? Task.FromResult(instance)
                : throw new KeyNotFoundException($"Unknown process instance: {processInstanceId}");
        }

        public Task AddOrUpdateInstance(ProcessInstanceInfo processInstanceInfo) => Task.CompletedTask;

        public Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances() =>
            Task.FromResult(_instances.Values.Where(instance => !instance.IsFinished).AsEnumerable());

        public Task<IEnumerable<ProcessInstanceInfo>> GetAllInstances() =>
            Task.FromResult(_instances.Values.AsEnumerable());
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
