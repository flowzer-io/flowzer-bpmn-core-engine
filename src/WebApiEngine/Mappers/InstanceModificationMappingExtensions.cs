using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Mappers;

/// <summary>
/// Projiziert die Anfrage und die Ergebnisse des Instanzeingriffs zwischen aussen und innen.
/// Die Umrechnung der Variablen ist der eigentliche Zweck: Aussen kommt ein ExpandoObject an,
/// innen arbeitet die Engine mit einem gewoehnlichen Woerterbuch.
/// </summary>
public static class InstanceModificationMappingExtensions
{
    public static InstanceModificationRequest ToRequest(this InstanceModificationRequestDto? request)
    {
        if (request is null) return new InstanceModificationRequest();

        return new InstanceModificationRequest(
            [.. (request.Moves ?? []).Select(move =>
                new InstanceModificationMove(move.TokenId, move.TargetFlowNodeId ?? string.Empty))],
            ToVariables(request.Variables?.Set),
            // Doppelte Nennungen sind keine Aussage, sondern eine Unachtsamkeit der Oberflaeche;
            // der Hinweis „gibt es nicht" soll deshalb nicht zweimal erscheinen.
            [.. (request.Variables?.Remove ?? []).Distinct(StringComparer.Ordinal)]);
    }

    public static InstanceModificationPreviewDto ToDto(this InstanceModificationPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return new InstanceModificationPreviewDto
        {
            InstanceId = preview.InstanceId,
            Applicable = preview.Applicable,
            Problems = [.. preview.Problems.Select(ToDto)],
            Notices = [.. preview.Notices.Select(ToDto)],
            Steps = [.. preview.Steps.Select(step => new InstanceModificationStepDto
            {
                TokenId = step.TokenId,
                FlowNodeId = step.FlowNodeId,
                Name = step.Name,
                Type = step.Type
            })],
            Targets = [.. preview.Targets.Select(target => new ModificationFlowNodeDto
            {
                Id = target.Id,
                Name = target.Name,
                Type = target.Type
            })]
        };
    }

    public static InstanceModificationResultDto ToDto(
        this InstanceModificationOutcome outcome,
        ProcessInstanceInfoDto instance)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(instance);

        return new InstanceModificationResultDto
        {
            InstanceId = outcome.InstanceId,
            Modified = outcome.Modified,
            Notices = [.. outcome.Notices.Select(ToDto)],
            Instance = instance
        };
    }

    public static InstanceModificationFindingDto ToDto(this InstanceModificationFinding finding) => new()
    {
        Code = finding.Code,
        TokenId = finding.TokenId,
        FlowNodeId = finding.FlowNodeId,
        Message = finding.Message
    };

    /// <summary>
    /// Ein leeres Objekt heisst „nichts setzen" und nicht „alles loeschen"; deshalb wird es wie
    /// eine fehlende Angabe behandelt.
    /// </summary>
    private static IReadOnlyDictionary<string, object?>? ToVariables(System.Dynamic.ExpandoObject? variables)
    {
        if (variables is null) return null;

        var values = (IDictionary<string, object?>)variables;
        return values.Count == 0 ? null : new Dictionary<string, object?>(values, StringComparer.Ordinal);
    }
}
