using FluentAssertions;
using PostgreSqlStorageSystem;
using Model;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Idempotency;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Zwei API-Prozesse mit getrennten Engine-Sperren reservieren denselben
    // Startschlüssel in PostgreSQL atomar; nur eine Instanz wird committed.
    [Test]
    public async Task ConcurrentStart_ShouldCommitOneInstanceAndReplayItsId()
    {
        var provider = new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema);
        var definition = CreateDefinition("Definitions_Idempotent", 1, 0, isActive: false);
        using (var storage = provider.GetTransactionalStorage())
        {
            await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition { DefinitionId = definition.DefinitionId, Name = "Idempotent" });
            await storage.DefinitionStorage.StoreDefinition(definition);
            await storage.DefinitionStorage.StoreBinary(definition.Id, EndXml);
            storage.CommitChanges();
        }
        await new BpmnBusinessLogic(provider).DeployDefinition(definition);
        var request = Request('a', 'b', "workflow-start");
        var first = new BpmnBusinessLogic(provider);
        var second = new BpmnBusinessLogic(provider);

        var results = await Task.WhenAll(
            first.StartProcessInstance(definition.DefinitionId, idempotency: request),
            second.StartProcessInstance(definition.DefinitionId, idempotency: request));

        results[0].InstanceId.Should().Be(results[1].InstanceId);
        var reader = new PostgreSqlStorage(_dataSource!, Schema);
        (await reader.InstanceStorage.GetAllInstances()).Should().ContainSingle();
    }

    // Testzweck: Parallele identische Abschlüsse über getrennte Engine-Objekte führen
    // genau eine Mutation aus; der wartende Aufruf erhält anschließend den Replay-Erfolg.
    [Test]
    public async Task ConcurrentCompletion_ShouldCommitOnceAndReplaySuccess()
    {
        var provider = new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema);
        var definition = CreateDefinition("Definitions_IdempotentTask", 1, 0, isActive: false);
        using (var storage = provider.GetTransactionalStorage())
        {
            await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition { DefinitionId = definition.DefinitionId, Name = "Idempotent task" });
            await storage.DefinitionStorage.StoreDefinition(definition);
            await storage.DefinitionStorage.StoreBinary(definition.Id, UserTaskXml);
            await FormTestSeed.StoreAsync(storage, "Approval");
            storage.CommitChanges();
        }
        var deployer = new BpmnBusinessLogic(provider);
        await deployer.DeployDefinition(definition);
        var instance = await deployer.StartProcessInstance(definition.DefinitionId);
        var token = instance.Tokens.Single(candidate => candidate.CurrentFlowNode is BPMN.HumanInteraction.UserTask);
        var result = new UserTaskResult { ProcessInstanceId = instance.InstanceId, TokenId = token.Id, FlowNodeId = token.CurrentFlowNode!.Id };
        var actor = new CurrentUserContext(Guid.NewGuid(), "test", false);
        var request = Request('c', 'd', "user-task-completion");

        var outcomes = await Task.WhenAll(
            new BpmnBusinessLogic(provider).CompleteUserTaskAsync(result, actor, true, idempotency: request),
            new BpmnBusinessLogic(provider).CompleteUserTaskAsync(result, actor, true, idempotency: request));

        outcomes.Should().OnlyContain(outcome => outcome == UserTaskCompletionOutcome.Completed);
        var reader = new PostgreSqlStorage(_dataSource!, Schema);
        (await reader.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Should().BeEmpty();
        (await reader.InstanceStorage.GetProcessInstance(instance.InstanceId)).State.Should().Be(ProcessInstanceState.Completed);
    }

    // Testzweck: Nach erfolgreichem Commit bleibt die Inhaltsbindung über eine neue
    // Engine-Instanz erhalten; ein geänderter Hash wird vor jeder Mutation abgelehnt.
    [Test]
    public async Task ChangedContent_ShouldConflictAfterRestart()
    {
        var provider = new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema);
        var definition = CreateDefinition("Definitions_IdempotentConflict", 1, 0, isActive: false);
        using (var storage = provider.GetTransactionalStorage())
        {
            await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition { DefinitionId = definition.DefinitionId, Name = "Idempotent conflict" });
            await storage.DefinitionStorage.StoreDefinition(definition);
            await storage.DefinitionStorage.StoreBinary(definition.Id, EndXml);
            storage.CommitChanges();
        }
        await new BpmnBusinessLogic(provider).DeployDefinition(definition);
        await new BpmnBusinessLogic(provider).StartProcessInstance(definition.DefinitionId, idempotency: Request('e', 'f', "workflow-start"));

        Func<Task> changed = () => new BpmnBusinessLogic(provider).StartProcessInstance(
            definition.DefinitionId, idempotency: Request('e', '0', "workflow-start"));
        await changed.Should().ThrowAsync<IdempotencyConflictException>();
        var reader = new PostgreSqlStorage(_dataSource!, Schema);
        (await reader.InstanceStorage.GetAllInstances()).Should().ContainSingle();
    }

    // Testzweck: PostgreSQL bereinigt nur abgelaufene Erfolge; ein offener Datensatz
    // bleibt als Sperre für einen potenziell unklaren Ausgang erhalten.
    [Test]
    public async Task Retention_ShouldDeleteCompletedButKeepIncompleteRecord()
    {
        var provider = new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema);
        var expired = DateTime.UtcNow.AddDays(-1);
        var incomplete = Record('1', expired);
        var completed = Record('2', expired);
        using (var storage = provider.GetTransactionalStorage())
        {
            (await storage.IdempotencyStorage.TryCreate(incomplete)).Should().BeTrue();
            (await storage.IdempotencyStorage.TryCreate(completed)).Should().BeTrue();
            await storage.IdempotencyStorage.Complete(completed.ScopeHash, Guid.NewGuid());
            storage.CommitChanges();
        }
        using (var storage = provider.GetTransactionalStorage())
        {
            await storage.IdempotencyStorage.DeleteExpired(DateTime.UtcNow);
            storage.CommitChanges();
        }

        using var reader = new PostgreSqlStorage(_dataSource!, Schema);
        (await reader.IdempotencyStorage.Get(incomplete.ScopeHash)).Should().NotBeNull();
        (await reader.IdempotencyStorage.Get(completed.ScopeHash)).Should().BeNull();
    }

    // Testzweck: Wenn das Idempotenz-Ergebnis nach den Fachschreibvorgängen ausfällt,
    // rollt PostgreSQL Instanz und Reservierung gemeinsam zurück; derselbe Retry kann
    // anschließend genau eine erfolgreiche Instanz anlegen.
    [Test]
    public async Task CompletionFailure_ShouldRollbackInstanceAndReservationTogether()
    {
        var provider = new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema);
        var definition = CreateDefinition("Definitions_IdempotentRollback", 1, 0, isActive: false);
        using (var storage = provider.GetTransactionalStorage())
        {
            await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
                { DefinitionId = definition.DefinitionId, Name = "Idempotent rollback" });
            await storage.DefinitionStorage.StoreDefinition(definition);
            await storage.DefinitionStorage.StoreBinary(definition.Id, EndXml);
            storage.CommitChanges();
        }
        await new BpmnBusinessLogic(provider).DeployDefinition(definition);
        var request = Request('3', '4', "workflow-start");
        var failingProvider = new FailingIdempotencyCompleteStorageProvider(provider);

        Func<Task> failed = async () => await new BpmnBusinessLogic(failingProvider)
            .StartProcessInstance(definition.DefinitionId, idempotency: request);
        await failed.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Injected idempotency completion failure.");

        var retry = await new BpmnBusinessLogic(provider)
            .StartProcessInstance(definition.DefinitionId, idempotency: request);
        using var reader = new PostgreSqlStorage(_dataSource!, Schema);
        (await reader.InstanceStorage.GetAllInstances()).Should().ContainSingle()
            .Which.InstanceId.Should().Be(retry.InstanceId);
    }

    private static IdempotencyRequest Request(char scope, char content, string operation) =>
        new(new string(scope, 64), new string(content, 64), operation);

    private static IdempotencyRecord Record(char scope, DateTime expiresAt) => new()
    {
        ScopeHash = new string(scope, 64), RequestHash = new string('0', 64), Operation = "test",
        CreatedAt = expiresAt.AddDays(-1), ExpiresAt = expiresAt
    };

    private const string EndXml = """
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Definitions_Idempotent" targetNamespace="test">
          <bpmn:process id="Process_Idempotent" isExecutable="true">
            <bpmn:startEvent id="Start"/><bpmn:endEvent id="End"/>
            <bpmn:sequenceFlow id="Flow" sourceRef="Start" targetRef="End"/>
          </bpmn:process>
        </bpmn:definitions>
        """;
}
