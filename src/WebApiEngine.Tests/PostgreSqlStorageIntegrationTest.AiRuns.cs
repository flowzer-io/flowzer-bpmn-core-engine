using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Model;
using PostgreSqlStorageSystem;
using StorageSystem;
using WebApiEngine.Ai;

namespace WebApiEngine.Tests;

public partial class PostgreSqlStorageIntegrationTest
{
    private static readonly DateTime AiRunNow = new(2026, 9, 9, 18, 0, 0, DateTimeKind.Utc);

    // Testzweck: Zwei PostgreSQL-Prozesse claimen dank Zeilensperre und SKIP LOCKED niemals
    // denselben KI-Lauf, sondern erhalten bei ausreichendem Vorrat verschiedene Tokens.
    [Test]
    public async Task AiRunStorage_ShouldClaimDistinctRunsAcrossProcesses()
    {
        var first = new PostgreSqlStorage(_dataSource!, Schema);
        var second = new PostgreSqlStorage(_dataSource!, Schema);
        var runA = RandomRun();
        var runB = RandomRun();
        await first.AiRunStorage.TryCreate(runA);
        await first.AiRunStorage.TryCreate(runB);

        var claims = await Task.WhenAll(
            first.AiRunStorage.ClaimProviderRuns("node-a", AiRunNow, AiRunNow.AddMinutes(5), 1),
            second.AiRunStorage.ClaimProviderRuns("node-b", AiRunNow, AiRunNow.AddMinutes(5), 1));

        claims.SelectMany(result => result).Should().HaveCount(2)
            .And.OnlyHaveUniqueItems(run => run.Id);
        claims.Should().OnlyContain(result => result.Count == 1);
    }

    // Testzweck: PostgreSQL erzwingt Token-Eindeutigkeit und CAS auch über getrennte
    // Adapterinstanzen; ein veralteter Schreiber überschreibt kein Providerergebnis.
    [Test]
    public async Task AiRunStorage_ShouldEnforceUniqueTokenAndRevisionAcrossProcesses()
    {
        var first = new PostgreSqlStorage(_dataSource!, Schema);
        var second = new PostgreSqlStorage(_dataSource!, Schema);
        var initial = RandomRun();
        (await first.AiRunStorage.TryCreate(initial)).Status.Should().Be(AiRunWriteStatus.Written);
        var duplicate = await second.AiRunStorage.TryCreate(
            RandomRun() with { ProcessInstanceId = initial.ProcessInstanceId, TokenId = initial.TokenId });
        var claimed = (await first.AiRunStorage.ClaimProviderRuns(
            "node-a", AiRunNow, AiRunNow.AddMinutes(5), 1)).Single();
        var started = claimed with
        {
            Attempt = 1,
            ProviderCallStartedAtUtc = AiRunNow.AddSeconds(1),
            Revision = claimed.Revision + 1,
            UpdatedAtUtc = AiRunNow.AddSeconds(1)
        };
        var written = await first.AiRunStorage.TryUpdate(
            started,
            claimed.Revision,
            "node-a",
            AiRunNow.AddSeconds(1));
        var stale = await second.AiRunStorage.TryUpdate(
            started with { Revision = started.Revision + 1 },
            claimed.Revision,
            "node-a",
            AiRunNow.AddSeconds(2));

        duplicate.Status.Should().Be(AiRunWriteStatus.Conflict);
        duplicate.Current!.Id.Should().Be(initial.Id);
        written.Status.Should().Be(AiRunWriteStatus.Written);
        stale.Status.Should().Be(AiRunWriteStatus.Conflict);
        stale.CurrentRevision.Should().Be(started.Revision);
    }

    // Testzweck: Ein validiertes Providerergebnis bleibt in den querybaren PostgreSQL-
    // Zustandsfeldern vollständig erhalten und kann anschließend genau einmal geleast werden.
    [Test]
    public async Task AiRunStorage_ShouldPersistAndClaimReadyResult()
    {
        var writer = new PostgreSqlStorage(_dataSource!, Schema);
        var reader = new PostgreSqlStorage(_dataSource!, Schema);
        var initial = RandomRun() with { MaximumAttempts = 1 };
        await writer.AiRunStorage.TryCreate(initial);
        var claimed = (await writer.AiRunStorage.ClaimProviderRuns(
            "provider", AiRunNow, AiRunNow.AddMinutes(5), 1)).Single();
        var started = claimed with
        {
            Attempt = 1,
            ProviderCallStartedAtUtc = AiRunNow.AddSeconds(1),
            Revision = claimed.Revision + 1,
            UpdatedAtUtc = AiRunNow.AddSeconds(1)
        };
        await writer.AiRunStorage.TryUpdate(started, claimed.Revision, "provider", AiRunNow.AddSeconds(1));
        var ready = started with
        {
            Status = AiRunStatus.ResultReady,
            LeaseOwner = null,
            LeaseExpiresAtUtc = null,
            OutputJson = "{}",
            ResultModel = "result-model",
            InputTokens = 8,
            OutputTokens = 2,
            TotalTokens = 10,
            Revision = started.Revision + 1,
            UpdatedAtUtc = AiRunNow.AddSeconds(2)
        };

        (await writer.AiRunStorage.TryUpdate(ready, started.Revision, "provider", AiRunNow.AddSeconds(2)))
            .Status.Should().Be(AiRunWriteStatus.Written);
        (await reader.AiRunStorage.Get(initial.Id)).Should().Be(ready);
        var engineClaim = await reader.AiRunStorage.ClaimResultRuns(
            "engine", AiRunNow.AddSeconds(3), AiRunNow.AddMinutes(5), 1);

        engineClaim.Should().ContainSingle().Which.Status.Should().Be(AiRunStatus.Completing);
        (await writer.AiRunStorage.ClaimResultRuns(
            "other", AiRunNow.AddSeconds(3), AiRunNow.AddMinutes(5), 1)).Should().BeEmpty();
    }

