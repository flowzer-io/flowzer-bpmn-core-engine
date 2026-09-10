using System.Text.Json;
using Model;
using StorageSystem;

namespace WebApiEngine.Ai;

/// <summary>Zaehler eines begrenzten Executor-Durchgangs ohne fachliche Laufdaten.</summary>
internal sealed record AiRunBatchResult(
    int Recovered,
    int Claimed,
    int ResultsReady,
    int RetriesScheduled,
    int Incidents,
    int LeasesLost);

/// <summary>
/// Fuehrt bereits persistierte KI-Läufe bis zum validierten Providerergebnis. Der spaetere
/// Engine-Commit ist absichtlich nicht Bestandteil dieses Dienstes und besitzt einen eigenen Claim.
/// </summary>
internal sealed class AiRunExecutor
{
    private static readonly HashSet<string> RetryableFailureCodes = new(StringComparer.Ordinal)
    {
        "ai.connection.resolve_failed",
        "ai.provider.rate_limited",
        "ai.provider.timeout",
        "ai.provider.transport",
        "ai.provider.unavailable"
    };

    private readonly IAiRunStorage _runs;
    private readonly IAiInferenceGateway _gateway;
    private readonly TimeProvider _time;
    private readonly AiRunExecutionPolicy _policy;
    private readonly ILogger<AiRunExecutor> _logger;
    private readonly string _leaseOwner;

    public AiRunExecutor(
        IAiRunStorage runs,
        IAiInferenceGateway gateway,
        TimeProvider time,
        AiRunExecutionPolicy policy,
        ILogger<AiRunExecutor> logger)
        : this(runs, gateway, time, policy, logger, CreateLeaseOwner())
    {
    }

    internal AiRunExecutor(
        IAiRunStorage runs,
        IAiInferenceGateway gateway,
        TimeProvider time,
        AiRunExecutionPolicy policy,
        ILogger<AiRunExecutor> logger,
        string leaseOwner)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(logger);
        if (!policy.IsValid()) throw new ArgumentException("AI run execution policy is invalid.", nameof(policy));
        if (string.IsNullOrWhiteSpace(leaseOwner)
            || leaseOwner.Length > 200
            || leaseOwner.Any(char.IsControl))
            throw new ArgumentException("AI run lease owner is invalid.", nameof(leaseOwner));

