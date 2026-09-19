namespace WebApiEngine.Shared;

/// <summary>
/// Wertefreier, gezielt auf ein BPMN-Element beziehbarer Modellbefund.
///
/// <c>severity</c> trennt die beiden Arten: <c>error</c> verhindert die Veröffentlichung und
/// erscheint deshalb in den Problem Details, <c>warning</c> begleitet eine erfolgreiche
/// Antwort und ist ein Hinweis, kein Blocker.
/// </summary>
public sealed record BpmnCapabilityIssueDto(
    string Code,
    string Severity,
    string? ElementId,
    string? PropertyPath,
    string Message);
