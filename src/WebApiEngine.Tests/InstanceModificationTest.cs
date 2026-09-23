using FilesystemStorageSystem;

namespace WebApiEngine.Tests;

/// <summary>Die Eingriffsszenarien gegen die isolierte Dateiablage.</summary>
[NonParallelizable]
public sealed class InstanceModificationTest
{
    // Testzweck: Der Kernfall — die alte Aufgabe verschwindet, am Ziel entsteht eine neue mit
    // neuer Kennung, die Variablen stimmen, und die Spur haelt das ohne Werte fest.
    [Test]
    public async Task Modification_ShouldMoveAWaitingStepOnFilesystem()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceModificationScenarios.CoreAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Der Auftrag eines verschobenen Service-Tasks verfaellt, und der Trockenlauf
    // kuendigt das an — sonst wartet ein Worker auf Arbeit, die es nicht mehr gibt.
    [Test]
    public async Task Modification_ShouldCancelTheServiceTaskJobOnFilesystem()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceModificationScenarios.ServiceTaskJobAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Mit der Aufgabe geht ihr privater Entwurf verloren; wer das erst hinterher
    // merkt, hat seine Eingaben ohne Vorwarnung verloren.
    [Test]
    public async Task Modification_ShouldDiscardDraftsOfTheCancelledTask()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceModificationScenarios.DraftAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Ein Hindernis traegt einen stabilen Code, und die abgelehnte Anfrage laesst die
    // Instanz unveraendert — auch ihre Spur bleibt leer.
    [Test]
    public async Task Modification_ShouldReportProblemsAndChangeNothing()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceModificationScenarios.ProblemsAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: An einer beendeten Instanz gibt es nichts zu ruecken; das ist ein
    // Zustandskonflikt und keine unbrauchbare Angabe.
    [Test]
    public async Task Modification_ShouldRefuseAFinishedInstance()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceModificationScenarios.FinishedInstanceAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Die leere Anfrage ist der Weg der Oberflaeche, ueberhaupt zu erfahren, was
    // moeglich ist — als Trockenlauf gueltig, als Eingriff nicht.
    [Test]
    public async Task Modification_ShouldAnswerAnEmptyPreviewButRefuseAnEmptyChange()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceModificationScenarios.EmptyRequestAsync(new FileSystemTransactionalStorageProvider());
    }

    // Testzweck: Eine unbekannte Instanz ist ein eigener Fall; der Controller macht daraus 404.
    [Test]
    public async Task Modification_ShouldReportAnUnknownInstance()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await InstanceModificationScenarios.UnknownInstanceAsync(new FileSystemTransactionalStorageProvider());
    }
}
