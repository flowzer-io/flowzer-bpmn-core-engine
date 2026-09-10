using System.Text.Json;
using Model;

namespace WebApiEngine.Ai;

/// <summary>
/// Oeffentlicher, nicht geheimer Vertrag einer fest registrierten Werkzeugimplementierung.
/// Zieladressen, Credentials und Handlerdetails sind absichtlich kein Bestandteil.
/// </summary>
public sealed record AiToolDefinition(
    string Id,
    int Version,
    string Name,
    string Description,
    string InputSchema,
    string OutputSchema,
    AiToolSideEffect SideEffect,
    bool AllowsPreApproval);

/// <summary>
/// Von der Runtime erzeugter Kontext. Der Werkzeughandler erhaelt weder den Prompt noch
/// frei waehlbare Berechtigungen aus einer Modellantwort.
/// </summary>
public sealed record AiToolExecutionRequest(
    Guid ProcessInstanceId,
    Guid AiRunId,
    string IdempotencyKey,
    JsonElement Arguments,
    bool DryRun);

public sealed record AiToolExecutionResult(JsonElement Output);

/// <summary>
/// Fest durch Dependency Injection registriertes Werkzeug. Die Ausfuehrung wird erst nach
/// serverseitiger Vertrags-, Rechte- und Freigabepruefung durch die Runtime aufgerufen.
/// </summary>
public interface IAiTool
{
    AiToolDefinition Definition { get; }

    ValueTask<AiToolExecutionResult> ExecuteAsync(
        AiToolExecutionRequest request,
        CancellationToken cancellationToken = default);
}
