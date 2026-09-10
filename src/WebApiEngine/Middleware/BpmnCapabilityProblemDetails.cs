namespace WebApiEngine.Middleware;

/// <summary>
/// Öffentlicher, versionierter Problem-Details-Vertrag für BPMN-Modellfehler.
/// Anders als freie <see cref="Microsoft.AspNetCore.Mvc.ProblemDetails.Extensions"/>
/// erscheinen diese Eigenschaften auch im OpenAPI-Schema und damit in generierten Clients.
/// </summary>
public sealed class BpmnCapabilityProblemDetails : ApiValidationProblem
{
    public string Code { get; init; } = "bpmn.model.invalid";
    public IReadOnlyList<BpmnCapabilityIssueDto> Issues { get; init; } = [];
    public string CapabilityContractVersion { get; init; } = "";
    public string TraceId { get; init; } = "";
}

/// <summary>Wertefreier, gezielt auf ein BPMN-Element beziehbarer Modellbefund.</summary>
public sealed record BpmnCapabilityIssueDto(
    string Code,
    string Severity,
    string? ElementId,
    string? PropertyPath,
    string Message);
