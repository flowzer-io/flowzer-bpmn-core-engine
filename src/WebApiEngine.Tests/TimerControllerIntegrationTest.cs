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

namespace WebApiEngine.Tests;

[NonParallelizable]
public class TimerControllerIntegrationTest
{
    [Test]
    public async Task GetAllTimers_ShouldReturnDefinitionAndInstanceScopedTimerSubscriptions()
    {
        var definitionId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var storage = new TestStorage();
        storage.SubscriptionStorageSeed.TimerSubscriptions.AddRange(
        [
            new TimerSubscription
            {
                DueAt = DateTime.UtcNow.AddMinutes(1),
                FlowNodeId = "StartEvent_Timer",
                Kind = TimerSubscriptionKind.ProcessStartEvent,
                ProcessId = "Process_Start",
                RelatedDefinitionId = "definition-start",
                DefinitionId = definitionId,
                RemainingOccurrences = 3
            },
            new TimerSubscription
            {
                DueAt = DateTime.UtcNow.AddMinutes(2),
                FlowNodeId = "TimerCatch_1",
                Kind = TimerSubscriptionKind.IntermediateCatchEvent,
                ProcessId = "Process_Catch",
                RelatedDefinitionId = "definition-catch",
                DefinitionId = definitionId,
                ProcessInstanceId = instanceId,
                TokenId = Guid.NewGuid()
            }
        ]);

        await using var factory = new TestWebApplicationFactory(storage);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/timer");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<TimerSubscriptionDto[]>>();
        payload.Should().NotBeNull();
        payload!.Successful.Should().BeTrue();
        payload.Result.Should().HaveCount(2);
        payload.Result!.Should().Contain(subscription => subscription.Kind == nameof(TimerSubscriptionKind.ProcessStartEvent));
        payload.Result.Should().Contain(subscription => subscription.RemainingOccurrences == 3);
        payload.Result.Should().Contain(subscription => subscription.ProcessInstanceId == instanceId);
    }

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

    private sealed class TestStorage : ITransactionalStorage
    {
        public TestTimerSubscriptionStorage SubscriptionStorageSeed { get; } = new();

        public IDefinitionStorage DefinitionStorage { get; } = new NoOpDefinitionStorage();
        public IMessageSubscriptionStorage SubscriptionStorage => SubscriptionStorageSeed;
        public IInstanceStorage InstanceStorage { get; } = new NoOpInstanceStorage();
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

    private sealed class TestTimerSubscriptionStorage : IMessageSubscriptionStorage
    {
        public List<TimerSubscription> TimerSubscriptions { get; } = [];

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
        public Task<IEnumerable<TimerSubscription>> GetAllTimerSubscriptions() => Task.FromResult(TimerSubscriptions.AsEnumerable());
        public Task<IEnumerable<TimerSubscription>> GetTimerSubscriptions(Guid instanceId) => Task.FromResult(TimerSubscriptions.Where(subscription => subscription.ProcessInstanceId == instanceId).AsEnumerable());

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
            TimerSubscriptions.RemoveAll(subscription => subscription.ProcessInstanceId == null && subscription.RelatedDefinitionId == relatedDefinitionId);
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpDefinitionStorage : IDefinitionStorage
    {
        public Task StoreBinary(Guid guid, string data) => Task.CompletedTask;
        public Task<string> GetBinary(Guid guid) => throw new NotSupportedException();
        public Task<Guid[]> GetAllBinaryDefinitions() => Task.FromResult(Array.Empty<Guid>());
        public Task<BpmnDefinition[]> GetAllDefinitions() => Task.FromResult(Array.Empty<BpmnDefinition>());
        public Task StoreDefinition(BpmnDefinition definition) => Task.CompletedTask;
        public Task<Model.Version?> GetMaxVersionId(string modelId) => Task.FromResult<Model.Version?>(null);
        public Task<BpmnDefinition> GetDefinitionById(Guid id) => throw new NotSupportedException();
        public Task<BpmnDefinition> GetLatestDefinition(string definitionId) => throw new NotSupportedException();
        public Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId) => Task.FromResult<BpmnDefinition?>(null);
        public Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions() => Task.FromResult(Array.Empty<ExtendedBpmnMetaDefinition>());
        public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition) => Task.CompletedTask;
        public Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition) => Task.CompletedTask;
        public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id) => throw new NotSupportedException();
    }

    private sealed class NoOpInstanceStorage : IInstanceStorage
    {
        public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId) => throw new NotSupportedException();
        public Task AddOrUpdateInstance(ProcessInstanceInfo processInstanceInfo) => Task.CompletedTask;
        public Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances() => Task.FromResult(Enumerable.Empty<ProcessInstanceInfo>());
        public Task<IEnumerable<ProcessInstanceInfo>> GetAllInstances() => Task.FromResult(Enumerable.Empty<ProcessInstanceInfo>());
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
}
