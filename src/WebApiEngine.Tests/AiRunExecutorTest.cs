using System.Text.Json;
using FilesystemStorageSystem;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Model;
using StorageSystem;
using WebApiEngine.Ai;

namespace WebApiEngine.Tests;

/// <summary>Providerseitige Ausfuehrung bereits persistierter KI-Läufe.</summary>
[NonParallelizable]
public sealed class AiRunExecutorTest
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 18, 0, 0, TimeSpan.Zero);

    // Testzweck: Der Executor markiert den externen Aufruf vor dessen Beginn und persistiert
    // nur das lokal validierte Ergebnis samt Modell- und Tokenmessung als ResultReady.
    [Test]
    public async Task RunProviderBatch_ShouldPersistValidatedProviderResult()
    {
        using var context = new Context();
        var initial = context.Run();
        await context.Storage.TryCreate(initial);
        AiRun? observedAtGateway = null;
        context.Gateway.OnExecute = async (_, _) =>
            observedAtGateway = await context.Storage.Get(initial.Id);

        var result = await context.Executor.RunProviderBatchAsync(default);

        result.Claimed.Should().Be(1);
        result.ResultsReady.Should().Be(1);
        observedAtGateway.Should().Match<AiRun>(run =>
            run.Status == AiRunStatus.Running
            && run.Attempt == 1
            && run.ProviderCallStartedAtUtc.HasValue);
        var stored = (await context.Storage.Get(initial.Id))!;
        stored.Status.Should().Be(AiRunStatus.ResultReady);
        stored.OutputJson.Should().Be("{\"category\":\"support\"}");
        stored.ResultModel.Should().Be("result-model");
        stored.TotalTokens.Should().Be(25);
        context.Gateway.LastCommand.Should().BeEquivalentTo(new
        {
            initial.ConnectionId,
            initial.ConnectionRevision,
            initial.Model,
            initial.InstructionVersion,
            initial.Instruction,
            initial.MaxInputTokens,
            initial.MaxOutputTokens
        });
    }

    // Testzweck: Nur explizit als temporaer klassifizierte Fehler erhalten innerhalb des
    // Versuchslimits einen persistenten Retrytermin; vor diesem Termin geschieht nichts.
    [Test]
    public async Task RunProviderBatch_ShouldScheduleOnlyBoundedRetryableFailure()
    {
        using var context = new Context();
        await context.Storage.TryCreate(context.Run() with { MaximumAttempts = 2 });
        context.Gateway.Failure = new AiProviderCallException(
            "ai.provider.rate_limited",
            retryable: true,
            "Temporary provider failure.");

        var first = await context.Executor.RunProviderBatchAsync(default);
        var retry = (await context.Storage.List()).Single();
        var early = await context.Executor.RunProviderBatchAsync(default);
        context.Time.Advance(context.Policy.RetryBaseDelay);
        context.Gateway.Failure = null;
        var second = await context.Executor.RunProviderBatchAsync(default);

        first.RetriesScheduled.Should().Be(1);
        retry.Status.Should().Be(AiRunStatus.RetryScheduled);
        retry.NextAttemptAtUtc.Should().Be(Now.UtcDateTime + context.Policy.RetryBaseDelay);
        early.Claimed.Should().Be(0);
        second.ResultsReady.Should().Be(1);
        (await context.Storage.List()).Single().Attempt.Should().Be(2);
        context.Gateway.Calls.Should().Be(2);
    }

    // Testzweck: Permanente, erschoepfte und unerwartete Fehler werden ohne Rohdetails als
    // Störung angehalten und nicht erneut automatisch angeboten.
    [TestCase("ai.provider.authentication", false, 3, "ai.provider.authentication")]
    [TestCase("ai.provider.authentication", true, 3, "ai.provider.authentication")]
    [TestCase("ai.provider.unavailable", true, 1, "ai.provider.unavailable")]
    [TestCase(null, false, 3, "ai.run.executor_failed")]
    public async Task RunProviderBatch_ShouldIncidentNonRetryableOrExhaustedFailure(
        string? providerCode,
        bool retryable,
        int maximumAttempts,
        string expectedCode)
    {
        using var context = new Context();
        await context.Storage.TryCreate(context.Run() with { MaximumAttempts = maximumAttempts });
        context.Gateway.Failure = providerCode is null
            ? new InvalidOperationException("sensitive provider details")
            : new AiProviderCallException(providerCode, retryable, "Provider failure.");

        var result = await context.Executor.RunProviderBatchAsync(default);

        result.Incidents.Should().Be(1);
        var stored = (await context.Storage.List()).Single();
        stored.Status.Should().Be(AiRunStatus.Incident);
        stored.FailureCode.Should().Be(expectedCode);
        (await context.Storage.ClaimProviderRuns(
            "other", Now.UtcDateTime, Now.UtcDateTime.AddMinutes(1), 1)).Should().BeEmpty();
    }

    // Testzweck: Geht die Lease waehrend eines laufenden Aufrufs verloren, wird der Provider
    // abgebrochen und ein eventuell spaetes Ergebnis niemals mit fremder Revision gespeichert.
    [Test]
    public async Task RunProviderBatch_ShouldDiscardResultAfterLeaseLoss()
    {
        using var context = new Context(renewLease: false);
        var initial = context.Run();
        await context.Storage.TryCreate(initial);
        context.Gateway.WaitForCancellation = true;

        var running = context.Executor.RunProviderBatchAsync(default);
        await context.Gateway.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        context.Time.Advance(context.Policy.HeartbeatInterval);
        var result = await running.WaitAsync(TimeSpan.FromSeconds(2));

        result.LeasesLost.Should().Be(1);
        context.Storage.RenewCalls.Should().Be(1);
        var stored = (await context.Storage.Get(initial.Id))!;
        stored.Status.Should().Be(AiRunStatus.Running);
        stored.OutputJson.Should().BeNull();
    }

    // Testzweck: Zwei parallel arbeitende Executor-Instanzen erhalten denselben persistenten
    // Lauf dank atomarem Claim niemals beide als Providerauftrag.
    [Test]
    public async Task RunProviderBatch_ShouldNotExecuteSameRunTwice()
    {
        using var context = new Context();
        await context.Storage.TryCreate(context.Run());
        context.Gateway.WaitForRelease = true;
        var second = new AiRunExecutor(
            context.Storage,
            context.Gateway,
            context.Time,
            context.Policy,
            NullLogger<AiRunExecutor>.Instance,
            "executor-b");

        var firstRun = context.Executor.RunProviderBatchAsync(default);
        await context.Gateway.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var secondRun = await second.RunProviderBatchAsync(default);
        context.Gateway.Release.TrySetResult();
        var firstResult = await firstRun;

        firstResult.Claimed.Should().Be(1);
        secondRun.Claimed.Should().Be(0);
        context.Gateway.Calls.Should().Be(1);
    }

    // Testzweck: Jeder Durchgang bereinigt abgelaufene Leases zuerst; nur ein Stand vor dem
    // markierten Provideraufruf wird erneut ausgefuehrt, ein unklarer Ausgang wird Incident.
    [Test]
    public async Task RunProviderBatch_ShouldRecoverExpiredRunsConservatively()
    {
        using var context = new Context();
        var beforeCall = context.Run();
        var duringCall = context.Run() with
        {
            Id = Guid.NewGuid(),
            ProcessInstanceId = Guid.NewGuid(),
            TokenId = Guid.NewGuid()
        };
        await context.Storage.TryCreate(beforeCall);
        await context.Storage.TryCreate(duringCall);
        var claimed = await context.Storage.ClaimProviderRuns(
            "lost", Now.UtcDateTime, Now.UtcDateTime.AddSeconds(10), 2);
        var call = claimed.Single(run => run.Id == duringCall.Id);
        var started = call with
        {
            Attempt = 1,
            ProviderCallStartedAtUtc = Now.UtcDateTime.AddSeconds(1),
            Revision = call.Revision + 1,
            UpdatedAtUtc = Now.UtcDateTime.AddSeconds(1)
        };
        await context.Storage.TryUpdate(
            started,
            call.Revision,
            "lost",
            Now.UtcDateTime.AddSeconds(1));
        context.Time.Advance(TimeSpan.FromSeconds(11));

        var result = await context.Executor.RunProviderBatchAsync(default);

        result.Recovered.Should().Be(2);
        result.ResultsReady.Should().Be(1);
        (await context.Storage.Get(duringCall.Id)).Should().Match<AiRun>(run =>
            run.Status == AiRunStatus.Incident
            && run.FailureCode == "ai.run.outcome_unknown");
        context.Gateway.Calls.Should().Be(1);
    }

    private sealed class Context : IDisposable
    {
        private readonly string? _previousRoot;
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"flowzer-ai-executor-{Guid.NewGuid():N}");

        public Context(bool renewLease = true)
        {
            _previousRoot = Environment.GetEnvironmentVariable(
                FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName);
            Environment.SetEnvironmentVariable(
                FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName,
                _root);
            var filesystem = new Storage();
            Storage = new TrackingAiRunStorage(filesystem.AiRunStorage, renewLease);
            Gateway = new FakeInferenceGateway();
            Time = new FakeTimeProvider(Now);
            Policy = new AiRunExecutionPolicy(
                Enabled: true,
                PollInterval: TimeSpan.FromSeconds(5),
                BatchSize: 10,
                LeaseDuration: TimeSpan.FromSeconds(30),
                HeartbeatInterval: TimeSpan.FromSeconds(5),
                RetryBaseDelay: TimeSpan.FromMinutes(1),
                MaximumRetryDelay: TimeSpan.FromMinutes(10));
            Executor = new AiRunExecutor(
                Storage,
                Gateway,
                Time,
                Policy,
                NullLogger<AiRunExecutor>.Instance,
                "executor-a");
        }

        public TrackingAiRunStorage Storage { get; }
        public FakeInferenceGateway Gateway { get; }
        public FakeTimeProvider Time { get; }
        public AiRunExecutionPolicy Policy { get; }
        public AiRunExecutor Executor { get; }

        public AiRun Run() => AiRunStorageTest.Run() with
        {
            Id = Guid.NewGuid(),
            ProcessInstanceId = Guid.NewGuid(),
            TokenId = Guid.NewGuid(),
            CreatedAtUtc = Now.UtcDateTime,
            UpdatedAtUtc = Now.UtcDateTime
        };

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(
                FilesystemStorageSystem.Storage.StorageRootEnvironmentVariableName,
                _previousRoot);
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeInferenceGateway : IAiInferenceGateway
    {
        public int Calls { get; private set; }
        public AiInferenceCommand? LastCommand { get; private set; }
        public Exception? Failure { get; set; }
        public bool WaitForCancellation { get; set; }
        public bool WaitForRelease { get; set; }
        public Func<AiInferenceCommand, CancellationToken, Task>? OnExecute { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AiInferenceResult> ExecuteAsync(
            AiInferenceCommand command,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastCommand = command;
            Started.TrySetResult();
            if (OnExecute is not null) await OnExecute(command, cancellationToken);
            if (WaitForCancellation)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (WaitForRelease)
                await Release.Task.WaitAsync(cancellationToken);
            if (Failure is not null) throw Failure;
            return new AiInferenceResult(
                Json("{\"category\":\"support\"}"),
                "result-model",
                new AiTokenUsage(20, 5, 25));
        }

        private static JsonElement Json(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
    }

    private sealed class TrackingAiRunStorage(IAiRunStorage inner, bool renewLease) : IAiRunStorage
    {
        public int RenewCalls { get; private set; }

        public Task<AiRunWriteResult> TryCreate(AiRun run) => inner.TryCreate(run);
        public Task<AiRun?> Get(Guid id) => inner.Get(id);
        public Task<IReadOnlyList<AiRun>> List() => inner.List();
        public Task<IReadOnlyList<AiRun>> ClaimProviderRuns(string leaseOwner, DateTime nowUtc, DateTime leaseExpiresAtUtc, int maxRuns) =>
            inner.ClaimProviderRuns(leaseOwner, nowUtc, leaseExpiresAtUtc, maxRuns);
        public Task<IReadOnlyList<AiRun>> ClaimResultRuns(string leaseOwner, DateTime nowUtc, DateTime leaseExpiresAtUtc, int maxRuns) =>
            inner.ClaimResultRuns(leaseOwner, nowUtc, leaseExpiresAtUtc, maxRuns);
        public Task<AiRunWriteResult> TryUpdate(AiRun updated, long expectedRevision, string leaseOwner, DateTime nowUtc) =>
            inner.TryUpdate(updated, expectedRevision, leaseOwner, nowUtc);
        public Task<IReadOnlyList<AiRun>> RecoverExpiredLeases(DateTime nowUtc, int maxRuns) =>
            inner.RecoverExpiredLeases(nowUtc, maxRuns);

        public Task<AiRun?> RenewLease(
            Guid id,
            long expectedRevision,
            string leaseOwner,
            DateTime nowUtc,
            DateTime leaseExpiresAtUtc)
        {
            RenewCalls++;
            return renewLease
                ? inner.RenewLease(id, expectedRevision, leaseOwner, nowUtc, leaseExpiresAtUtc)
                : Task.FromResult<AiRun?>(null);
        }
    }
}
