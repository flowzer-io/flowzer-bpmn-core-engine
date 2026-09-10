using System.Dynamic;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Model;
using PostgreSqlStorageSystem;
using StorageSystem;
using WebApiEngine.Ai;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    private static readonly Guid EngineConnectionId = Guid.Parse("118adeb6-65a4-4e57-a03b-d3b0a3300ac9");

    // Testzweck: Ein absichtlich vor dem PostgreSQL-Commit abgebrochener Engine-Durchgang
    // rollt Result-Claim und Instanzfortschritt gemeinsam zurueck. Ein zweiter Prozess kann
    // dasselbe Ergebnis danach genau einmal sicher uebernehmen.
    [Test]
    public async Task AiEngine_ShouldRollbackAndThenCompleteReadyResultExactlyOnce()
    {
        var provider = new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema);
        var business = new BpmnBusinessLogic(provider, aiSecretStore: new ReadyAiSecretStore());
        var definition = await SeedAiWorkflowAsync(provider, business);
        ExpandoObject input = new();
        ((IDictionary<string, object?>)input)["request"] = "Please classify";
        var instance = await business.StartProcessInstance(definition.DefinitionId, input);
        // Erzwingt eine Genauigkeit unterhalb der von PostgreSQL gespeicherten Mikrosekunde.
        // Damit bleibt der Regressionstest unabhängig von der zufälligen Systemuhr-Auflösung.
        var now = new DateTime((DateTime.UtcNow.Ticks / 10 * 10) + 1, DateTimeKind.Utc);
        await MakePostgreSqlAiResultReadyAsync(now);
        var time = new FakeTimeProvider(new DateTimeOffset(now.AddMinutes(1)));
        var policy = AiEnginePolicy();

        var failing = new BpmnBusinessLogic(
            new FailingCommitStorageProvider(provider),
            aiSecretStore: new ReadyAiSecretStore());
        var failedCommit = () => failing.CompleteAiRunBatchAsync(time, policy, default);
        await failedCommit.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Simulated commit failure.");

        var afterRollback = new PostgreSqlStorage(_dataSource!, Schema);
        (await afterRollback.AiRunStorage.List()).Single().Status.Should().Be(AiRunStatus.ResultReady);
        (await afterRollback.InstanceStorage.GetProcessInstance(instance.InstanceId)).State
            .Should().Be(ProcessInstanceState.Waiting);

        var first = new BpmnBusinessLogic(provider, aiSecretStore: new ReadyAiSecretStore());
        var second = new BpmnBusinessLogic(provider, aiSecretStore: new ReadyAiSecretStore());
        var results = await Task.WhenAll(
            first.CompleteAiRunBatchAsync(time, policy, default),
            second.CompleteAiRunBatchAsync(time, policy, default));

        results.Sum(result => result.Claimed).Should().Be(1);
        results.Sum(result => result.Completed).Should().Be(1);
        (await afterRollback.AiRunStorage.List()).Single().Status.Should().Be(AiRunStatus.Completed);
        var completed = await afterRollback.InstanceStorage.GetProcessInstance(instance.InstanceId);
        completed.State.Should().Be(ProcessInstanceState.Completed);
        var master = completed.Tokens.Single(token => token.ParentTokenId is null);
        ((IDictionary<string, object?>)master.Variables!)["classification"].Should().Be("support");
    }

    private async Task<BpmnDefinition> SeedAiWorkflowAsync(
        PostgreSqlTransactionalStorageProvider provider,
        BpmnBusinessLogic business)
    {
        var definition = CreateDefinition("Definitions_AiEngine", 1, 0, isActive: false);
        using (var storage = provider.GetTransactionalStorage())
        {
            await storage.AiConnectionStorage.TryCreate(CreateAiConnection(
                EngineConnectionId,
                "AI engine",
                1,
                "env:FLOWZER_AI_ENGINE"));
            await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            {
                DefinitionId = definition.DefinitionId,
                Name = "AI engine"
            });
            await storage.DefinitionStorage.StoreDefinition(definition);
            await storage.DefinitionStorage.StoreBinary(definition.Id, AiEngineXml);
            storage.CommitChanges();
        }
        await business.DeployDefinition(definition);
        return definition;
    }

    private async Task MakePostgreSqlAiResultReadyAsync(DateTime now)
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var claimed = (await storage.AiRunStorage.ClaimProviderRuns(
            "provider",
            now,
            now.AddMinutes(5),
            1)).Single();
        var started = claimed with
        {
            Attempt = 1,
            ProviderCallStartedAtUtc = now.AddSeconds(1),
            Revision = claimed.Revision + 1,
            UpdatedAtUtc = now.AddSeconds(1)
        };
        var startResult = await storage.AiRunStorage.TryUpdate(
            started,
            claimed.Revision,
            "provider",
            now.AddSeconds(1));
        startResult.Status.Should().Be(AiRunWriteStatus.Written);
        var persistedStarted = startResult.Current!;
        var ready = persistedStarted with
        {
            Status = AiRunStatus.ResultReady,
            LeaseOwner = null,
            LeaseExpiresAtUtc = null,
            OutputJson = "{\"category\":\"support\"}",
            ResultModel = "provider-model",
            InputTokens = 10,
            OutputTokens = 2,
            TotalTokens = 12,
            Revision = persistedStarted.Revision + 1,
            UpdatedAtUtc = now.AddSeconds(2)
        };
        (await storage.AiRunStorage.TryUpdate(
            ready,
            persistedStarted.Revision,
            "provider",
            now.AddSeconds(2)))
            .Status.Should().Be(AiRunWriteStatus.Written);
    }

    private static AiRunExecutionPolicy AiEnginePolicy() => new(
        Enabled: true,
        PollInterval: TimeSpan.FromSeconds(5),
        BatchSize: 1,
        LeaseDuration: TimeSpan.FromMinutes(5),
        HeartbeatInterval: TimeSpan.FromSeconds(30),
        RetryBaseDelay: TimeSpan.FromSeconds(30),
        MaximumRetryDelay: TimeSpan.FromMinutes(10));

    private sealed class ReadyAiSecretStore : IAiSecretStore
    {
        public ValueTask<bool> ExistsAsync(string reference, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(true);
        public ValueTask<AiSecretValue?> ResolveAsync(string reference, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The engine integration test never calls a provider.");
    }

    private sealed class FailingCommitStorageProvider(ITransactionalStorageProvider inner)
        : ITransactionalStorageProvider
    {
        public ITransactionalStorage GetTransactionalStorage() =>
            new FailingCommitStorage(inner.GetTransactionalStorage());
    }

    private sealed class FailingCommitStorage(ITransactionalStorage inner) : ITransactionalStorage
    {
        public IDefinitionStorage DefinitionStorage => inner.DefinitionStorage;
        public IFolderStorage FolderStorage => inner.FolderStorage;
        public IMessageSubscriptionStorage SubscriptionStorage => inner.SubscriptionStorage;
        public IInstanceStorage InstanceStorage => inner.InstanceStorage;
        public IFormStorage FormStorage => inner.FormStorage;
        public IFormAuthoringStorage FormAuthoringStorage => inner.FormAuthoringStorage;
        public IFormSectionStorage FormSectionStorage => inner.FormSectionStorage;
        public IServiceTaskStorage ServiceTaskStorage => inner.ServiceTaskStorage;
        public IIdempotencyStorage IdempotencyStorage => inner.IdempotencyStorage;
        public IIdentityDirectoryStorage IdentityDirectoryStorage => inner.IdentityDirectoryStorage;
        public IUserTaskDraftStorage UserTaskDraftStorage => inner.UserTaskDraftStorage;
        public IUserTaskLifecycleStorage UserTaskLifecycleStorage => inner.UserTaskLifecycleStorage;
        public IUserTaskDeadlineStorage UserTaskDeadlineStorage => inner.UserTaskDeadlineStorage;
        public IUserTaskNotificationStorage UserTaskNotificationStorage => inner.UserTaskNotificationStorage;
        public IRuntimeNodeEventStorage RuntimeNodeEventStorage => inner.RuntimeNodeEventStorage;
        public IAiConnectionStorage AiConnectionStorage => inner.AiConnectionStorage;
        public IAiRunStorage AiRunStorage => inner.AiRunStorage;
        public void CommitChanges() => throw new InvalidOperationException("Simulated commit failure.");
        public void RollbackTransaction() => inner.RollbackTransaction();
        public void Dispose() => inner.Dispose();
    }

    private const string AiEngineXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                          xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"
                          id="Definitions_AiEngine" targetNamespace="test">
          <bpmn:process id="Process_AiEngine" isExecutable="true">
            <bpmn:startEvent id="Start"><bpmn:outgoing>ToAi</bpmn:outgoing></bpmn:startEvent>
            <bpmn:sequenceFlow id="ToAi" sourceRef="Start" targetRef="Ai_1" />
            <bpmn:serviceTask id="Ai_1">
              <bpmn:extensionElements>
                <zeebe:taskDefinition type="flowzer.ai.v1" retries="2" />
                <flowzer:aiTask contractVersion="1" connectionId="118adeb6-65a4-4e57-a03b-d3b0a3300ac9"
                    instructionVersion="1" maxInputTokens="4096" maxOutputTokens="1024" timeoutSeconds="60">
                  <flowzer:instruction>Classify the request.</flowzer:instruction>
                  <flowzer:resultSchema>{"type":"object","properties":{"category":{"type":"string"}},"required":["category"],"additionalProperties":false}</flowzer:resultSchema>
                </flowzer:aiTask>
                <zeebe:ioMapping><zeebe:input source="=request" target="request" /><zeebe:output source="=category" target="classification" /></zeebe:ioMapping>
              </bpmn:extensionElements>
              <bpmn:incoming>ToAi</bpmn:incoming><bpmn:outgoing>ToEnd</bpmn:outgoing>
            </bpmn:serviceTask>
            <bpmn:sequenceFlow id="ToEnd" sourceRef="Ai_1" targetRef="End" />
            <bpmn:endEvent id="End"><bpmn:incoming>ToEnd</bpmn:incoming></bpmn:endEvent>
          </bpmn:process>
        </bpmn:definitions>
        """;
}
