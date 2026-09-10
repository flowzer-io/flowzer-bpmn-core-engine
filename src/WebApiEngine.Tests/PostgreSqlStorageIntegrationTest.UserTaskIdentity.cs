using PostgreSqlStorageSystem;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Eine geöffnete parallele Aufgabe behält nach Fortschritt und Neustart
    // ID und gespeicherte Zuweisung; nur ein neuer Schritt bekommt eine neue ID.
    [Test]
    public async Task ParallelProgress_ShouldPreserveExistingTaskOnPostgreSql()
    {
        await StableUserTaskScenarios.ProgressAsync(new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema));
    }

    // Testzweck: Auch ein Timerfortschritt darf die daneben wartende Aufgabe nicht ersetzen.
    [Test]
    public async Task TimerProgress_ShouldPreserveExistingTaskOnPostgreSql()
    {
        await StableUserTaskScenarios.TimerAsync(new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema));
    }

    // Testzweck: Abbruch entfernt nur Aufgaben der betroffenen Instanz, keine fremden.
    [Test]
    public async Task Cancellation_ShouldRemoveOnlyOwnTasksOnPostgreSql()
    {
        await StableUserTaskScenarios.CancelAsync(new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema));
    }

    // Testzweck: Doppelte Tokens oder inkonsistente Definitionen werden nicht automatisch
    // zusammengeführt; vor Aufgaben-Writes ist eine ausdrückliche Klärung erforderlich.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Corruption_ShouldNotBeSilentlyRepairedOnPostgreSql(bool duplicate)
    {
        await StableUserTaskScenarios.CorruptAsync(new PostgreSqlTransactionalStorageProvider(_dataSource!, Schema), duplicate);
    }
}
