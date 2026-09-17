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

    // Testzweck: Deployt ein zweiter API-Prozess mitten im Stapel, darf der Umzug die restlichen
    // Instanzen nicht an die alte Zielversion haengen; bereits migrierte Instanzen bleiben es.
    [Test]
    public async Task Migration_ShouldStopInstancesAfterAnotherProcessDeployed()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceMigrationScenarios.TargetVersionChangedAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Eine Ablage ohne Entwurfsvertrag darf nicht halb umziehen; Vorschau und Umzug
    // melden das mit eigenem Code, und die Instanz bleibt unveraendert.
    [Test]
    public async Task Migration_ShouldRefuseInstancesWhenDraftsCannotBeMoved()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceMigrationScenarios.DraftStorageNotSupportedAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Eine Ablage, die ausdruecklich keine Entwuerfe fuehrt, darf den Umzug nicht
    // verhindern — sie kann keinen Entwurf verlieren, den es nie gab.
    [Test]
    public async Task Migration_ShouldRunWithoutADraftStorage()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceMigrationScenarios.NoDraftStorageAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Timer rechnen nach dem Umzug mit der Dauer der Zielversion ab dem
    // urspruenglichen Beginn des Wartens; die Vorschau muss davor warnen — auch fuer einen
    // Boundary-Timer, den erst die Zielversion anheftet.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Migration_ShouldAnnounceRecalculatedTimers(bool boundaryTimer)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceMigrationScenarios.TimerNoticeAsync(
            new FileSystemTransactionalStorageProvider(), boundaryTimer);
    }

    // Testzweck: Ein fehlender Knoten laesst sich von Hand zuordnen; der Trockenlauf fragt
    // danach, und der Umzug fasst den Plan mit der Zuordnung auch in seiner eigenen Transaktion
    // neu — sonst bliebe die Instanz trotz Zuordnung liegen.
    [Test]
    public async Task Migration_ShouldLiftAnInstanceWithAManualMappingOnFilesystem()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceMigrationScenarios.MappingAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Eine Zuordnung auf einen unbekannten Knoten veraendert nichts und wird erneut
    // abgefragt, statt die Instanz stillschweigend liegen zu lassen.
    [Test]
    public async Task Migration_ShouldAskAgainWhenTheMappingTargetIsMissing()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceMigrationScenarios.MappingTargetMissingAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Fehlt das Modell der Quellversion, bleibt die Frage nach der Zuordnung
    // stellbar — mit Kennung und Typ aus dem wartenden Token und ohne geratenen Namen.
    [Test]
    public async Task MigrationPreview_ShouldAskForAMappingWithoutTheSourceModel()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceMigrationScenarios.MappingWithoutSourceModelAsync(
            new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Eine Zuordnung ueber Elementtypen hinweg ist beantwortet, aber unzulaessig.
    [Test]
    public async Task Migration_ShouldRefuseAMappingAcrossElementTypes()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceMigrationScenarios.MappingTypeChangedAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Die Zuordnung gilt fuer die ganze Anfrage und laesst die Instanz an einem
    // unveraenderten Knoten unberuehrt weiterziehen.
    [Test]
    public async Task Migration_ShouldApplyTheMappingOnlyWhereItIsNeeded()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceMigrationScenarios.MappingPartialAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Eine Instanz auf der deployten Version ist kein Umzug, sondern ein Befund.
    [Test]
    public async Task Migration_ShouldReportInstancesAlreadyOnTheDeployedVersion()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceMigrationScenarios.AlreadyOnTargetAsync(new FileSystemTransactionalStorageProvider());
    }
}
