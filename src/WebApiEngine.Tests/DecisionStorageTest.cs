using FilesystemStorageSystem;
using FluentAssertions;
using Model;
using StorageSystem;
using StorageSystem.Exceptions;

namespace WebApiEngine.Tests;

/// <summary>
/// Die Dateiablage der Entscheidungsdateien: Katalogkopf, fortlaufende Staende und der
/// Riegel vor dem Dateinamen.
/// </summary>
[NonParallelizable]
public sealed class DecisionStorageTest
{
    // Testzweck: Die Staende einer Datei bleiben nebeneinander stehen, kommen in der
    // Reihenfolge ihrer Nummern und der juengste ist der deployte.
    [Test]
    public async Task SaveVersion_ShouldKeepEveryStateAndReportTheLatest()
    {
        using var context = new StorageContext();
        var storage = context.Storage.DecisionStorage;
        await storage.SaveDefinition(new DecisionDefinition("rabatt", "Rabattstufen"));

        await storage.SaveVersion(Version("rabatt", 1, "<erste/>"));
        await storage.SaveVersion(Version("rabatt", 2, "<zweite/>"));
        await storage.SaveVersion(Version("rabatt", 3, "<dritte/>"));

        var versions = await storage.GetVersions("rabatt");
        versions.Select(version => version.Version).Should().Equal(1, 2, 3);
        (await storage.GetLatestVersion("rabatt"))!.Xml.Should().Be("<dritte/>");
        (await storage.GetVersion("rabatt", 2))!.Xml.Should().Be("<zweite/>");
        (await storage.GetVersion("rabatt", 4)).Should().BeNull();
    }

    // Testzweck: Ein gespeicherter Stand ist unveraenderlich. Zwei gleichzeitige Uploads
    // wuerden sonst beide die Nummer 2 bekommen und der zweite den ersten still ersetzen.
    [Test]
    public async Task SaveVersion_ShouldRejectAnAlreadyStoredVersionNumber()
    {
        using var context = new StorageContext();
        var storage = context.Storage.DecisionStorage;
        await storage.SaveDefinition(new DecisionDefinition("rabatt", "Rabattstufen"));
        await storage.SaveVersion(Version("rabatt", 1, "<erste/>"));

        await storage.Invoking(item => item.SaveVersion(Version("rabatt", 1, "<andere/>")))
            .Should().ThrowAsync<DefinitionStorageConflictException>();
        (await storage.GetVersion("rabatt", 1))!.Xml.Should().Be("<erste/>");
    }

    // Testzweck: Der Katalogkopf traegt die enthaltenen Entscheidungen je Stand; genau daran
    // findet die Engine spaeter die Datei zu einer decisionId.
    [Test]
    public async Task SaveVersion_ShouldKeepTheContainedDecisions()
    {
        using var context = new StorageContext();
        var storage = context.Storage.DecisionStorage;
        await storage.SaveDefinition(new DecisionDefinition("rabatt", "Rabattstufen"));

        await storage.SaveVersion(new DecisionDefinitionVersion("rabatt", 1, "<erste/>",
            DateTimeOffset.UtcNow, Guid.NewGuid(), [new DecisionSummary("rabattstufe", "Rabattstufe")]));

        var latest = await storage.GetLatestVersion("rabatt");
        latest!.Decisions.Should().ContainSingle()
            .Which.Should().Be(new DecisionSummary("rabattstufe", "Rabattstufe"));
    }

    // Testzweck: Loeschen entfernt Kopf und alle Staende — eine verwaiste Fassung waere
    // in der Oberflaeche unsichtbar und blockierte die Kennung.
    [Test]
    public async Task DeleteDefinition_ShouldRemoveTheHeadAndEveryVersion()
    {
        using var context = new StorageContext();
        var storage = context.Storage.DecisionStorage;
        await storage.SaveDefinition(new DecisionDefinition("rabatt", "Rabattstufen"));
        await storage.SaveVersion(Version("rabatt", 1, "<erste/>"));
        await storage.SaveVersion(Version("rabatt", 2, "<zweite/>"));

        await storage.DeleteDefinition("rabatt");

        (await storage.GetDefinition("rabatt")).Should().BeNull();
        (await storage.GetVersions("rabatt")).Should().BeEmpty();
        (await storage.GetDefinitions()).Should().BeEmpty();
    }

    // Testzweck: Das Suchmuster der Dateiablage darf keine fremde Datei mitnehmen, deren
    // Kennung mit derselben Zeichenfolge beginnt.
    [Test]
    public async Task GetVersions_ShouldNotReturnVersionsOfADefinitionWithTheSamePrefix()
    {
        using var context = new StorageContext();
        var storage = context.Storage.DecisionStorage;
        await storage.SaveDefinition(new DecisionDefinition("rabatt", "Rabattstufen"));
        await storage.SaveDefinition(new DecisionDefinition("rabatt-neu", "Neue Rabattstufen"));
        await storage.SaveVersion(Version("rabatt", 1, "<alt/>"));
        await storage.SaveVersion(Version("rabatt-neu", 1, "<neu/>"));

        (await storage.GetVersions("rabatt")).Should().ContainSingle().Which.Xml.Should().Be("<alt/>");
        (await storage.GetVersions("rabatt-neu")).Should().ContainSingle().Which.Xml.Should().Be("<neu/>");
    }

    // Testzweck: Eine Kennung mit Pfadanteilen wird abgewiesen, bevor sie zum Dateinamen
    // wird — sie zeigte sonst aus dem Ablageordner heraus.
    [Test]
    public async Task Storage_ShouldRejectDecisionDefinitionIdsWithPathCharacters()
    {
        using var context = new StorageContext();
        var storage = context.Storage.DecisionStorage;

        await storage.Invoking(item => item.SaveDefinition(new DecisionDefinition("../x", "Boese")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*is not a valid definition id*");
        await storage.Invoking(item => item.SaveVersion(Version("../x", 1, "<x/>")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*is not a valid definition id*");
        await storage.Invoking(item => item.GetVersions("../x"))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*is not a valid definition id*");
        await storage.Invoking(item => item.DeleteDefinition(".."))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*is not a valid definition id*");

        Directory.GetFiles(context.Storage.GetBasePath("FileStorage/Decisions")).Should().BeEmpty();
        Directory.GetFiles(context.Storage.GetBasePath("FileStorage/Decisions/Meta")).Should().BeEmpty();
    }

    private static DecisionDefinitionVersion Version(string decisionDefinitionId, int version, string xml) =>
        new(decisionDefinitionId, version, xml, DateTimeOffset.UtcNow, Guid.NewGuid(),
            [new DecisionSummary("rabattstufe", "Rabattstufe")]);

    private sealed class StorageContext : IDisposable
    {
        private readonly string? _originalRoot;
        private readonly string _tempRoot;

        public StorageContext()
        {
            _originalRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            _tempRoot = Path.Combine(Path.GetTempPath(), "flowzer-decision-storage-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _tempRoot);
            Storage = new Storage();
        }

        public Storage Storage { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _originalRoot);
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
    }
}
