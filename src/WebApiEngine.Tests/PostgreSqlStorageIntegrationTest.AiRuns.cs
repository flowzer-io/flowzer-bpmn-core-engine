using FluentAssertions;
using Model;
using PostgreSqlStorageSystem;
using StorageSystem;

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

    private static AiRun RandomRun() => AiRunStorageTest.Run() with
    {
        Id = Guid.NewGuid(),
        ProcessInstanceId = Guid.NewGuid(),
        TokenId = Guid.NewGuid(),
        CreatedAtUtc = AiRunNow,
        UpdatedAtUtc = AiRunNow
    };
}
