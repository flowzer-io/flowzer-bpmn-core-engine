using FilesystemStorageSystem;
using Microsoft.Extensions.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Model;
using StorageSystem;
using WebApiEngine.Background;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Diagnostics;

namespace WebApiEngine.Tests;

/// <summary>
/// Fristenrechnung und Hintergrundlauf der Aufbewahrung. Die Uhr ist steuerbar, damit Fristen
/// von Tagen geprueft werden koennen, ohne zu warten.
/// </summary>
[NonParallelizable]
public class InstanceRetentionServiceTest
{
    private static readonly DateTime FinishedAt = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    // Testzweck: Die globale Frist entscheidet allein, solange kein Workflow etwas anderes sagt.
    // Einen Tag vor Ablauf passiert nichts, danach ist die Instanz weg — die Grenze selbst ist
    // die Zusage, nicht „irgendwann ungefaehr".
    [Test]
    public async Task RunAsync_ShouldDeleteOnlyAfterTheGlobalPeriodHasElapsed()
    {
        using var context = new RetentionContext();
        var seeded = await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt);

        (await context.Service.RunAsync(globalDays: 30, FinishedAt.AddDays(29), batchSize: 100))
            .Should().Be(0, "die Frist ist noch nicht abgelaufen");
        await InstancePurgeFixture.AssertIntactAsync(context.Storage, seeded);

