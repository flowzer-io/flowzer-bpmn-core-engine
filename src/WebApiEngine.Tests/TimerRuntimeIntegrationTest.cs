using BPMN.Process;
using core_engine;
using core_engine.Extensions;
using FluentAssertions;
using Model;
using StorageSystem;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

[NonParallelizable]
public class TimerRuntimeIntegrationTest
{
    [Test]
    public async Task DeployDefinition_ShouldPersistProcessStartTimerSubscription()
    {
        var definition = CreateDefinition();
        var storage = new TimerRuntimeTestStorage();
        storage.Definitions[definition.Id] = definition;
        storage.Binaries[definition.Id] = CreateTimerStartXml();

        var businessLogic = new BpmnBusinessLogic(new TestTransactionalStorageProvider(storage));

        var beforeDeploy = DateTime.UtcNow;
        await businessLogic.DeployDefinition(definition);
        var afterDeploy = DateTime.UtcNow;

        storage.TimerSubscriptions.Should().ContainSingle();
        var timerSubscription = storage.TimerSubscriptions.Single();
        timerSubscription.ProcessInstanceId.Should().BeNull();
        timerSubscription.Kind.Should().Be(TimerSubscriptionKind.ProcessStartEvent);
        timerSubscription.FlowNodeId.Should().Be("StartEvent_Timer");
        timerSubscription.ProcessId.Should().Be("Process_TimerStart");
        timerSubscription.RelatedDefinitionId.Should().Be(definition.DefinitionId);
        timerSubscription.DefinitionId.Should().Be(definition.Id);
        timerSubscription.DueAt.Should().BeAfter(beforeDeploy.AddSeconds(1));
        timerSubscription.DueAt.Should().BeBefore(afterDeploy.AddSeconds(3));

        definition.IsActive.Should().BeTrue();
        definition.DeployedOn.Should().NotBeNull();
    }

    [Test]
    public async Task HandleTime_ShouldStartDueDefinitionTimer_AndRemoveStartSubscription()
    {
        var definition = CreateDefinition();
        var storage = new TimerRuntimeTestStorage();
        storage.Definitions[definition.Id] = definition;
        storage.Binaries[definition.Id] = CreateTimerStartXml();

        var businessLogic = new BpmnBusinessLogic(new TestTransactionalStorageProvider(storage));
        await businessLogic.DeployDefinition(definition);

        var processedTimers = await businessLogic.HandleTime(DateTime.UtcNow.AddSeconds(5));

        processedTimers.Should().Be(1);
        storage.TimerSubscriptions.Should().BeEmpty();
        storage.Instances.Should().ContainSingle();
        storage.Instances.Values.Single().State.Should().Be(ProcessInstanceState.Completed);
        storage.Instances.Values.Single().IsFinished.Should().BeTrue();
    }

    [Test]
    public async Task HandleTime_ShouldAdvanceDueInstanceTimer_AndClearPersistedInstanceTimer()
    {
        var definitionId = Guid.NewGuid();
        var process = ParseSingleProcess(CreateIntermediateTimerXml());
        var instance = new ProcessEngine(process).StartProcess();
        instance.InstanceId = Guid.NewGuid();

        var storage = new TimerRuntimeTestStorage();
        storage.Definitions[definitionId] = new BpmnDefinition
        {
            Id = definitionId,
            DefinitionId = "definition-timer-catch",
            Hash = "hash",
            SavedByUser = Guid.NewGuid(),
            SavedOn = DateTime.UtcNow,
            Version = new Model.Version(1, 0),
            IsActive = true
        };

        storage.Instances[instance.InstanceId] = new ProcessInstanceInfo
        {
            InstanceId = instance.InstanceId,
            metaDefinitionId = "definition-timer-catch",
            DefinitionId = definitionId,
            ProcessId = process.Id,
            Tokens = instance.Tokens,
            IsFinished = instance.IsFinished,
            State = instance.State,
            MessageSubscriptionCount = 0,
            SignalSubscriptionCount = 0,
            UserTaskSubscriptionCount = 0,
            ServiceSubscriptionCount = 0
        };

        var timerDescriptor = ((ICatchHandler)instance).ActiveTimerSubscriptions.Should().ContainSingle().Subject;
        storage.TimerSubscriptions.Add(new TimerSubscription
        {
            DueAt = timerDescriptor.DueAt,
            FlowNodeId = timerDescriptor.FlowNodeId,
            Kind = timerDescriptor.Kind,
            ProcessId = process.Id,
            RelatedDefinitionId = "definition-timer-catch",
            DefinitionId = definitionId,
            ProcessInstanceId = instance.InstanceId,
            TokenId = timerDescriptor.TokenId
        });

        var businessLogic = new BpmnBusinessLogic(new TestTransactionalStorageProvider(storage));
        var processedTimers = await businessLogic.HandleTime(timerDescriptor.DueAt.AddMilliseconds(50));

        processedTimers.Should().Be(1);
        storage.TimerSubscriptions.Should().BeEmpty();
        storage.Instances[instance.InstanceId].State.Should().Be(ProcessInstanceState.Completed);
        storage.Instances[instance.InstanceId].IsFinished.Should().BeTrue();
    }

