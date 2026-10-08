using FluentAssertions;
using Model;
using PostgreSqlStorageSystem;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.BusinessLogic;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Getrennte API-/Engine-Objekte teilen nur die echte PostgreSQL-Sperre;
    // gleichzeitiger persönlicher Rückzug muss genau einen Auditfakt und keine offenen Tasks behalten.
    [Test]
    public async Task ConcurrentWithdrawal_ShouldPersistOnePersonalAuditAcrossDatabaseSessions()
    {
        var provider = new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema);
        var actor = new CurrentUserContext(Guid.NewGuid(), "synthetic:test", false)
        {
            Identity = new AuthenticatedSubject("https://synthetic.test/realms/demo", "synthetic-initiator")
        };
        var instance = await StartWithdrawalFixture(provider, actor);
        var outcomes = await Task.WhenAll(
            new BpmnBusinessLogic(provider).WithdrawInstance(instance.InstanceId, actor),
            new BpmnBusinessLogic(provider).WithdrawInstance(instance.InstanceId, actor));
        var first = outcomes[0].Tokens.Single(token => token.ParentTokenId is null).Withdrawal;
        first.Should().NotBeNull();
        outcomes[1].Tokens.Single(token => token.ParentTokenId is null).Withdrawal.Should().Be(first);
        using var reader = new PostgreSqlStorage(_dataSource!, Schema);
        var stored = await reader.InstanceStorage.GetProcessInstance(instance.InstanceId);
        stored.State.Should().Be(ProcessInstanceState.Terminated);
        stored.Tokens.Single(token => token.ParentTokenId is null).Withdrawal.Should().Be(first);
        (await reader.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Should().BeEmpty();
    }

    // Testzweck: Dieselbe technische Benutzer-GUID in einem anderen Issuer darf
    // auch über die transaktionale Ablage keine fremde Instanz zurückziehen oder deren Zustand ändern.
    [Test]
    public async Task Withdrawal_ShouldRejectAnotherIssuerBeforeDatabaseMutation()
    {
        var provider = new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema);
        var actor = new CurrentUserContext(Guid.NewGuid(), "synthetic:test", false)
        {
            Identity = new AuthenticatedSubject("https://synthetic.test/realms/demo", "synthetic-initiator")
        };
        var instance = await StartWithdrawalFixture(provider, actor);
        var foreign = actor with { Identity = actor.Identity! with { Issuer = "https://other.test/realms/demo" } };
        Func<Task> denied = () => new BpmnBusinessLogic(provider).WithdrawInstance(instance.InstanceId, foreign);
        await denied.Should().ThrowAsync<FileNotFoundException>();
        using var reader = new PostgreSqlStorage(_dataSource!, Schema);
        var stored = await reader.InstanceStorage.GetProcessInstance(instance.InstanceId);
        stored.IsFinished.Should().BeFalse();
        stored.Tokens.Single(token => token.ParentTokenId is null).Withdrawal.Should().BeNull();
        (await reader.SubscriptionStorage.GetAllUserTasks(instance.InstanceId)).Should().ContainSingle();
    }

    private static async Task<ProcessInstanceInfo> StartWithdrawalFixture(
        PostgreSqlTransactionalStorageProvider provider, CurrentUserContext actor)
    {
        var definition = CreateDefinition("Definitions_Withdrawal", 1, 0, isActive: false);
        using (var storage = provider.GetTransactionalStorage())
        {
            await storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
                { DefinitionId = definition.DefinitionId, Name = "Synthetic withdrawal" });
            await storage.DefinitionStorage.StoreDefinition(definition);
            await storage.DefinitionStorage.StoreBinary(definition.Id, UserTaskXml);
            await FormTestSeed.StoreAsync(storage, "Approval");
            storage.CommitChanges();
        }
        var engine = new BpmnBusinessLogic(provider);
        await engine.DeployDefinition(definition);
        return await engine.StartProcessInstance(definition.DefinitionId,
            initiator: actor.Identity, expectedDefinitionId: definition.Id);
    }
}
