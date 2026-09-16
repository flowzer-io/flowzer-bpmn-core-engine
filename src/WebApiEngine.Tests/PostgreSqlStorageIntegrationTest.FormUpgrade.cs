using FluentAssertions;
using Model;
using PostgreSqlStorageSystem;
using WebApiEngine.Persistence;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Bestandsübernahme läuft im selben PostgreSQL-Commit und ist nach
    // Rollback vollständig unsichtbar; wiederholte Deploymentläufe bleiben idempotent.
    [Test]
    public async Task HistoricalFormUpgrade_ShouldCommitAtomicallyAndBeRepeatable()
    {
        using var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var definition = CreateDefinition("legacy-upgrade", 1, 0, isActive: true);
        var formId = Guid.NewGuid();
        var form = new Form { Id = Guid.NewGuid(), FormId = formId, Version = new Model.Version(1, 0), FormData = """{"components":[]}""" };
        await storage.FormStorage.SaveFormMetaData(new FormMetadata { FormId = formId, Name = "Legacy" });
        await storage.FormStorage.SaveForm(form);
        await storage.DefinitionStorage.StoreDefinition(definition);
        await storage.DefinitionStorage.StoreBinary(definition.Id, """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:z="http://camunda.org/schema/zeebe/1.0">
              <process id="Process"><startEvent id="Start"><extensionElements><z:formDefinition formKey="Legacy" /></extensionElements></startEvent></process>
            </definitions>
            """);
        using (var rollback = new PostgreSqlTransactionalStorage(_dataSource!, Schema))
        {
            await rollback.LockForFormCompatibilityUpgradeAsync();
            (await LegacyFormBindingUpgrade.ApplyAsync(rollback)).Should().Be(1);
            rollback.RollbackTransaction();
        }
        (await storage.DefinitionStorage.GetDefinitionById(definition.Id)).FormBindings.Should().BeNull();
        using (var commit = new PostgreSqlTransactionalStorage(_dataSource!, Schema))
        {
            await commit.LockForFormCompatibilityUpgradeAsync();
            (await LegacyFormBindingUpgrade.ApplyAsync(commit)).Should().Be(1);
            commit.CommitChanges();
        }
        (await storage.DefinitionStorage.GetDefinitionById(definition.Id)).FormBindings!["Legacy"].Id.Should().Be(form.Id);
        using var repeat = new PostgreSqlTransactionalStorage(_dataSource!, Schema);
        await repeat.LockForFormCompatibilityUpgradeAsync();
        (await LegacyFormBindingUpgrade.ApplyAsync(repeat)).Should().Be(0);
        repeat.CommitChanges();
    }
}
