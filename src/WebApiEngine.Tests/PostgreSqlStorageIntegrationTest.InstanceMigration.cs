using PostgreSqlStorageSystem;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Der Kernfall des Umzugs gilt auch auf dem Betriebspfad PostgreSQL — dort
    // laeuft er in einer echten Transaktion je Instanz.
    [Test]
    public async Task Migration_ShouldLiftAWaitingInstanceOnPostgreSql()
    {
        await InstanceMigrationScenarios.CoreAsync(
            new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema));
    }

    // Testzweck: Auch in PostgreSQL entscheidet die Formularbindung ueber den privaten Entwurf;
    // Spalte und Rumpf muessen dabei gemeinsam umgebunden werden.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Migration_ShouldKeepDraftsOnlyForIdenticalFormsOnPostgreSql(bool changeForm)
    {
        await InstanceMigrationScenarios.DraftAsync(
            new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema), changeForm);
    }

    // Testzweck: Eine gescheiterte Instanz rollt in PostgreSQL nur ihre eigene Transaktion
    // zurueck; die migrierbare Instanz derselben Anfrage bleibt migriert.
    [Test]
    public async Task Migration_ShouldMigratePartiallyOnPostgreSql()
    {
        await InstanceMigrationScenarios.PartialAsync(
            new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema));
    }

    // Testzweck: In PostgreSQL liegt der Vergabezustand eines Auftrags in eigenen Spalten. Das
    // Umbinden auf die Zielversion darf die Sperre des arbeitenden Workers nicht loeschen.
    [Test]
    public async Task Migration_ShouldKeepLockedServiceTaskJobsOnPostgreSql()
    {
        await InstanceMigrationScenarios.ServiceTaskAsync(
            new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema));
    }

    // Testzweck: Timer- und Nachrichtenanmeldungen tragen auch auf dem Betriebspfad nach dem
    // Umzug die Zielversion.
    [Test]
    public async Task Migration_ShouldRebindSubscriptionsOnPostgreSql()
    {
        await InstanceMigrationScenarios.SubscriptionsAsync(
            new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema));
    }
}
