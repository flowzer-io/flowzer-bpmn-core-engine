using FilesystemStorageSystem;

namespace WebApiEngine.Tests;

[NonParallelizable]
public class StableUserTaskIdentityTest
{
    // Testzweck: Eine geöffnete parallele Aufgabe behält nach Fortschritt und Neustart
    // ID und gespeicherte Zuweisung; nur ein neuer Schritt bekommt eine neue ID.
    [Test]
    public async Task ParallelProgress_ShouldPreserveExistingTaskOnFilesystem()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await StableUserTaskScenarios.ProgressAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Auch ein Timerfortschritt darf die daneben wartende Aufgabe nicht ersetzen.
    [Test]
    public async Task TimerProgress_ShouldPreserveExistingTaskOnFilesystem()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await StableUserTaskScenarios.TimerAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Abbruch entfernt nur Aufgaben der betroffenen Instanz, keine fremden.
    [Test]
    public async Task Cancellation_ShouldRemoveOnlyOwnTasksOnFilesystem()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await StableUserTaskScenarios.CancelAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Doppelte Tokens oder inkonsistente Definitionen werden nicht automatisch
    // zusammengeführt; vor Aufgaben-Writes ist eine ausdrückliche Klärung erforderlich.
    [TestCase(false)]
    [TestCase(true)]
    public async Task Corruption_ShouldNotBeSilentlyRepairedOnFilesystem(bool duplicate)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await StableUserTaskScenarios.CorruptAsync(new FileSystemTransactionalStorageProvider(), duplicate);
    }

    // Testzweck: Stabile Directory-Referenzen und Task-ID überleben Fortschritt sowie einen
    // vollständigen Datei-Storage-/Engine-Neustart ohne Rückfall auf Textzuweisungen.
    [Test]
    public async Task DirectoryAssignment_ShouldSurviveProgressAndRestartOnFilesystem()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await StableUserTaskScenarios.DirectoryProgressAsync(new FileSystemTransactionalStorageProvider());
    }
}
