using PostgreSqlStorageSystem;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Der Kernfall des Eingriffs gilt auch auf dem Betriebspfad PostgreSQL — dort
    // laeuft er in einer echten Transaktion, und die Eingriffsspur muss eine Spalte ueberleben.
    [Test]
    public async Task Modification_ShouldMoveAWaitingStepOnPostgreSql()
    {
        await InstanceModificationScenarios.CoreAsync(
            new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema));
    }

    // Testzweck: In PostgreSQL liegt der Auftrag in eigenen Spalten. Wird sein Schritt
    // verschoben, muss er wirklich verschwinden statt verwaist liegen zu bleiben.
    [Test]
    public async Task Modification_ShouldCancelTheServiceTaskJobOnPostgreSql()
    {
        await InstanceModificationScenarios.ServiceTaskJobAsync(
            new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema));
    }

    // Testzweck: Der Entwurf haengt in PostgreSQL per Fremdschluessel an der Aufgabe. Mit der
    // verschwindenden Aufgabe muss er mitgehen, sonst bleibt er unerreichbar zurueck.
    [Test]
    public async Task Modification_ShouldDiscardDraftsOfTheCancelledTaskOnPostgreSql()
    {
        await InstanceModificationScenarios.DraftAsync(
            new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema));
    }

    // Testzweck: Eine abgelehnte Anfrage darf auch auf dem Betriebspfad nichts zuruecklassen —
    // die Transaktion bleibt ohne Commit offen.
    [Test]
    public async Task Modification_ShouldWriteNothingWhenTheRequestHasProblemsOnPostgreSql()
    {
        await InstanceModificationScenarios.ProblemsAsync(
            new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema));
    }
}
