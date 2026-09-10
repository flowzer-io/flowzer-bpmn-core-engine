using FilesystemStorageSystem;
using FluentAssertions;
using Model;
using StorageSystem;

namespace WebApiEngine.Tests;

/// <summary>Dauerhafter KI-Laufvertrag der Einzelprozess-Entwicklungsablage.</summary>
[NonParallelizable]
public sealed class AiRunStorageTest
{
    private static readonly DateTime Now = new(2026, 9, 9, 18, 0, 0, DateTimeKind.Utc);

    // Testzweck: Je wartendem Engine-Token entsteht genau ein stabiler Lauf; ein erneuter
    // Anlageversuch liefert den vorhandenen Stand statt einen zweiten Providerauftrag.
    [Test]
    public async Task FilesystemStorage_ShouldCreateOneAiRunPerToken()
    {
        using var context = new Context();
        var initial = Run();

        var created = await context.Storage.AiRunStorage.TryCreate(initial);
        var duplicate = await context.Storage.AiRunStorage.TryCreate(
            Run() with { Id = Guid.NewGuid(), ProcessInstanceId = initial.ProcessInstanceId, TokenId = initial.TokenId });

        created.Status.Should().Be(AiRunWriteStatus.Written);
        duplicate.Status.Should().Be(AiRunWriteStatus.Conflict);
        duplicate.Current.Should().Be(initial);
        (await context.Storage.AiRunStorage.List()).Should().ContainSingle();
    }

    // Testzweck: Claim, Aufrufbeginn und Providerergebnis sind revisions- und leasegebunden;
    // ein fremder oder veralteter Schreiber kann den Lauf nicht mehr verändern.
    [Test]
    public async Task FilesystemStorage_ShouldProtectAiRunTransitionsByRevisionAndLease()
    {
        using var context = new Context();
        await context.Storage.AiRunStorage.TryCreate(Run());
        var claimed = (await context.Storage.AiRunStorage.ClaimProviderRuns(
            "node-a", Now, Now.AddMinutes(5), 1)).Single();
        var started = MarkStarted(claimed, Now.AddSeconds(1));

        (await context.Storage.AiRunStorage.TryUpdate(started, claimed.Revision, "node-a", Now.AddSeconds(1)))
            .Status.Should().Be(AiRunWriteStatus.Written);
        (await context.Storage.AiRunStorage.TryUpdate(
                started with { Revision = started.Revision + 1 },
                claimed.Revision,
                "node-a",
                Now.AddSeconds(2)))
            .Status.Should().Be(AiRunWriteStatus.Conflict);
        (await context.Storage.AiRunStorage.TryUpdate(
                Ready(started, Now.AddSeconds(2)),
                started.Revision,
                "node-b",
                Now.AddSeconds(2)))
            .Status.Should().Be(AiRunWriteStatus.LeaseLost);
        (await context.Storage.AiRunStorage.TryUpdate(
                Ready(started, Now.AddMinutes(6)),
                started.Revision,
                "node-a",
                Now.AddMinutes(6)))
            .Status.Should().Be(AiRunWriteStatus.LeaseLost);
    }

    // Testzweck: Ein Heartbeat verlängert nur die aktuelle eigene Lease und ändert dabei
    // die Revision; fremde, veraltete oder bereits abgelaufene Heartbeats bleiben wirkungslos.
    [Test]
    public async Task FilesystemStorage_ShouldRenewOnlyCurrentAiRunLease()
    {
        using var context = new Context();
        await context.Storage.AiRunStorage.TryCreate(Run());
        var claimed = (await context.Storage.AiRunStorage.ClaimProviderRuns(
            "node-a", Now, Now.AddMinutes(5), 1)).Single();

        var renewed = await context.Storage.AiRunStorage.RenewLease(
            claimed.Id, claimed.Revision, "node-a", Now.AddMinutes(1), Now.AddMinutes(20));
        var stale = await context.Storage.AiRunStorage.RenewLease(
            claimed.Id, claimed.Revision, "node-a", Now.AddMinutes(2), Now.AddMinutes(30));
        var foreign = await context.Storage.AiRunStorage.RenewLease(
            claimed.Id, renewed!.Revision, "node-b", Now.AddMinutes(2), Now.AddMinutes(30));
        var expired = await context.Storage.AiRunStorage.RenewLease(
            claimed.Id, renewed.Revision, "node-a", Now.AddMinutes(21), Now.AddMinutes(30));

        renewed.LeaseExpiresAtUtc.Should().Be(Now.AddMinutes(20));
        renewed.Revision.Should().Be(claimed.Revision + 1);
        stale.Should().BeNull();
        foreign.Should().BeNull();
        expired.Should().BeNull();
    }

