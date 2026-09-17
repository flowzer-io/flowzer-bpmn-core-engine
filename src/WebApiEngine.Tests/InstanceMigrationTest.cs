using FilesystemStorageSystem;

namespace WebApiEngine.Tests;

/// <summary>Die Migrationsszenarien gegen die isolierte Dateiablage.</summary>
[NonParallelizable]
public sealed class InstanceMigrationTest
{
    // Testzweck: Der Kernfall des Umzugs — Aufgabe, Bearbeitungsdaten und Migrationsspur
    // bleiben, und die Instanz folgt danach dem Modell der Zielversion.
    [Test]
    public async Task Migration_ShouldLiftAWaitingInstanceOnFilesystem()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceMigrationScenarios.CoreAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Ein privater Entwurf ueberlebt nur die unveraenderte Formularbindung; sonst
    // wird er verworfen und der Trockenlauf kuendigt genau das an.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Migration_ShouldKeepDraftsOnlyForIdenticalFormsOnFilesystem(bool changeForm)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceMigrationScenarios.DraftAsync(new FileSystemTransactionalStorageProvider(), changeForm);
    }

    // Testzweck: Eine nicht deckungsgleiche Instanz bleibt unveraendert und haelt die
    // migrierbare Instanz derselben Anfrage nicht auf.
    [Test]
    public async Task Migration_ShouldMigratePartiallyOnFilesystem()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceMigrationScenarios.PartialAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Ein laufender Worker-Auftrag behaelt Kennung und Sperre und laesst sich nach
    // dem Umzug unveraendert zurueckmelden.
    [Test]
    public async Task Migration_ShouldKeepLockedServiceTaskJobsOnFilesystem()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceMigrationScenarios.ServiceTaskAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Timer- und Nachrichtenanmeldungen der Instanz tragen nach dem Umzug die
    // Zielversion; sonst suchte ein eintreffendes Ereignis die falsche Version.
    [Test]
    public async Task Migration_ShouldRebindSubscriptionsOnFilesystem()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceMigrationScenarios.SubscriptionsAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Eine Instanz auf der deployten Version ist kein Umzug, sondern ein Befund.
    [Test]
    public async Task Migration_ShouldReportInstancesAlreadyOnTheDeployedVersion()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceMigrationScenarios.AlreadyOnTargetAsync(new FileSystemTransactionalStorageProvider());
    }
}
