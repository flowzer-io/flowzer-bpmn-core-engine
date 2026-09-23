using FluentAssertions;
using Model;
using PostgreSqlStorageSystem;
using StorageSystem.Exceptions;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Die PostgreSQL-Ablage verhaelt sich wie die Dateiablage: fortlaufende
    // Staende, der juengste als deployter, gezielter Zugriff auf eine Fassung.
    [Test]
    public async Task DecisionStorage_ShouldMirrorFilesystemContract()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        var decisions = new[] { new DecisionSummary("rabattstufe", "Rabattstufe") };
        await storage.DecisionStorage.SaveDefinition(new DecisionDefinition("rabatt", "Rabattstufen"));
        await storage.DecisionStorage.SaveVersion(Version("rabatt", 1, "<erste/>", decisions));
        await storage.DecisionStorage.SaveVersion(Version("rabatt", 2, "<zweite/>", decisions));

        (await storage.DecisionStorage.GetDefinitions()).Should().ContainSingle()
            .Which.Name.Should().Be("Rabattstufen");
        (await storage.DecisionStorage.GetVersions("rabatt")).Select(version => version.Version)
            .Should().Equal(1, 2);
        (await storage.DecisionStorage.GetLatestVersion("rabatt"))!.Xml.Should().Be("<zweite/>");
        (await storage.DecisionStorage.GetVersion("rabatt", 1))!.Decisions.Should().BeEquivalentTo(decisions);
        (await storage.DecisionStorage.GetVersion("rabatt", 3)).Should().BeNull();
        (await storage.DecisionStorage.GetDefinition("unbekannt")).Should().BeNull();
        (await storage.DecisionStorage.GetLatestVersion("unbekannt")).Should().BeNull();
    }

    // Testzweck: Zwei gleichzeitige Uploads duerfen nicht dieselbe Versionsnummer belegen;
    // der zweite bekommt einen Konflikt statt den ersten still zu ersetzen.
    [Test]
    public async Task DecisionStorage_ShouldRejectAnAlreadyStoredVersionNumber()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        await storage.DecisionStorage.SaveDefinition(new DecisionDefinition("rabatt", "Rabattstufen"));
        await storage.DecisionStorage.SaveVersion(Version("rabatt", 1, "<erste/>", []));

        await storage.DecisionStorage.Invoking(item => item.SaveVersion(Version("rabatt", 1, "<andere/>", [])))
            .Should().ThrowAsync<DefinitionStorageConflictException>();
        (await storage.DecisionStorage.GetVersion("rabatt", 1))!.Xml.Should().Be("<erste/>");
    }

    // Testzweck: Ein erneutes Speichern des Katalogkopfs benennt um, statt einen zweiten
    // Eintrag anzulegen; das Loeschen nimmt die Staende mit.
    [Test]
    public async Task DecisionStorage_ShouldRenameTheHeadAndDeleteEveryVersion()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        await storage.DecisionStorage.SaveDefinition(new DecisionDefinition("rabatt", "Rabattstufen"));
        await storage.DecisionStorage.SaveVersion(Version("rabatt", 1, "<erste/>", []));
        await storage.DecisionStorage.SaveDefinition(new DecisionDefinition("rabatt", "Rabattstufen 2026"));

        (await storage.DecisionStorage.GetDefinitions()).Should().ContainSingle()
            .Which.Name.Should().Be("Rabattstufen 2026");

        await storage.DecisionStorage.DeleteDefinition("rabatt");

        (await storage.DecisionStorage.GetDefinitions()).Should().BeEmpty();
        (await storage.DecisionStorage.GetVersions("rabatt")).Should().BeEmpty();
    }

    // Testzweck: Auch die Datenbankablage weist eine Kennung mit Pfadanteilen ab. Sie wandert
    // zwar in keinen Dateinamen, darf aber nicht ueber die Adapter hinweg unterschiedlich
    // streng sein — sonst waere ein Bestand nach dem Umzug nicht mehr derselbe.
    [Test]
    public async Task DecisionStorage_ShouldRejectDecisionDefinitionIdsWithPathCharacters()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema);

        await storage.DecisionStorage.Invoking(item =>
                item.SaveDefinition(new DecisionDefinition("../x", "Boese")))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*is not a valid definition id*");
        await storage.DecisionStorage.Invoking(item => item.GetVersions("../x"))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*is not a valid definition id*");
    }

    private static DecisionDefinitionVersion Version(
        string decisionDefinitionId, int version, string xml, IReadOnlyList<DecisionSummary> decisions) =>
        new(decisionDefinitionId, version, xml, DateTimeOffset.UtcNow, Guid.NewGuid(), decisions);
}
