using System.Text.Json;
using core_engine;
using Model;

namespace StorageSystem;

/// <summary>
/// Dauerhafte KI-Laufablage. Claims und Zustandswechsel muessen Revision, Lease und Status in
/// derselben atomaren Operation pruefen; nur so laufen mehrere API-Prozesse ohne Doppelaufruf.
/// </summary>
public interface IAiRunStorage
{
    Task<AiRunWriteResult> TryCreate(AiRun run);
    Task<AiRun?> Get(Guid id);
    Task<IReadOnlyList<AiRun>> List();

    Task<IReadOnlyList<AiRun>> ClaimProviderRuns(
        string leaseOwner,
        DateTime nowUtc,
        DateTime leaseExpiresAtUtc,
        int maxRuns);

    Task<IReadOnlyList<AiRun>> ClaimResultRuns(
        string leaseOwner,
        DateTime nowUtc,
        DateTime leaseExpiresAtUtc,
        int maxRuns);

    Task<AiRun?> RenewLease(
        Guid id,
        long expectedRevision,
        string leaseOwner,
        DateTime nowUtc,
        DateTime leaseExpiresAtUtc);

    Task<AiRunWriteResult> TryUpdate(
        AiRun updated,
        long expectedRevision,
        string leaseOwner,
        DateTime nowUtc);

    Task<IReadOnlyList<AiRun>> RecoverExpiredLeases(DateTime nowUtc, int maxRuns);
}

public enum AiRunWriteStatus
{
    Written,
    NotFound,
    Conflict,
    LeaseLost
}

public sealed record AiRunWriteResult(
    AiRunWriteStatus Status,
    AiRun? Current,
    long CurrentRevision);

/// <summary>
/// Gemeinsame Struktur- und Uebergangsregeln fuer alle Ablagen. Auf diese Weise darf eine
/// alternative Storage-Implementierung keine schwaechere Lease- oder Snapshot-Semantik haben.
/// </summary>
public static class AiRunStorageRules
{
    private const int MaximumTextLength = 20_000;
    private const int MaximumInputLength = 1_048_576;
    private const int MaximumModelLength = 200;

