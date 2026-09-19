using FilesystemStorageSystem;
using FluentAssertions;
using StorageSystem;

namespace WebApiEngine.Tests;

/// <summary>
/// Vollstaendiges Loeschen einer Instanz in der Dateiablage. Das Gegenstueck fuer PostgreSQL
/// steht in <c>PostgreSqlStorageIntegrationTest.InstanceRetention.cs</c> und prueft dieselbe
/// Datenartenliste.
/// </summary>
[NonParallelizable]
public class InstancePurgeFilesystemTest
{
    private static readonly DateTime FinishedAt = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    // Testzweck: Das Loeschen einer Instanz entfernt jede einzelne an ihr haengende Datenart —
    // Anmeldungen, Entwuerfe, Bearbeiterzustand, Historie, Faelligkeiten, Meldungen, Auftraege,
    // Ereignisspur, KI-Laeufe und Idempotenzverweise. Bliebe eine davon liegen, waere die
    // Aufbewahrung genau der Datenrest, den sie verhindern soll.
    [Test]
    public async Task PurgeInstance_ShouldRemoveEveryAttachedRecord()
    {
        using var context = new StorageContext();
        var seeded = await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt);
        await InstancePurgeFixture.AssertIntactAsync(context.Storage, seeded);

        await InstancePurge.ExecuteAsync(context.Storage, seeded.InstanceId);

        await InstancePurgeFixture.AssertPurgedAsync(context.Storage, seeded);
    }

    // Testzweck: Eine zweite, gleichartige Instanz bleibt vollstaendig unberuehrt. Ein Loeschen,
    // das ueber die Instanzgrenze hinausgreift, waere schlimmer als gar keine Aufbewahrung.
    [Test]
    public async Task PurgeInstance_ShouldLeaveOtherInstancesUntouched()
    {
        using var context = new StorageContext();
        var victim = await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt);
        var bystander = await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt);

        await InstancePurge.ExecuteAsync(context.Storage, victim.InstanceId);

        await InstancePurgeFixture.AssertPurgedAsync(context.Storage, victim);
        await InstancePurgeFixture.AssertIntactAsync(context.Storage, bystander);
    }

    // Testzweck: Eine laufende Instanz wird nicht angefasst. Sie taucht in der Auswahlmenge der
    // Aufbewahrung gar nicht erst auf; dieser Test haelt die Auswahl selbst fest.
    [Test]
    public async Task GetAllFinishedInstances_ShouldNeverReturnARunningInstance()
    {
        using var context = new StorageContext();
        var running = await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt, finished: false);
        var finished = await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt);

        var candidates = (await context.Storage.InstanceStorage.GetAllFinishedInstances()).ToArray();

        candidates.Select(instance => instance.InstanceId).Should().BeEquivalentTo([finished.InstanceId]);
        candidates.Should().NotContain(instance => instance.InstanceId == running.InstanceId);
    }

    // Testzweck: Die Aufbewahrung rechnet mit demselben Endzeitpunkt, den die Instanzansicht
    // anzeigt — dem letzten Zustandswechsel. Eine beendete Instanz ohne Tokens laesst sich nicht
    // datieren und bleibt deshalb ausdruecklich stehen, statt mangels Datum sofort zu fallen.
    [Test]
    public async Task GetFinishedAtUtc_ShouldUseTheLastStateChange_AndStayNullWithoutTokens()
    {
        using var context = new StorageContext();
        var seeded = await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt);
        var instance = await context.Storage.InstanceStorage.GetProcessInstance(seeded.InstanceId);

        ProcessInstanceLifetime.GetFinishedAtUtc(instance).Should().Be(FinishedAt);

        instance.Tokens.Clear();
        ProcessInstanceLifetime.GetFinishedAtUtc(instance).Should().BeNull();
    }

    private sealed class StorageContext : IDisposable
    {
        private readonly string? _originalRoot;
        private readonly string _tempRoot;

        public StorageContext()
        {
            _originalRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            _tempRoot = Path.Combine(Path.GetTempPath(), "flowzer-instance-purge-test", Guid.NewGuid().ToString("N"));
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