    // Testzweck: Zwei parallele KI-Ergebnisse derselben Prozessinstanz werden nicht von
    // verschiedenen API-Prozessen gleichzeitig uebernommen; sonst koennten beide einen alten
    // Instanzstand speichern und jeweils den Fortschritt des anderen ueberschreiben.
    [Test]
    public async Task AiRunStorage_ShouldSerializeResultClaimsPerProcessInstance()
    {
        var processInstanceId = Guid.NewGuid();
        var firstRun = RandomRun() with { ProcessInstanceId = processInstanceId };
        var secondRun = RandomRun() with { ProcessInstanceId = processInstanceId };
        var writer = new PostgreSqlStorage(_dataSource!, Schema);
        await MakeReady(writer.AiRunStorage, firstRun, "provider-a");
        await MakeReady(writer.AiRunStorage, secondRun, "provider-b");
        using var first = new PostgreSqlTransactionalStorage(_dataSource!, Schema);
        using var second = new PostgreSqlTransactionalStorage(_dataSource!, Schema);

        var firstClaim = await first.AiRunStorage.ClaimResultRuns(
            "engine-a", AiRunNow.AddSeconds(3), AiRunNow.AddMinutes(5), 1);
        var competingClaim = await second.AiRunStorage.ClaimResultRuns(
            "engine-b", AiRunNow.AddSeconds(3), AiRunNow.AddMinutes(5), 1);

        firstClaim.Should().ContainSingle();
        competingClaim.Should().BeEmpty();
        first.CommitChanges();
        (await second.AiRunStorage.ClaimResultRuns(
            "engine-b", AiRunNow.AddSeconds(4), AiRunNow.AddMinutes(5), 1)).Should().ContainSingle();
        second.RollbackTransaction();
    }

    // Testzweck: Auch ein einzelner PostgreSQL-Engine-Batch claimt höchstens einen Lauf je
    // Instanz, damit ein früher Ergebnisfortschritt keinen bereits mitgeclaimten Lauf entzieht.
    [Test]
    public async Task AiRunStorage_ShouldClaimAtMostOneReadyResultPerProcessInstance()
    {
        var processInstanceId = Guid.NewGuid();
        var firstRun = RandomRun() with { ProcessInstanceId = processInstanceId };
        var secondRun = RandomRun() with { ProcessInstanceId = processInstanceId };
        var storage = new PostgreSqlStorage(_dataSource!, Schema);
        await MakeReady(storage.AiRunStorage, firstRun, "provider-a");
        await MakeReady(storage.AiRunStorage, secondRun, "provider-b");

        var results = await storage.AiRunStorage.ClaimResultRuns(
            "engine", AiRunNow.AddSeconds(3), AiRunNow.AddMinutes(5), 10);

        results.Should().ContainSingle();
    }

    // Testzweck: PostgreSQL-Recovery unterscheidet selbst nach Prozessneustart eine Lease vor
    // dem Aufruf von einem unklar ausgegangenen externen Aufruf und wiederholt nur erstere.
    [Test]
    public async Task AiRunStorage_ShouldRecoverExpiredLeasesWithoutRetryingAmbiguousCall()
    {
        var first = new PostgreSqlStorage(_dataSource!, Schema);
        var second = new PostgreSqlStorage(_dataSource!, Schema);
        var beforeCall = RandomRun();
        var duringCall = RandomRun();
        await first.AiRunStorage.TryCreate(beforeCall);
        await first.AiRunStorage.TryCreate(duringCall);
        var claimed = await first.AiRunStorage.ClaimProviderRuns(
            "node-a", AiRunNow, AiRunNow.AddMinutes(5), 2);
        var call = claimed.Single(run => run.Id == duringCall.Id);
        var started = call with
        {
            Attempt = 1,
            ProviderCallStartedAtUtc = AiRunNow.AddSeconds(1),
            Revision = call.Revision + 1,
            UpdatedAtUtc = AiRunNow.AddSeconds(1)
        };
        await first.AiRunStorage.TryUpdate(started, call.Revision, "node-a", AiRunNow.AddSeconds(1));

        var recovered = await second.AiRunStorage.RecoverExpiredLeases(AiRunNow.AddMinutes(6), 10);

        recovered.Single(run => run.Id == beforeCall.Id).Status.Should().Be(AiRunStatus.Pending);
        recovered.Single(run => run.Id == duringCall.Id).Should().Match<AiRun>(run =>
            run.Status == AiRunStatus.Incident && run.FailureCode == "ai.run.outcome_unknown");
    }

