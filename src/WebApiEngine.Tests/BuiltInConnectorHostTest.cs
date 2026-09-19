using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using WebApiEngine.Background;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Connectors;
using WebApiEngine.Jobs;
using Model;
using Variables = System.Dynamic.ExpandoObject;

namespace WebApiEngine.Tests;

/// <summary>
/// Der Host der mitgelieferten Konnektoren. Er holt und meldet ueber dieselben Mechanismen wie
/// ein externer Worker; die Engine kennt keinen Sonderpfad fuer eingebaute Arbeit.
/// </summary>
[NonParallelizable]
public class BuiltInConnectorHostTest
{
    // Testzweck: Ein abgeschalteter Konnektor holt keinen einzigen Auftrag. Opt-in heisst, dass
    // ohne Freigabe nichts laeuft — auch nicht versehentlich ueber den Host.
    [Test]
    public async Task RunOnce_ShouldNotTouchJobs_WhenTheConnectorIsDisabled()
    {
        var context = new HostContext();
        var connector = new StubConnector("stub", enabled: false, _ => new ConnectorOutcome.Completed(null));
        var job = await context.AddJob(connector.JobType);

        await context.Host(connector).RunOnce(connector, CancellationToken.None);

        connector.Calls.Should().Be(0);
        (await context.Service.GetAll()).Single().Id.Should().Be(job.Id);
        (await context.Service.GetAll()).Single().LockedBy.Should().BeNull();
    }

    // Testzweck: Der Auftrag wird unter der festen, dokumentierten Konnektorkennung uebernommen.
    // Sonst haette die Sperre keinen nachvollziehbaren Inhaber, und ein beliebiger Worker
    // koennte sie bedienen.
    [Test]
    public async Task RunOnce_ShouldClaimTheJobUnderTheBuiltInConnectorIdentity()
    {
        var context = new HostContext();
        string? owner = null;
        var connector = new StubConnector("stub", enabled: true, job =>
        {
            owner = job.LockedBy;
            return new ConnectorOutcome.Failed("fertig", TimeSpan.Zero);
        });
        await context.AddJob(connector.JobType);

        await context.Host(connector).RunOnce(connector, CancellationToken.None);

        connector.Calls.Should().Be(1);
        owner.Should().Be(ServiceTaskJobService.BuildLockOwner(
            BuiltInConnectorIdentity.UserId, "flowzer-connector-stub"));
    }

    // Testzweck: Ein technischer Fehlschlag senkt die Versuche und legt die Wartezeit an; der
    // Auftrag bleibt wiederholbar, statt still zu verschwinden.
    [Test]
    public async Task RunOnce_ShouldFailTheJobWithBackoff_AndCountIt()
    {
        var context = new HostContext();
        var connector = new StubConnector("stub", enabled: true,
            _ => new ConnectorOutcome.Failed("Endpunkt nicht erreichbar", TimeSpan.FromSeconds(30)));
        await context.AddJob(connector.JobType, retries: 3);

        await context.Host(connector).RunOnce(connector, CancellationToken.None);

        var stored = (await context.Service.GetAll()).Single();
        stored.Retries.Should().Be(2);
        stored.RetryAt.Should().Be(context.Time.GetUtcNow().UtcDateTime.AddSeconds(30));
        stored.LastErrorMessage.Should().Be("Endpunkt nicht erreichbar");
        var snapshot = context.Diagnostics.GetSnapshot().Single();
        snapshot.FailedJobs.Should().Be(1);
        snapshot.LastErrorMessage.Should().Be("Endpunkt nicht erreichbar");
    }