    public static void ValidateNew(AiRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Id == Guid.Empty
            || run.ProcessInstanceId == Guid.Empty
            || run.TokenId == Guid.Empty
            || run.DefinitionId == Guid.Empty
            || run.ConnectionId == Guid.Empty)
            throw new ArgumentException("AI run identifiers are required.", nameof(run));
        if (string.IsNullOrWhiteSpace(run.FlowNodeId)
            || string.IsNullOrWhiteSpace(run.MetaDefinitionId)
            || string.IsNullOrWhiteSpace(run.ProcessId)
            || string.IsNullOrWhiteSpace(run.Model)
            || string.IsNullOrWhiteSpace(run.Instruction))
            throw new ArgumentException("AI run snapshot text is required.", nameof(run));
        if (run.FlowNodeId.Length > 500
            || run.MetaDefinitionId.Length > 500
            || run.ProcessId.Length > 500
            || run.Model.Length > MaximumModelLength
            || run.Instruction.Length > MaximumTextLength)
            throw new ArgumentException("AI run snapshot text is too long.", nameof(run));
        if (run.ConnectionRevision < 1
            || run.InstructionVersion < 1
            || run.MaxInputTokens is < 1 or > 128_000
            || run.MaxOutputTokens is < 1 or > 32_768
            || run.TimeoutSeconds is < 1 or > 300
            || run.MaximumAttempts is < 1 or > 100)
            throw new ArgumentException("AI run limits are invalid.", nameof(run));
        if (run.Status != AiRunStatus.Pending
            || run.Attempt != 0
            || run.Revision != 1
            || run.NextAttemptAtUtc is not null
            || run.LeaseOwner is not null
            || run.LeaseExpiresAtUtc is not null
            || run.ProviderCallStartedAtUtc is not null
            || HasResult(run)
            || run.FailureCode is not null)
            throw new ArgumentException("A new AI run must be pending and empty.", nameof(run));
        EnsureUtc(run.CreatedAtUtc, nameof(run.CreatedAtUtc));
        EnsureUtc(run.UpdatedAtUtc, nameof(run.UpdatedAtUtc));
        if (run.UpdatedAtUtc != run.CreatedAtUtc)
            throw new ArgumentException("A new AI run must use one creation timestamp.", nameof(run));
        ValidateInputs(run.InputsJson);
        AiResultSchemaProfile.ValidateSchema(run.ResultSchema);
    }

    public static void ValidateTransition(
        AiRun current,
        AiRun updated,
        long expectedRevision,
        string leaseOwner,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(updated);
        ValidateLeaseArguments(leaseOwner, nowUtc, nowUtc.AddTicks(1));
        if (current.Revision != expectedRevision
            || updated.Revision != checked(expectedRevision + 1)
            || updated.UpdatedAtUtc < current.UpdatedAtUtc
            || updated.UpdatedAtUtc > nowUtc)
            throw new ArgumentException("AI run transition revision or time is invalid.", nameof(updated));
        EnsureUtc(updated.UpdatedAtUtc, nameof(updated.UpdatedAtUtc));
        EnsureOptionalUtc(updated.NextAttemptAtUtc, nameof(updated.NextAttemptAtUtc));
        EnsureOptionalUtc(updated.LeaseExpiresAtUtc, nameof(updated.LeaseExpiresAtUtc));
        EnsureOptionalUtc(updated.ProviderCallStartedAtUtc, nameof(updated.ProviderCallStartedAtUtc));
        if (!string.Equals(current.LeaseOwner, leaseOwner, StringComparison.Ordinal)
            || current.LeaseExpiresAtUtc is null
            || current.LeaseExpiresAtUtc <= nowUtc)
            throw new ArgumentException("AI run lease is not valid for this transition.", nameof(leaseOwner));
        EnsureSnapshotUnchanged(current, updated);

        switch (current.Status, updated.Status)
        {
            case (AiRunStatus.Running, AiRunStatus.Running):
                ValidateCallStart(current, updated, nowUtc);
                break;
            case (AiRunStatus.Running, AiRunStatus.ResultReady):
                ValidateReady(current, updated);
                break;
            case (AiRunStatus.Running, AiRunStatus.RetryScheduled):
                ValidateRetry(current, updated, nowUtc);
                break;
            case (AiRunStatus.Running, AiRunStatus.Incident):
                ValidateIncident(current, updated);
                break;
            case (AiRunStatus.Completing, AiRunStatus.Completed):
                ValidateCompleted(current, updated);
                break;
            case (AiRunStatus.Completing, AiRunStatus.Incident):
                ValidateIncident(current, updated);
                break;
            default:
                throw new ArgumentException("AI run status transition is not supported.", nameof(updated));
        }
    }

    public static void ValidateLeaseArguments(
        string leaseOwner,
        DateTime nowUtc,
        DateTime leaseExpiresAtUtc,
        int maxRuns = 1)
    {
        if (string.IsNullOrWhiteSpace(leaseOwner) || leaseOwner.Length > 200 || leaseOwner.Any(char.IsControl))
            throw new ArgumentException("AI run lease owner is invalid.", nameof(leaseOwner));
        EnsureUtc(nowUtc, nameof(nowUtc));
        EnsureUtc(leaseExpiresAtUtc, nameof(leaseExpiresAtUtc));
        if (leaseExpiresAtUtc <= nowUtc || leaseExpiresAtUtc > nowUtc.AddHours(1))
            throw new ArgumentOutOfRangeException(nameof(leaseExpiresAtUtc));
        if (maxRuns is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maxRuns));
    }

    private static void ValidateCallStart(AiRun current, AiRun updated, DateTime nowUtc)
    {
        if (current.ProviderCallStartedAtUtc is not null
            || updated.ProviderCallStartedAtUtc is null
            || updated.ProviderCallStartedAtUtc > nowUtc
            || updated.ProviderCallStartedAtUtc < current.UpdatedAtUtc
            || updated.Attempt != current.Attempt + 1
            || updated.Attempt > updated.MaximumAttempts
            || LeaseChanged(current, updated)
            || updated.NextAttemptAtUtc is not null
            || HasResult(updated)
            || updated.FailureCode is not null)
            throw new ArgumentException("AI provider call start transition is invalid.", nameof(updated));
    }

    private static void ValidateReady(AiRun current, AiRun updated)
    {
        if (current.ProviderCallStartedAtUtc is null
            || updated.ProviderCallStartedAtUtc != current.ProviderCallStartedAtUtc
            || updated.Attempt != current.Attempt
            || !LeaseCleared(updated)
            || updated.NextAttemptAtUtc is not null
            || string.IsNullOrWhiteSpace(updated.OutputJson)
            || string.IsNullOrWhiteSpace(updated.ResultModel)
            || updated.ResultModel.Length > MaximumModelLength
            || updated.InputTokens is null or < 0
            || updated.OutputTokens is null or < 0
            || updated.TotalTokens is null or < 0
            || updated.TotalTokens.Value != (long)updated.InputTokens.Value + updated.OutputTokens.Value
            || updated.InputTokens > updated.MaxInputTokens
            || updated.OutputTokens > updated.MaxOutputTokens
            || updated.FailureCode is not null)
            throw new ArgumentException("AI provider result transition is invalid.", nameof(updated));
        AiResultSchemaProfile.ParseAndValidateResult(updated.ResultSchema, updated.OutputJson);
    }

    private static void ValidateRetry(AiRun current, AiRun updated, DateTime nowUtc)
    {
        if (current.ProviderCallStartedAtUtc is null
            || updated.ProviderCallStartedAtUtc is not null
            || updated.Attempt != current.Attempt
            || updated.Attempt >= updated.MaximumAttempts
            || !LeaseCleared(updated)
            || updated.NextAttemptAtUtc is null
            || updated.NextAttemptAtUtc <= nowUtc
            || HasResult(updated)
            || !IsFailureCode(updated.FailureCode))
            throw new ArgumentException("AI run retry transition is invalid.", nameof(updated));
    }

    private static void ValidateIncident(AiRun current, AiRun updated)
    {
        if (updated.Attempt != current.Attempt
            || !LeaseCleared(updated)
            || updated.NextAttemptAtUtc is not null
            || !IsFailureCode(updated.FailureCode)
            || (current.Status == AiRunStatus.Running && HasResult(updated))
            || (current.Status == AiRunStatus.Completing && !SameResult(current, updated)))
            throw new ArgumentException("AI run incident transition is invalid.", nameof(updated));
    }

    private static void ValidateCompleted(AiRun current, AiRun updated)
    {
        if (updated.Attempt != current.Attempt
            || !LeaseCleared(updated)
            || updated.NextAttemptAtUtc is not null
            || updated.FailureCode is not null
            || !SameResult(current, updated))
            throw new ArgumentException("AI run completion transition is invalid.", nameof(updated));
    }

    private static void EnsureSnapshotUnchanged(AiRun current, AiRun updated)
    {
        var same = current.Id == updated.Id
                   && current.ProcessInstanceId == updated.ProcessInstanceId
                   && current.TokenId == updated.TokenId
                   && current.FlowNodeId == updated.FlowNodeId
                   && current.MetaDefinitionId == updated.MetaDefinitionId
                   && current.DefinitionId == updated.DefinitionId
                   && current.ProcessId == updated.ProcessId
                   && current.ConnectionId == updated.ConnectionId
                   && current.ConnectionRevision == updated.ConnectionRevision
                   && current.Model == updated.Model
                   && current.InstructionVersion == updated.InstructionVersion
                   && current.Instruction == updated.Instruction
                   && current.InputsJson == updated.InputsJson
                   && current.ResultSchema == updated.ResultSchema
                   && current.MaxInputTokens == updated.MaxInputTokens
                   && current.MaxOutputTokens == updated.MaxOutputTokens
                   && current.TimeoutSeconds == updated.TimeoutSeconds
                   && current.MaximumAttempts == updated.MaximumAttempts
                   && current.CreatedAtUtc == updated.CreatedAtUtc;
        if (!same) throw new ArgumentException("AI run execution snapshot is immutable.", nameof(updated));
    }

    private static void ValidateInputs(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaximumInputLength)
            throw new ArgumentException("AI run inputs are invalid.", nameof(json));
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("AI run inputs must be an object.", nameof(json));
        }
        catch (JsonException)
        {
            // Eingaben koennen personenbezogene Fachdaten enthalten. Parserdetails werden
            // deshalb nicht als spaeter moeglicherweise protokolliertes InnerException behalten.
            throw new ArgumentException("AI run inputs are invalid.", nameof(json));
        }
    }

    private static bool LeaseChanged(AiRun current, AiRun updated) =>
        current.LeaseOwner != updated.LeaseOwner
        || current.LeaseExpiresAtUtc != updated.LeaseExpiresAtUtc;

    private static bool LeaseCleared(AiRun run) =>
        run.LeaseOwner is null && run.LeaseExpiresAtUtc is null;

    private static bool HasResult(AiRun run) =>
        run.OutputJson is not null
        || run.ResultModel is not null
        || run.InputTokens is not null
        || run.OutputTokens is not null
        || run.TotalTokens is not null;

    private static bool SameResult(AiRun first, AiRun second) =>
        first.OutputJson == second.OutputJson
        && first.ResultModel == second.ResultModel
        && first.InputTokens == second.InputTokens
        && first.OutputTokens == second.OutputTokens
        && first.TotalTokens == second.TotalTokens
        && first.ProviderCallStartedAtUtc == second.ProviderCallStartedAtUtc;

    private static bool IsFailureCode(string? code) =>
        !string.IsNullOrWhiteSpace(code)
        && code.Length <= 200
        && code.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-');

    private static void EnsureUtc(DateTime value, string parameter)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("AI run timestamps must be UTC.", parameter);
    }

    private static void EnsureOptionalUtc(DateTime? value, string parameter)
    {
        if (value.HasValue) EnsureUtc(value.Value, parameter);
    }
}