    private static BpmnDefinition CreateDefinition()
    {
        return new BpmnDefinition
        {
            Id = Guid.NewGuid(),
            DefinitionId = "definition-timer-start",
            Hash = "hash",
            SavedByUser = Guid.NewGuid(),
            SavedOn = DateTime.UtcNow,
            Version = new Model.Version(1, 0),
            IsActive = false
        };
    }

    private static Process ParseSingleProcess(string xml)
    {
        return ModelParser.ParseModel(xml).GetProcesses().Single();
    }

    private static string CreateTimerStartXml()
    {
        return """
               <?xml version="1.0" encoding="UTF-8"?>
               <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                 id="Definitions_TimerStart"
                                 targetNamespace="http://bpmn.io/schema/bpmn">
                 <bpmn:process id="Process_TimerStart" isExecutable="true">
                   <bpmn:startEvent id="StartEvent_Timer">
                     <bpmn:outgoing>Flow_1</bpmn:outgoing>
                     <bpmn:timerEventDefinition id="TimerDefinition_1">
                       <bpmn:timeDuration xsi:type="bpmn:tFormalExpression" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">PT2S</bpmn:timeDuration>
                     </bpmn:timerEventDefinition>
                   </bpmn:startEvent>
                   <bpmn:endEvent id="EndEvent_1">
                     <bpmn:incoming>Flow_1</bpmn:incoming>
                   </bpmn:endEvent>
                   <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_Timer" targetRef="EndEvent_1" />
                 </bpmn:process>
               </bpmn:definitions>
               """;
    }

    private static string CreateIntermediateTimerXml()
    {
        return """
               <?xml version="1.0" encoding="UTF-8"?>
               <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                                 id="Definitions_TimerCatch"
                                 targetNamespace="http://bpmn.io/schema/bpmn">
                 <bpmn:process id="Process_TimerCatch" isExecutable="true">
                   <bpmn:startEvent id="StartEvent_1">
                     <bpmn:outgoing>Flow_1</bpmn:outgoing>
                   </bpmn:startEvent>
                   <bpmn:intermediateCatchEvent id="TimerCatch_1">
                     <bpmn:incoming>Flow_1</bpmn:incoming>
                     <bpmn:outgoing>Flow_2</bpmn:outgoing>
                     <bpmn:timerEventDefinition id="TimerDefinition_1">
                       <bpmn:timeDuration xsi:type="bpmn:tFormalExpression" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">PT2S</bpmn:timeDuration>
                     </bpmn:timerEventDefinition>
                   </bpmn:intermediateCatchEvent>
                   <bpmn:endEvent id="EndEvent_1">
                     <bpmn:incoming>Flow_2</bpmn:incoming>
                   </bpmn:endEvent>
                   <bpmn:sequenceFlow id="Flow_1" sourceRef="StartEvent_1" targetRef="TimerCatch_1" />
                   <bpmn:sequenceFlow id="Flow_2" sourceRef="TimerCatch_1" targetRef="EndEvent_1" />
                 </bpmn:process>
               </bpmn:definitions>
               """;
    }

    private sealed class TestTransactionalStorageProvider(TimerRuntimeTestStorage storage) : ITransactionalStorageProvider
    {
        public ITransactionalStorage GetTransactionalStorage()
        {
            return storage;
        }
    }

    private sealed class TimerRuntimeTestStorage : ITransactionalStorage
    {
        public Dictionary<Guid, BpmnDefinition> Definitions { get; } = [];
        public Dictionary<Guid, string> Binaries { get; } = [];
        public Dictionary<Guid, ProcessInstanceInfo> Instances { get; } = [];
        public List<TimerSubscription> TimerSubscriptions { get; } = [];

        public IDefinitionStorage DefinitionStorage => new TestDefinitionStorage(this);
        public IMessageSubscriptionStorage SubscriptionStorage => new TestSubscriptionStorage(this);
        public IInstanceStorage InstanceStorage => new TestInstanceStorage(this);
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

    private sealed class TestDefinitionStorage(TimerRuntimeTestStorage storage) : IDefinitionStorage
    {
        public Task StoreBinary(Guid guid, string data)
        {
            storage.Binaries[guid] = data;
            return Task.CompletedTask;
        }

        public Task<string> GetBinary(Guid guid)
        {
            return storage.Binaries.TryGetValue(guid, out var data)
                ? Task.FromResult(data)
                : throw new FileNotFoundException($"Binary definition {guid} was not found.");
        }

        public Task<Guid[]> GetAllBinaryDefinitions() => Task.FromResult(storage.Binaries.Keys.ToArray());
        public Task<BpmnDefinition[]> GetAllDefinitions() => Task.FromResult(storage.Definitions.Values.ToArray());