    // Testzweck: Ohne markierten Provideraufruf darf weder ein Ergebnis erscheinen noch der
    // unveränderliche Modell-/Prompt-Snapshot während eines Laufs ausgetauscht werden.
    [Test]
    public async Task FilesystemStorage_ShouldRejectInvalidAiRunTransition()
    {
        using var context = new Context();
        await context.Storage.AiRunStorage.TryCreate(Run());
        var claimed = (await context.Storage.AiRunStorage.ClaimProviderRuns(
            "node-a", Now, Now.AddMinutes(5), 1)).Single();

        var resultBeforeCall = () => context.Storage.AiRunStorage.TryUpdate(
            Ready(claimed, Now.AddSeconds(1)),
            claimed.Revision,
            "node-a",
            Now.AddSeconds(1));
        var changedSnapshot = () => context.Storage.AiRunStorage.TryUpdate(
            MarkStarted(claimed, Now.AddSeconds(1)) with { Model = "different-model" },
            claimed.Revision,
            "node-a",
            Now.AddSeconds(1));

        await resultBeforeCall.Should().ThrowAsync<ArgumentException>();
        await changedSnapshot.Should().ThrowAsync<ArgumentException>();
        (await context.Storage.AiRunStorage.Get(claimed.Id)).Should().Be(claimed);
    }