        (await context.Service.RunAsync(globalDays: 30, FinishedAt.AddDays(30), batchSize: 100))
            .Should().Be(1);
        await InstancePurgeFixture.AssertPurgedAsync(context.Storage, seeded);
    }

    // Testzweck: Ohne globale Frist loescht ein Lauf nichts. Der Default ist aus; eine
    // Installation, die nie darum gebeten hat, darf keine Vorgangsdaten verlieren.
    [Test]
    public async Task RunAsync_ShouldDeleteNothing_WhenNoPeriodIsConfigured()
    {
        using var context = new RetentionContext();
        var seeded = await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt);

        (await context.Service.RunAsync(globalDays: null, FinishedAt.AddYears(10), batchSize: 100))
            .Should().Be(0);
        (await context.Service.RunAsync(globalDays: 0, FinishedAt.AddYears(10), batchSize: 100))
            .Should().Be(0);

        await InstancePurgeFixture.AssertIntactAsync(context.Storage, seeded);
    }

    // Testzweck: Das Workflow-Metadatum schlaegt den globalen Wert in beide Richtungen — eine
    // kuerzere Frist loescht frueher, eine laengere spaeter. Nur „kuerzer gewinnt" zu pruefen
    // liesse den Fall offen, in dem ein Workflow laenger aufbewahren muss als die Installation.
    [Test]
    public async Task RunAsync_ShouldLetTheWorkflowOverrideTheGlobalPeriodInBothDirections()
    {
        using var context = new RetentionContext();
        await context.SetRetentionAsync("kurz", 10);
        await context.SetRetentionAsync("lang", 90);
        var shortLived = await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt, metaDefinitionId: "kurz");
        var longLived = await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt, metaDefinitionId: "lang");

        (await context.Service.RunAsync(globalDays: 30, FinishedAt.AddDays(10), batchSize: 100)).Should().Be(1);
        await InstancePurgeFixture.AssertPurgedAsync(context.Storage, shortLived);
        await InstancePurgeFixture.AssertIntactAsync(context.Storage, longLived);

        // Die globale Frist von 30 Tagen ist laengst um; die Instanz haelt trotzdem bis Tag 90.
        (await context.Service.RunAsync(globalDays: 30, FinishedAt.AddDays(89), batchSize: 100)).Should().Be(0);
        await InstancePurgeFixture.AssertIntactAsync(context.Storage, longLived);

        (await context.Service.RunAsync(globalDays: 30, FinishedAt.AddDays(90), batchSize: 100)).Should().Be(1);
        await InstancePurgeFixture.AssertPurgedAsync(context.Storage, longLived);
    }

    // Testzweck: `retentionDays = 0` heisst „nie loeschen" und gewinnt auch gegen eine gesetzte
    // globale Frist. Wuerde die 0 als „sofort" oder als „nicht gesetzt" gelesen, verschwaenden
    // genau die aufbewahrungspflichtigen Vorgaenge zuerst.
    [Test]
    public async Task RunAsync_ShouldNeverDeleteAWorkflowWithRetentionDaysZero()
    {
        using var context = new RetentionContext();
        await context.SetRetentionAsync("nie", 0);
        var kept = await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt, metaDefinitionId: "nie");

        (await context.Service.RunAsync(globalDays: 1, FinishedAt.AddYears(10), batchSize: 100)).Should().Be(0);

        await InstancePurgeFixture.AssertIntactAsync(context.Storage, kept);
    }

    // Testzweck: Die Stapelgrenze wird eingehalten, und der naechste Lauf macht weiter. Ohne
    // Grenze triebe ein erstmalig aktivierter Lauf einen ganzen Altbestand auf einmal durch die
    // Datenbank.
    [Test]
    public async Task RunAsync_ShouldRespectTheBatchSizeAndContinueOnTheNextRun()
    {
        using var context = new RetentionContext();
        for (var index = 0; index < 5; index++)
            await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt);

        (await context.Service.RunAsync(globalDays: 1, FinishedAt.AddDays(10), batchSize: 2)).Should().Be(2);
        (await context.Storage.InstanceStorage.GetAllFinishedInstances()).Should().HaveCount(3);

        (await context.Service.RunAsync(globalDays: 1, FinishedAt.AddDays(10), batchSize: 2)).Should().Be(2);
        (await context.Service.RunAsync(globalDays: 1, FinishedAt.AddDays(10), batchSize: 2)).Should().Be(1);
        (await context.Storage.InstanceStorage.GetAllFinishedInstances()).Should().BeEmpty();
    }

    // Testzweck: Eine laufende Instanz wird nie geloescht, egal wie alt sie ist. Das ist die
    // wichtigste Zusage der Aufbewahrung; sie darf nicht von der Auswahlabfrage allein abhaengen.
    [Test]
    public async Task RunAsync_ShouldNeverTouchARunningInstance()
    {
        using var context = new RetentionContext();
        var running = await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt, finished: false);

        (await context.Service.RunAsync(globalDays: 1, FinishedAt.AddYears(10), batchSize: 100)).Should().Be(0);

        await InstancePurgeFixture.AssertIntactAsync(context.Storage, running);
    }

    // Testzweck: Ohne gesetzte Frist laeuft der Hintergrunddienst gar nicht erst an und meldet
    // das auch so. Ein Dienst, der ohne Auftrag zyklisch den Instanzbestand laedt, waere Last
    // ohne Zweck.
    [Test]
    public async Task BackgroundService_ShouldStayDisabledWithoutAConfiguredPeriod()
    {
        using var context = new RetentionContext();
        var diagnostics = new InstanceRetentionDiagnosticsState();
        using var service = context.CreateBackgroundService(new InstanceRetentionOptions { Days = null }, diagnostics);

        await service.StartAsync(CancellationToken.None);
        try
        {
            // Die Hintergrundschleife laeuft nebenlaeufig an; der Zustand wird deshalb
            // abgewartet und nicht sofort nach StartAsync abgelesen.
            Assert.That(() => diagnostics.GetSnapshot().Status, Is.EqualTo("Disabled").After(3000, 25));
            diagnostics.GetSnapshot().Enabled.Should().BeFalse();
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    // Testzweck: Der Hintergrunddienst loescht beim Start und meldet die Anzahl in die Diagnose.
    [Test]
    public async Task BackgroundService_ShouldDeleteOnStartAndReportTheCount()
    {
        using var context = new RetentionContext();
        var seeded = await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt);
        var diagnostics = new InstanceRetentionDiagnosticsState();
        context.Clock.SetUtcNow(FinishedAt.AddDays(40));
        using var service = context.CreateBackgroundService(
            new InstanceRetentionOptions { Days = 30, PollIntervalMinutes = 60, BatchSize = 100 }, diagnostics);

        await service.StartAsync(CancellationToken.None);
        try
        {
            Assert.That(() => diagnostics.GetSnapshot().Status, Is.EqualTo("Healthy").After(3000, 25));
            var snapshot = diagnostics.GetSnapshot();
            snapshot.Enabled.Should().BeTrue();
            snapshot.Days.Should().Be(30);
            snapshot.LastDeletedInstances.Should().Be(1);
            snapshot.TotalDeletedInstances.Should().Be(1);
            snapshot.SuccessfulRunCount.Should().Be(1);
            snapshot.LastErrorMessage.Should().BeNull();
            await InstancePurgeFixture.AssertPurgedAsync(context.Storage, seeded);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    // Testzweck: Ein gescheiterter Lauf bricht den Dienst nicht ab, sondern erscheint als
    // Stoerung in der Diagnose — und der naechste Lauf arbeitet wieder. Ohne das stuende die
    // Aufbewahrung nach der ersten kurzen Datenbankstoerung bis zum Neustart still.
    [Test]
    public async Task BackgroundService_ShouldSurviveAFailedRunAndRecoverOnTheNext()
    {
        using var context = new RetentionContext();
        await InstancePurgeFixture.SeedAsync(context.Storage, FinishedAt);
        var diagnostics = new InstanceRetentionDiagnosticsState();
        var failing = new FailingOnceStorageProvider(new FileSystemTransactionalStorageProvider());
        var service = new InstanceRetentionBackgroundService(
            new InstanceRetentionService(failing, context.Storage),
            context.Clock,
            Options.Create(new InstanceRetentionOptions { Days = 30, PollIntervalMinutes = 1, BatchSize = 100 }),
            diagnostics,
            NullLogger<InstanceRetentionBackgroundService>.Instance);
        context.Clock.SetUtcNow(FinishedAt.AddDays(40));

        await service.StartAsync(CancellationToken.None);
        try
        {
            Assert.That(() => diagnostics.GetSnapshot().Status, Is.EqualTo("Faulted").After(3000, 25));
            var failed = diagnostics.GetSnapshot();
            failed.FailedRunCount.Should().Be(1);
            failed.LastErrorMessage.Should().Contain("Ablage kurz nicht erreichbar");
            failed.SuccessfulRunCount.Should().Be(0);

            // Der naechste Takt laeuft wieder durch; der Dienst lebt also noch.
            context.Clock.Advance(TimeSpan.FromMinutes(1));
            Assert.That(() => diagnostics.GetSnapshot().Status, Is.EqualTo("Healthy").After(3000, 25));
            var recovered = diagnostics.GetSnapshot();
            recovered.SuccessfulRunCount.Should().Be(1);
            recovered.LastDeletedInstances.Should().Be(1);
            recovered.LastErrorMessage.Should().BeNull("ein geglueckter Lauf ueberholt die alte Stoerung");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
            service.Dispose();
        }
    }

    // Testzweck: Die Compose-Vorlage reicht `Retention__FinishedInstances__Days` ohne gesetzten
    // Wert als leere Zeichenkette durch. Die muss als „keine Frist" binden und nicht beim
    // Hoststart scheitern — sonst startet jede Installation nicht mehr, die die Aufbewahrung
    // gar nicht nutzt.
    [Test]
    public void Options_ShouldBindAnEmptyDaysVariableAsNoRetention()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Retention:FinishedInstances:Days"] = string.Empty,
                ["Retention:FinishedInstances:PollIntervalMinutes"] = "60",
                ["Retention:FinishedInstances:BatchSize"] = "100"
            })
            .Build();

        var options = new InstanceRetentionOptions();
        configuration.GetSection(InstanceRetentionOptions.SectionName).Bind(options);

        options.Days.Should().BeNull();
        options.IsEnabled.Should().BeFalse();
        options.IsValid().Should().BeTrue("eine nicht gesetzte Frist ist gueltig, nicht kaputt");
    }

    // Testzweck: Unbrauchbare Werte werden als solche gemeldet. Die Validierung laeuft beim
    // Hoststart; eine halb aktive Aufbewahrung waere schlimmer als ein klarer Startfehler.
    [Test]
    public void Options_ShouldRejectUnusableValues()
    {
        new InstanceRetentionOptions { Days = -1 }.IsValid().Should().BeFalse();
        new InstanceRetentionOptions { PollIntervalMinutes = 0 }.IsValid().Should().BeFalse();
        new InstanceRetentionOptions { BatchSize = 0 }.IsValid().Should().BeFalse();
        new InstanceRetentionOptions { Days = 30 }.IsValid().Should().BeTrue();
    }

    /// <summary>Scheitert genau beim ersten Zugriff und arbeitet danach normal weiter.</summary>
    private sealed class FailingOnceStorageProvider(ITransactionalStorageProvider inner) : ITransactionalStorageProvider
    {
        private int _calls;

        public ITransactionalStorage GetTransactionalStorage()
        {
            if (Interlocked.Increment(ref _calls) == 1)
                throw new InvalidOperationException("Ablage kurz nicht erreichbar.");
            return inner.GetTransactionalStorage();
        }
    }

    private sealed class RetentionContext : IDisposable
    {
        private readonly string? _originalRoot;
        private readonly string _tempRoot;

        public RetentionContext()
        {
            _originalRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            _tempRoot = Path.Combine(Path.GetTempPath(), "flowzer-retention-test", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _tempRoot);
            Storage = new Storage();
            Clock = new FakeTimeProvider(new DateTimeOffset(FinishedAt));
            Service = new InstanceRetentionService(new FileSystemTransactionalStorageProvider(), Storage);
        }

        public Storage Storage { get; }
        public FakeTimeProvider Clock { get; }
        public InstanceRetentionService Service { get; }

        /// <summary>Setzt das Workflow-Metadatum, das die globale Frist ueberschreibt.</summary>
        public Task SetRetentionAsync(string definitionId, int? retentionDays) =>
            Storage.DefinitionStorage.StoreMetaDefinition(new BpmnMetaDefinition
            {
                DefinitionId = definitionId,
                Name = definitionId,
                RetentionDays = retentionDays
            });

        public InstanceRetentionBackgroundService CreateBackgroundService(
            InstanceRetentionOptions options,
            InstanceRetentionDiagnosticsState diagnostics) =>
            new(Service, Clock, Options.Create(options), diagnostics,
                NullLogger<InstanceRetentionBackgroundService>.Instance);

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _originalRoot);
            if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