        public Task StoreDefinition(BpmnDefinition definition)
        {
            storage.Definitions[definition.Id] = definition;
            return Task.CompletedTask;
        }

        public Task<Model.Version?> GetMaxVersionId(string modelId)
        {
            var version = storage.Definitions.Values
                .Where(definition => definition.DefinitionId == modelId)
                .Select(definition => definition.Version)
                .OrderByDescending(definition => definition)
                .Cast<Model.Version?>()
                .FirstOrDefault();
            return Task.FromResult(version);
        }

        public Task<BpmnDefinition> GetDefinitionById(Guid id)
        {
            return storage.Definitions.TryGetValue(id, out var definition)
                ? Task.FromResult(definition)
                : throw new FileNotFoundException($"Definition {id} was not found.");
        }

        public Task<BpmnDefinition> GetLatestDefinition(string definitionId)
        {
            var definition = storage.Definitions.Values
                .Where(candidate => candidate.DefinitionId == definitionId)
                .OrderByDescending(candidate => candidate.Version)
                .FirstOrDefault()
                ?? throw new FileNotFoundException($"Definition {definitionId} was not found.");
            return Task.FromResult(definition);
        }

        public Task<BpmnDefinition?> GetDeployedDefinition(string definitionDefinitionId)
        {
            var definition = storage.Definitions.Values
                .SingleOrDefault(candidate => candidate.DefinitionId == definitionDefinitionId && candidate.IsActive);
            return Task.FromResult(definition);
        }

        public Task<ExtendedBpmnMetaDefinition[]> GetAllMetaDefinitions() => Task.FromResult(Array.Empty<ExtendedBpmnMetaDefinition>());
        public Task StoreMetaDefinition(BpmnMetaDefinition metaDefinition) => Task.CompletedTask;
        public Task UpdateMetaDefinition(BpmnMetaDefinition metaDefinition) => Task.CompletedTask;
        public Task<BpmnMetaDefinition> GetMetaDefinitionById(string id) => throw new FileNotFoundException($"Meta definition {id} was not found.");
    }

    private sealed class TestSubscriptionStorage(TimerRuntimeTestStorage storage) : IMessageSubscriptionStorage
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

        public Task<IEnumerable<TimerSubscription>> GetAllTimerSubscriptions() => Task.FromResult(storage.TimerSubscriptions.AsEnumerable());

        public Task<IEnumerable<TimerSubscription>> GetTimerSubscriptions(Guid instanceId)
        {
            return Task.FromResult(storage.TimerSubscriptions
                .Where(subscription => subscription.ProcessInstanceId == instanceId)
                .AsEnumerable());
        }

        public Task AddTimerSubscription(TimerSubscription timerSubscription)
        {
            storage.TimerSubscriptions.RemoveAll(existing => existing.Id == timerSubscription.Id);
            storage.TimerSubscriptions.Add(timerSubscription);
            return Task.CompletedTask;
        }

        public Task RemoveTimerSubscription(Guid timerSubscriptionId)
        {
            storage.TimerSubscriptions.RemoveAll(subscription => subscription.Id == timerSubscriptionId);
            return Task.CompletedTask;
        }

        public Task RemoveProcessTimerSubscriptionsByProcessInstanceId(Guid instanceId)
        {
            storage.TimerSubscriptions.RemoveAll(subscription => subscription.ProcessInstanceId == instanceId);
            return Task.CompletedTask;
        }

        public Task RemoveAllProcessTimerSubscriptionsWithNoInstanceId(string relatedDefinitionId)
        {
            storage.TimerSubscriptions.RemoveAll(subscription =>
                string.Equals(subscription.RelatedDefinitionId, relatedDefinitionId, StringComparison.Ordinal) &&
                subscription.ProcessInstanceId == null);
            return Task.CompletedTask;
        }
    }

    private sealed class TestInstanceStorage(TimerRuntimeTestStorage storage) : IInstanceStorage
    {
        public Task<ProcessInstanceInfo> GetProcessInstance(Guid processInstanceId)
        {
            return storage.Instances.TryGetValue(processInstanceId, out var instance)
                ? Task.FromResult(instance)
                : throw new FileNotFoundException($"Process instance {processInstanceId} was not found.");
        }

        public Task AddOrUpdateInstance(ProcessInstanceInfo processInstanceInfo)
        {
            storage.Instances[processInstanceInfo.InstanceId] = processInstanceInfo;
            return Task.CompletedTask;
        }

        public Task<IEnumerable<ProcessInstanceInfo>> GetAllActiveInstances() =>
            Task.FromResult(storage.Instances.Values.Where(instance => !instance.IsFinished).AsEnumerable());

        public Task<IEnumerable<ProcessInstanceInfo>> GetAllInstances() =>
            Task.FromResult(storage.Instances.Values.AsEnumerable());
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