    // Testzweck: Ein lokal erneut geöffneter Storage liest das validierte Ergebnis samt
    // Modellkennung und Tokenmessung, ohne Secret oder Secret-Referenz zu persistieren.
    [Test]
    public async Task FilesystemStorage_ShouldPersistProviderResultWithoutSecretMaterial()
    {
        using var context = new Context();
        var initial = Run();
        await context.Storage.AiRunStorage.TryCreate(initial);
        var claimed = (await context.Storage.AiRunStorage.ClaimProviderRuns(
            "node-a", Now, Now.AddMinutes(5), 1)).Single();
        var started = MarkStarted(claimed, Now.AddSeconds(1));
        await context.Storage.AiRunStorage.TryUpdate(started, claimed.Revision, "node-a", Now.AddSeconds(1));
        var ready = Ready(started, Now.AddSeconds(2));

        (await context.Storage.AiRunStorage.TryUpdate(ready, started.Revision, "node-a", Now.AddSeconds(2)))
            .Status.Should().Be(AiRunWriteStatus.Written);

        var reopened = new Storage();
        var loaded = await reopened.AiRunStorage.Get(initial.Id);
        loaded.Should().Be(ready);
        var persisted = Directory.GetFiles(context.Root, "*.json", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .Single(content => content.Contains(initial.Id.ToString(), StringComparison.OrdinalIgnoreCase));
        persisted.ToLowerInvariant().Should().NotContain("secret");
    }

    // Testzweck: Recovery gibt eine vor dem externen Aufruf verlorene Lease erneut frei,
    // hält einen unklar ausgegangenen Provideraufruf aber ohne automatische Wiederholung an.
    [Test]
    public async Task FilesystemStorage_ShouldRecoverExpiredAiRunLeasesWithoutBlindRetry()
    {
        using var context = new Context();
        var beforeCall = Run();
        var duringCall = Run() with { Id = Guid.NewGuid(), ProcessInstanceId = Guid.NewGuid(), TokenId = Guid.NewGuid() };
        await context.Storage.AiRunStorage.TryCreate(beforeCall);
        await context.Storage.AiRunStorage.TryCreate(duringCall);
        var claimed = await context.Storage.AiRunStorage.ClaimProviderRuns(
            "node-a", Now, Now.AddMinutes(5), 2);
        var started = MarkStarted(claimed.Single(run => run.Id == duringCall.Id), Now.AddSeconds(1));
        await context.Storage.AiRunStorage.TryUpdate(
            started,
            started.Revision - 1,
            "node-a",
            Now.AddSeconds(1));

        var recovered = await context.Storage.AiRunStorage.RecoverExpiredLeases(Now.AddMinutes(6), 10);

        recovered.Single(run => run.Id == beforeCall.Id).Status.Should().Be(AiRunStatus.Pending);
        var ambiguous = recovered.Single(run => run.Id == duringCall.Id);
        ambiguous.Status.Should().Be(AiRunStatus.Incident);
        ambiguous.FailureCode.Should().Be("ai.run.outcome_unknown");
        (await context.Storage.AiRunStorage.ClaimProviderRuns(
            "node-b", Now.AddMinutes(6), Now.AddMinutes(11), 10))
            .Should().ContainSingle().Which.Id.Should().Be(beforeCall.Id);
    }

    // Testzweck: Nur ein eindeutig klassifizierter Fehlschlag mit verbleibendem Budget darf
    // zeitgesteuert erneut vorgemerkt werden; vor dem Termin bleibt der Lauf unsichtbar.
    [Test]
    public async Task FilesystemStorage_ShouldScheduleOnlyBoundedKnownRetry()
    {
        using var context = new Context();
        await context.Storage.AiRunStorage.TryCreate(Run() with { MaximumAttempts = 2 });
        var claimed = (await context.Storage.AiRunStorage.ClaimProviderRuns(
            "node-a", Now, Now.AddMinutes(5), 1)).Single();
        var started = MarkStarted(claimed, Now.AddSeconds(1));
        await context.Storage.AiRunStorage.TryUpdate(started, claimed.Revision, "node-a", Now.AddSeconds(1));
        var retry = started with
        {
            Status = AiRunStatus.RetryScheduled,
            ProviderCallStartedAtUtc = null,
            LeaseOwner = null,
            LeaseExpiresAtUtc = null,
            NextAttemptAtUtc = Now.AddMinutes(2),
            FailureCode = "ai.provider.rate_limited",
            Revision = started.Revision + 1,
            UpdatedAtUtc = Now.AddSeconds(2)
        };

        (await context.Storage.AiRunStorage.TryUpdate(retry, started.Revision, "node-a", Now.AddSeconds(2)))
            .Status.Should().Be(AiRunWriteStatus.Written);
        (await context.Storage.AiRunStorage.ClaimProviderRuns(
            "node-b", Now.AddMinutes(1), Now.AddMinutes(5), 1)).Should().BeEmpty();
        var next = await context.Storage.AiRunStorage.ClaimProviderRuns(
            "node-b", Now.AddMinutes(2), Now.AddMinutes(5), 1);

        next.Should().ContainSingle().Which.Attempt.Should().Be(1);
    }

    // Testzweck: Ein bereitliegendes Ergebnis wird getrennt vom Providerclaim genau einmal
    // für den späteren Engine-Commit geleast.
    [Test]
    public async Task FilesystemStorage_ShouldClaimReadyResultOnlyOnce()
    {
        using var context = new Context();
        await context.Storage.AiRunStorage.TryCreate(Run() with { MaximumAttempts = 1 });
        var claimed = (await context.Storage.AiRunStorage.ClaimProviderRuns(
            "provider", Now, Now.AddMinutes(5), 1)).Single();
        var started = MarkStarted(claimed, Now.AddSeconds(1));
        await context.Storage.AiRunStorage.TryUpdate(started, claimed.Revision, "provider", Now.AddSeconds(1));
        var ready = Ready(started, Now.AddSeconds(2));
        await context.Storage.AiRunStorage.TryUpdate(ready, started.Revision, "provider", Now.AddSeconds(2));

        var first = await context.Storage.AiRunStorage.ClaimResultRuns(
            "engine-a", Now.AddSeconds(3), Now.AddMinutes(5), 1);
        var second = await context.Storage.AiRunStorage.ClaimResultRuns(
            "engine-b", Now.AddSeconds(3), Now.AddMinutes(5), 1);

        var completing = first.Should().ContainSingle().Which;
        completing.Status.Should().Be(AiRunStatus.Completing);
        second.Should().BeEmpty();
        var completed = completing with
        {
            Status = AiRunStatus.Completed,
            LeaseOwner = null,
            LeaseExpiresAtUtc = null,
            Revision = completing.Revision + 1,
            UpdatedAtUtc = Now.AddSeconds(4)
        };
        (await context.Storage.AiRunStorage.TryUpdate(
            completed,
            completing.Revision,
            "engine-a",
            Now.AddSeconds(4))).Status.Should().Be(AiRunWriteStatus.Written);
    }

    // Testzweck: Ein Engine-Batch claimt höchstens ein Ergebnis je Prozessinstanz; der erste
    // Fortschritt kann sonst einen zweiten parallelen KI-Token im selben Commit stornieren.
    [Test]
    public async Task FilesystemStorage_ShouldClaimAtMostOneReadyResultPerProcessInstance()
    {
        using var context = new Context();
        var first = Run();
        var second = Run() with { Id = Guid.NewGuid(), TokenId = Guid.NewGuid() };
        await context.Storage.AiRunStorage.TryCreate(first);
        await context.Storage.AiRunStorage.TryCreate(second);
        var providerClaims = await context.Storage.AiRunStorage.ClaimProviderRuns(
            "provider", Now, Now.AddMinutes(5), 2);
        foreach (var claim in providerClaims)
        {
            var started = MarkStarted(claim, Now.AddSeconds(1));
            await context.Storage.AiRunStorage.TryUpdate(
                started, claim.Revision, "provider", Now.AddSeconds(1));
            await context.Storage.AiRunStorage.TryUpdate(
                Ready(started, Now.AddSeconds(2)), started.Revision, "provider", Now.AddSeconds(2));
        }

        var results = await context.Storage.AiRunStorage.ClaimResultRuns(
            "engine", Now.AddSeconds(3), Now.AddMinutes(5), 10);

        results.Should().ContainSingle();
    }

    // Testzweck: Verlaesst ein Token seinen KI-Schritt durch Abbruch oder Fehlerpfad, wird
    // ein noch offener Lauf dauerhaft storniert und kann weder Provider- noch Ergebnisclaimen.
    [Test]
    public async Task FilesystemStorage_ShouldCancelOnlyObsoleteOpenRuns()
    {
        using var context = new Context();
        var obsolete = Run();
        var active = Run() with
        {
            Id = Guid.NewGuid(),
            TokenId = Guid.NewGuid()
        };
        await context.Storage.AiRunStorage.TryCreate(obsolete);
        await context.Storage.AiRunStorage.TryCreate(active);

        var cancelled = await context.Storage.AiRunStorage.CancelObsoleteRuns(
            obsolete.ProcessInstanceId,
            [active.TokenId],
            Now.AddSeconds(1));

        cancelled.Should().Be(1);
        (await context.Storage.AiRunStorage.Get(obsolete.Id))!.Status.Should().Be(AiRunStatus.Cancelled);
        (await context.Storage.AiRunStorage.Get(active.Id))!.Status.Should().Be(AiRunStatus.Pending);
        (await context.Storage.AiRunStorage.ClaimProviderRuns(
            "provider",
            Now.AddSeconds(2),
            Now.AddMinutes(5),
            10)).Should().ContainSingle().Which.Id.Should().Be(active.Id);
    }

    // Testzweck: Eine verspätet eintreffende Engine-Zeit darf beim Stornieren eines
    // veralteten Laufs dessen persistierten Aktualisierungszeitpunkt nicht zurücksetzen.
    [Test]
    public async Task FilesystemStorage_ShouldKeepCancellationTimestampMonotonic()
    {
        using var context = new Context();
        var run = Run() with
        {
            CreatedAtUtc = Now.AddMinutes(1),
            UpdatedAtUtc = Now.AddMinutes(1)
        };
        await context.Storage.AiRunStorage.TryCreate(run);

        await context.Storage.AiRunStorage.CancelObsoleteRuns(
            run.ProcessInstanceId,
            [],
            Now);

        var cancelled = (await context.Storage.AiRunStorage.Get(run.Id))!;
        cancelled.Status.Should().Be(AiRunStatus.Cancelled);
        cancelled.UpdatedAtUtc.Should().Be(Now.AddMinutes(1));
    }

    private static AiRun MarkStarted(AiRun run, DateTime at) => run with
    {
        Attempt = run.Attempt + 1,
        ProviderCallStartedAtUtc = at,
        Revision = run.Revision + 1,
        UpdatedAtUtc = at
    };

    private static AiRun Ready(AiRun run, DateTime at) => run with
    {
        Status = AiRunStatus.ResultReady,
        LeaseOwner = null,
        LeaseExpiresAtUtc = null,
        OutputJson = "{\"category\":\"support\"}",
        ResultModel = "result-model",
        InputTokens = 20,
        OutputTokens = 5,
        TotalTokens = 25,
        Revision = run.Revision + 1,
        UpdatedAtUtc = at
    };

    internal static AiRun Run() => new()
    {
        Id = Guid.Parse("A1111111-1111-4111-8111-111111111111"),
        ProcessInstanceId = Guid.Parse("A2222222-2222-4222-8222-222222222222"),
        TokenId = Guid.Parse("A3333333-3333-4333-8333-333333333333"),
        FlowNodeId = "Task_AI",
        MetaDefinitionId = "ai-demo",
        DefinitionId = Guid.Parse("A4444444-4444-4444-8444-444444444444"),
        ProcessId = "Process_AI",
        ConnectionId = Guid.Parse("A5555555-5555-4555-8555-555555555555"),
        ConnectionRevision = 3,
        Model = "task-model",
        InstructionVersion = 2,
        Instruction = "Classify the declared input.",
        InputsJson = "{\"request\":\"Help\"}",
        ResultSchema = "{\"type\":\"object\",\"properties\":{}}",
        MaxInputTokens = 4_096,
        MaxOutputTokens = 512,
        TimeoutSeconds = 60,
        MaximumAttempts = 3,
        Status = AiRunStatus.Pending,
        Revision = 1,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    private sealed class Context : IDisposable
    {
        private readonly string? _previousRoot;

        public Context()
        {
            Root = Path.Combine(Path.GetTempPath(), $"flowzer-ai-runs-{Guid.NewGuid():N}");
            _previousRoot = Environment.GetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName);
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, Root);
            Storage = new Storage();
        }

        public string Root { get; }
        public Storage Storage { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Storage.StorageRootEnvironmentVariableName, _previousRoot);
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
