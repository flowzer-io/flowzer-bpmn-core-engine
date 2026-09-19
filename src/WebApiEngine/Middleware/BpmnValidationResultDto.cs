using core_engine;
using WebApiEngine.Shared;

namespace WebApiEngine.Middleware;

/// <summary>
/// Das Ergebnis einer Prüfung, die zugesagt hat.
///
/// Der Vertrag ist bewusst additiv: <c>contractVersion</c> und <c>elements</c> stehen
/// unverändert an derselben Stelle wie im bisher gelieferten Fähigkeitsvertrag,
/// <c>warnings</c> kommt hinzu. Ein Client, der die Warnungen nicht kennt, liest weiterhin
/// genau das, was er bisher gelesen hat.
/// </summary>
public sealed class BpmnValidationResultDto
{
    public required string ContractVersion { get; init; }
    public required IReadOnlyList<BpmnElementCapability> Elements { get; init; }

    /// <summary>Hinweise, die die Veröffentlichung ausdrücklich nicht verhindern.</summary>
    public IReadOnlyList<BpmnCapabilityIssueDto> Warnings { get; init; } = [];

    public static BpmnValidationResultDto From(
        BpmnCapabilityContract contract,
        IReadOnlyList<core_engine.Exceptions.BpmnCapabilityIssue> warnings) => new()
        {
            ContractVersion = contract.ContractVersion,
            Elements = contract.Elements,
            Warnings = warnings.ToWarningDtos()
        };
}

/// <summary>Übersetzt Vertragsbefunde in den öffentlichen, wertefreien Warnungsvertrag.</summary>
public static class BpmnCapabilityWarningMapper
{
    public static IReadOnlyList<BpmnCapabilityIssueDto> ToWarningDtos(
        this IReadOnlyList<core_engine.Exceptions.BpmnCapabilityIssue> warnings) =>
        warnings.Count == 0
            ? []
            : warnings
                .Select(warning => new BpmnCapabilityIssueDto(
                    warning.Code, "warning", warning.ElementId, warning.PropertyPath, warning.Message))
                .ToArray();
}