        _runs = runs;
        _gateway = gateway;
        _time = time;
        _policy = policy;
        _logger = logger;
        _leaseOwner = leaseOwner;
    }

    public async Task<AiRunBatchResult> RunProviderBatchAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var nowUtc = UtcNow();
        var recovered = await _runs.RecoverExpiredLeases(nowUtc, _policy.BatchSize);
        var claimed = await _runs.ClaimProviderRuns(
            _leaseOwner,
            nowUtc,
            nowUtc + _policy.LeaseDuration,
            _policy.BatchSize);

        var outcomes = await Task.WhenAll(claimed.Select(run =>
            ExecuteClaimedSafelyAsync(run, cancellationToken)));

        return new AiRunBatchResult(
            recovered.Count,
            claimed.Count,
            outcomes.Count(outcome => outcome == ExecutionOutcome.ResultReady),
            outcomes.Count(outcome => outcome == ExecutionOutcome.RetryScheduled),
            outcomes.Count(outcome => outcome == ExecutionOutcome.Incident),
            outcomes.Count(outcome => outcome == ExecutionOutcome.LeaseLost));
    }

    private async Task<ExecutionOutcome> ExecuteClaimedSafelyAsync(
        AiRun claimed,
        CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested) return ExecutionOutcome.Unchanged;
        try
        {
            return await ExecuteClaimedAsync(claimed, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return ExecutionOutcome.Unchanged;
        }
        catch (Exception)
        {
            // Infrastrukturdetails und persistierte Fachdaten gehoeren nicht in Logs. Der
            // geleaste Stand bleibt fuer die konservative Recovery erhalten.
            _logger.LogError("Processing a claimed AI run failed.");
            return ExecutionOutcome.Unchanged;
        }
    }

    private async Task<ExecutionOutcome> ExecuteClaimedAsync(
        AiRun claimed,
        CancellationToken stoppingToken)
    {
        JsonElement inputs;
        try
        {
            using var inputDocument = JsonDocument.Parse(claimed.InputsJson);
            inputs = inputDocument.RootElement.Clone();
        }
        catch (JsonException)
        {
            return await WriteIncidentAsync(claimed, "ai.run.input_invalid", stoppingToken);
        }

        var startedAtUtc = UtcNow();
        var started = claimed with
        {
            Attempt = claimed.Attempt + 1,
            ProviderCallStartedAtUtc = startedAtUtc,
            Revision = claimed.Revision + 1,
            UpdatedAtUtc = startedAtUtc
        };
        var startWrite = await _runs.TryUpdate(
            started,
            claimed.Revision,
            _leaseOwner,
            startedAtUtc);
        if (startWrite.Status != AiRunWriteStatus.Written)
            return startWrite.Status == AiRunWriteStatus.LeaseLost
                ? ExecutionOutcome.LeaseLost
                : ExecutionOutcome.Unchanged;

        started = startWrite.Current!;
        var tracker = new LeaseTracker(started);
        using var providerCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeat = MaintainLeaseAsync(tracker, providerCancellation, heartbeatStop.Token);

        AiInferenceResult? result = null;
        Exception? failure = null;
        try
        {
            result = await _gateway.ExecuteAsync(ToCommand(started, inputs), providerCancellation.Token);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested || tracker.LeaseLost)
        {
            // Bei Shutdown oder Lease-Verlust bleibt der markierte Aufruf unangetastet. Die
            // persistente Recovery stuft seinen Ausgang konservativ als unklar ein.
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            heartbeatStop.Cancel();
            await IgnoreExpectedCancellationAsync(heartbeat);
        }

        if (tracker.LeaseLost) return ExecutionOutcome.LeaseLost;
        if (stoppingToken.IsCancellationRequested) return ExecutionOutcome.Unchanged;
        var current = tracker.Current;

        if (result is not null)
        {
            var completedAtUtc = UtcNow();
            var ready = current with
            {
                Status = AiRunStatus.ResultReady,
                LeaseOwner = null,
                LeaseExpiresAtUtc = null,
                OutputJson = result.Output.GetRawText(),
                ResultModel = result.Model,
                InputTokens = result.Usage.InputTokens,
                OutputTokens = result.Usage.OutputTokens,
                TotalTokens = result.Usage.TotalTokens,
                FailureCode = null,
                Revision = current.Revision + 1,
                UpdatedAtUtc = completedAtUtc
            };
            return MapWrite(await _runs.TryUpdate(
                ready,
                current.Revision,
                _leaseOwner,
                completedAtUtc),
                ExecutionOutcome.ResultReady);
        }

        if (failure is AiProviderCallException { Retryable: true } providerFailure
            && RetryableFailureCodes.Contains(providerFailure.Code)
            && current.Attempt < current.MaximumAttempts)
        {
            var failedAtUtc = UtcNow();
            var retry = current with
            {
                Status = AiRunStatus.RetryScheduled,
                ProviderCallStartedAtUtc = null,
                LeaseOwner = null,
                LeaseExpiresAtUtc = null,
                NextAttemptAtUtc = failedAtUtc + RetryDelay(current.Attempt),
                FailureCode = SafeFailureCode(providerFailure.Code),
                Revision = current.Revision + 1,
                UpdatedAtUtc = failedAtUtc
            };
            return MapWrite(await _runs.TryUpdate(
                retry,
                current.Revision,
                _leaseOwner,
                failedAtUtc),
                ExecutionOutcome.RetryScheduled);
        }

        var code = failure is AiProviderCallException provider
            ? SafeFailureCode(provider.Code)
            : "ai.run.executor_failed";
        return await WriteIncidentAsync(current, code, stoppingToken);
    }

    private async Task<ExecutionOutcome> WriteIncidentAsync(
        AiRun current,
        string failureCode,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return ExecutionOutcome.Unchanged;
        var failedAtUtc = UtcNow();
        var incident = current with
        {
            Status = AiRunStatus.Incident,
            LeaseOwner = null,
            LeaseExpiresAtUtc = null,
            NextAttemptAtUtc = null,
            FailureCode = SafeFailureCode(failureCode),
            Revision = current.Revision + 1,
            UpdatedAtUtc = failedAtUtc
        };
        return MapWrite(await _runs.TryUpdate(
            incident,
            current.Revision,
            _leaseOwner,
            failedAtUtc),
            ExecutionOutcome.Incident);
    }

    private async Task MaintainLeaseAsync(
        LeaseTracker tracker,
        CancellationTokenSource providerCancellation,
        CancellationToken heartbeatCancellation)
    {
        try
        {
            while (true)
            {
                await Task.Delay(_policy.HeartbeatInterval, _time, heartbeatCancellation);
                var current = tracker.Current;
                var nowUtc = UtcNow();
                AiRun? renewed;
                try
                {
                    renewed = await _runs.RenewLease(
                        current.Id,
                        current.Revision,
                        _leaseOwner,
                        nowUtc,
                        nowUtc + _policy.LeaseDuration);
                }
                catch (Exception) when (!heartbeatCancellation.IsCancellationRequested)
                {
                    // Storage-Details koennten Infrastrukturwerte enthalten und werden nicht
                    // mit dem Lauf protokolliert. Ohne sichere Erneuerung gilt die Lease als verloren.
                    renewed = null;
                }

                if (renewed is not null)
                {
                    tracker.Update(renewed);
                    continue;
                }

                tracker.MarkLeaseLost();
                providerCancellation.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) when (heartbeatCancellation.IsCancellationRequested)
        {
            // Normaler Abschluss des Provideraufrufs oder Host-Shutdown.
        }
    }

    private TimeSpan RetryDelay(int attempt)
    {
        var delay = _policy.RetryBaseDelay;
        for (var index = 1; index < attempt && delay < _policy.MaximumRetryDelay; index++)
        {
            if (delay >= _policy.MaximumRetryDelay / 2)
                return _policy.MaximumRetryDelay;
            delay *= 2;
        }
        return delay > _policy.MaximumRetryDelay ? _policy.MaximumRetryDelay : delay;
    }

    private static AiInferenceCommand ToCommand(AiRun run, JsonElement inputs) => new(
        run.ConnectionId,
        run.ConnectionRevision,
        run.Model,
        run.InstructionVersion,
        run.Instruction,
        inputs,
        run.ResultSchema,
        run.MaxInputTokens,
        run.MaxOutputTokens,
        TimeSpan.FromSeconds(run.TimeoutSeconds));

    private static string SafeFailureCode(string code) =>
        !string.IsNullOrWhiteSpace(code)
        && code.Length <= 200
        && code.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-')
            ? code
            : "ai.run.executor_failed";

    private static ExecutionOutcome MapWrite(
        AiRunWriteResult write,
        ExecutionOutcome written) => write.Status switch
        {
            AiRunWriteStatus.Written => written,
            AiRunWriteStatus.LeaseLost => ExecutionOutcome.LeaseLost,
            _ => ExecutionOutcome.Unchanged
        };

    private static async Task IgnoreExpectedCancellationAsync(Task heartbeat)
    {
        try { await heartbeat; }
        catch (OperationCanceledException) { }
    }

    private DateTime UtcNow() => _time.GetUtcNow().UtcDateTime;

    private static string CreateLeaseOwner() => $"flowzer-ai:{Environment.ProcessId}:{Guid.NewGuid():N}";

    private enum ExecutionOutcome
    {
        Unchanged,
        ResultReady,
        RetryScheduled,
        Incident,
        LeaseLost
    }

    private sealed class LeaseTracker(AiRun current)
    {
        private readonly object _gate = new();
        private AiRun _current = current;
        private bool _leaseLost;

        public AiRun Current
        {
            get { lock (_gate) return _current; }
        }

        public bool LeaseLost
        {
            get { lock (_gate) return _leaseLost; }
        }

        public void Update(AiRun renewed)
        {
            lock (_gate) _current = renewed;
        }

        public void MarkLeaseLost()
        {
            lock (_gate) _leaseLost = true;
        }
    }
}
