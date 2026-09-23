namespace core_engine.Exceptions;

/// <summary>
/// Kennzeichnet einen vorhersehbaren Verstoß gegen den versionierten BPMN-Fähigkeitsvertrag.
/// Code und Element-ID sind absichtlich maschinenlesbar, damit jede Modellieransicht denselben
/// Knoten markieren kann, statt Parser- oder Laufzeitdetails erraten zu müssen.
/// </summary>
public sealed class BpmnCapabilityValidationException(
    string code,
    string? elementId,
    string? propertyPath,
    string contractVersion,
    string message) : ModelValidationException(message)
{
    public IReadOnlyList<BpmnCapabilityIssue> Issues { get; init; } = [new(code, elementId, propertyPath, message)];
    public string Code { get; } = code;
    public string? ElementId { get; } = elementId;
    public string? PropertyPath { get; } = propertyPath;
    public string ContractVersion { get; } = contractVersion;
}

/// <summary>Wertearmer Befund für eine gemeinsame Veröffentlichungsprüfung.</summary>
public sealed record BpmnCapabilityIssue(string Code, string? ElementId, string? PropertyPath, string Message);