    // Testzweck: PostgreSQL erhöht beim Entzug eines veralteten KI-Laufs die Revision,
    // setzt dessen Aktualisierungszeit aber auch bei einer zurückliegenden Engine-Uhr nicht zurück.
    [Test]
    public async Task AiRunStorage_ShouldKeepCancellationTimestampMonotonic()
    {
        var storage = new PostgreSqlStorage(_dataSource!, Schema).AiRunStorage;
        var run = RandomRun() with
        {
            CreatedAtUtc = AiRunNow.AddMinutes(1),
            UpdatedAtUtc = AiRunNow.AddMinutes(1)
        };
        await storage.TryCreate(run);

        await storage.CancelObsoleteRuns(run.ProcessInstanceId, [], AiRunNow);

        var cancelled = (await storage.Get(run.Id))!;
        cancelled.Status.Should().Be(AiRunStatus.Cancelled);
        cancelled.UpdatedAtUtc.Should().Be(AiRunNow.AddMinutes(1));
    }

    // Testzweck: Zwei getrennte Executor mit eigenen PostgreSQL-Adaptern koennen denselben
    // Providerlauf auch dann nicht doppelt starten, wenn der erste Aufruf noch aktiv ist.
    [Test]
    public async Task AiRunExecutor_ShouldClaimProviderCallOnceAcrossPostgreSqlProcesses()
    {
        var firstStorage = new PostgreSqlStorage(_dataSource!, Schema).AiRunStorage;
        var secondStorage = new PostgreSqlStorage(_dataSource!, Schema).AiRunStorage;
        var initial = RandomRun();
        await firstStorage.TryCreate(initial);
        var gateway = new CoordinatedAiInferenceGateway();
        var time = new FakeTimeProvider(new DateTimeOffset(AiRunNow));
        var policy = new AiRunExecutionPolicy(
            Enabled: true,
            PollInterval: TimeSpan.FromSeconds(5),
            BatchSize: 1,
            LeaseDuration: TimeSpan.FromMinutes(5),
            HeartbeatInterval: TimeSpan.FromMinutes(1),
            RetryBaseDelay: TimeSpan.FromMinutes(1),
            MaximumRetryDelay: TimeSpan.FromMinutes(10));
        var first = new AiRunExecutor(
            firstStorage,
            gateway,
            time,
            policy,
            NullLogger<AiRunExecutor>.Instance,
            "postgres-a");
        var second = new AiRunExecutor(
            secondStorage,
            gateway,
            time,
            policy,
            NullLogger<AiRunExecutor>.Instance,
            "postgres-b");

        var firstTick = first.RunProviderBatchAsync(default);
        await gateway.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondTick = await second.RunProviderBatchAsync(default);
        gateway.Release.TrySetResult();
        var firstResult = await firstTick;

        firstResult.Claimed.Should().Be(1);
        secondTick.Claimed.Should().Be(0);
        gateway.Calls.Should().Be(1);
        (await secondStorage.Get(initial.Id))!.Status.Should().Be(AiRunStatus.ResultReady);
    }

    private static AiRun RandomRun() => AiRunStorageTest.Run() with
    {
        Id = Guid.NewGuid(),
        ProcessInstanceId = Guid.NewGuid(),
        TokenId = Guid.NewGuid(),
        CreatedAtUtc = AiRunNow,
        UpdatedAtUtc = AiRunNow
    };

    private static async Task MakeReady(IAiRunStorage storage, AiRun initial, string owner)
    {
        await storage.TryCreate(initial);
        var claimed = (await storage.ClaimProviderRuns(
            owner,
            AiRunNow,
            AiRunNow.AddMinutes(5),
            1)).Single(run => run.Id == initial.Id);
        var started = claimed with
        {
            Attempt = 1,
            ProviderCallStartedAtUtc = AiRunNow.AddSeconds(1),
            Revision = claimed.Revision + 1,
            UpdatedAtUtc = AiRunNow.AddSeconds(1)
        };
        await storage.TryUpdate(started, claimed.Revision, owner, AiRunNow.AddSeconds(1));
        var ready = started with
        {
            Status = AiRunStatus.ResultReady,
            LeaseOwner = null,
            LeaseExpiresAtUtc = null,
            OutputJson = "{}",
            ResultModel = "result-model",
            InputTokens = 8,
            OutputTokens = 2,
            TotalTokens = 10,
            Revision = started.Revision + 1,
            UpdatedAtUtc = AiRunNow.AddSeconds(2)
        };
        await storage.TryUpdate(ready, started.Revision, owner, AiRunNow.AddSeconds(2));
    }

    private sealed class CoordinatedAiInferenceGateway : IAiInferenceGateway
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AiInferenceResult> ExecuteAsync(
            AiInferenceCommand command,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            using var output = JsonDocument.Parse("{}");
            return new AiInferenceResult(
                output.RootElement.Clone(),
                "result-model",
                new AiTokenUsage(8, 2, 10));
        }
    }
}