/// <summary>Kompatibilitaetswache fuer noch nicht auf den neuen Vertrag umgestellte Testdoppel.</summary>
internal sealed class UnsupportedAiRunStorage : IAiRunStorage
{
    public static UnsupportedAiRunStorage Instance { get; } = new();
    private UnsupportedAiRunStorage() { }

    public Task<AiRunWriteResult> TryCreate(AiRun run) => throw Unsupported();
    public Task<AiRun?> Get(Guid id) => throw Unsupported();
    public Task<IReadOnlyList<AiRun>> List() => throw Unsupported();
    public Task<IReadOnlyList<AiRun>> ClaimProviderRuns(string leaseOwner, DateTime nowUtc, DateTime leaseExpiresAtUtc, int maxRuns) => throw Unsupported();
    public Task<IReadOnlyList<AiRun>> ClaimResultRuns(string leaseOwner, DateTime nowUtc, DateTime leaseExpiresAtUtc, int maxRuns) => throw Unsupported();
    public Task<AiRun?> RenewLease(Guid id, long expectedRevision, string leaseOwner, DateTime nowUtc, DateTime leaseExpiresAtUtc) => throw Unsupported();
    public Task<AiRunWriteResult> TryUpdate(AiRun updated, long expectedRevision, string leaseOwner, DateTime nowUtc) => throw Unsupported();
    public Task<IReadOnlyList<AiRun>> RecoverExpiredLeases(DateTime nowUtc, int maxRuns) => throw Unsupported();

    private static NotSupportedException Unsupported() =>
        new("This storage implementation does not support AI runs.");
}