    // Testzweck: Wirft ein Konnektor eine unerwartete Ausnahme, bleibt der Dienst am Leben und
    // der Auftrag wiederholbar. Sonst stuende nach einem einzelnen kaputten Auftrag die ganze
    // Auftragsbearbeitung still.
    [Test]
    public async Task RunOnce_ShouldSurviveAThrowingConnector()
    {
        var context = new HostContext();
        var connector = new StubConnector("stub", enabled: true,
            _ => throw new InvalidOperationException("kaputt"));
        await context.AddJob(connector.JobType, retries: 2);
        var host = context.Host(connector);

        var act = async () => await host.RunOnce(connector, CancellationToken.None);

        await act.Should().NotThrowAsync();
        (await context.Service.GetAll()).Single().Retries.Should().Be(1);
        context.Diagnostics.GetSnapshot().Single().FailedJobs.Should().Be(1);
        // Der naechste Durchgang nimmt denselben Auftrag wieder auf.
        context.Time.Advance(TimeSpan.FromMinutes(1));
        await host.RunOnce(connector, CancellationToken.None);
        connector.Calls.Should().Be(2);
    }

    // Testzweck: Die rohe Ausnahmemeldung eines Konnektors gehoert nicht in die Betriebssicht;
    // sie koennte Adressen oder aufgeloeste Werte tragen.
    [Test]
    public async Task RunOnce_ShouldNotPutRawExceptionTextIntoTheDiagnostics()
    {
        var context = new HostContext();
        var connector = new StubConnector("stub", enabled: true,
            _ => throw new InvalidOperationException("geheim-123"));
        await context.AddJob(connector.JobType);

        await context.Host(connector).RunOnce(connector, CancellationToken.None);

        context.Diagnostics.GetSnapshot().Single().LastErrorMessage.Should().NotContain("geheim-123");
    }

    // Testzweck: Abgeschaltete Konnektoren stehen mit im Betriebsbild. „Nicht aktiviert“ ist
    // eine Aussage; „gar nicht aufgefuehrt“ waere keine.
    [Test]
    public async Task ExecuteAsync_ShouldReportEveryConnectorIncludingTheDisabledOnes()
    {
        var context = new HostContext();
        var disabled = new StubConnector("stub", enabled: false, _ => new ConnectorOutcome.Completed(null));
        using var host = context.Host(disabled);

        await host.StartAsync(CancellationToken.None);
        await host.StopAsync(CancellationToken.None);

        var snapshot = context.Diagnostics.GetSnapshot().Single();
        snapshot.Name.Should().Be("stub");
        snapshot.JobType.Should().Be("flowzer:stub");
        snapshot.Enabled.Should().BeFalse();
    }

    private sealed class HostContext
    {
        public HostContext()
        {
            Time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero));
            Storage = new InMemoryServiceTaskStorage();
            var provider = new ServiceTaskOnlyProvider(Storage);
            Service = new ServiceTaskJobService(
                provider,
                new BpmnBusinessLogic(provider),
                Time,
                NullLogger<ServiceTaskJobService>.Instance);
        }

        public FakeTimeProvider Time { get; }
        public InMemoryServiceTaskStorage Storage { get; }
        public ServiceTaskJobService Service { get; }
        public ConnectorDiagnosticsState Diagnostics { get; } = new();

        public BuiltInConnectorBackgroundService Host(params IBuiltInConnector[] connectors) => new(
            connectors,
            Service,
            new FlowzerConnectorOptions(),
            Diagnostics,
            Time,
            NullLogger<BuiltInConnectorBackgroundService>.Instance);

        public async Task<ServiceTaskJob> AddJob(string type, int retries = 3)
        {
            var job = ConnectorTestData.Job(type, null, retries);
            job.CreatedAt = Time.GetUtcNow().UtcDateTime;
            await Storage.SaveJob(job);
            return job;
        }
    }

    /// <summary>
    /// Ein Konnektor, der nur festhaelt, dass er aufgerufen wurde. Der Host soll unabhaengig
    /// von HTTP und SMTP pruefbar sein.
    /// </summary>
    private sealed class StubConnector(string name, bool enabled, Func<ServiceTaskJob, ConnectorOutcome> run)
        : IBuiltInConnector
    {
        public int Calls { get; private set; }

        public string JobType => $"flowzer:{name}";

        public string Name => name;

        public bool Enabled => enabled;

        public Task<ConnectorOutcome> Execute(ServiceTaskJob job, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(run(job));
        }
    }
}
