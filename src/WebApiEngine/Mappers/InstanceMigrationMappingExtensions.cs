using WebApiEngine.BusinessLogic;
using WebApiEngine.Shared;

namespace WebApiEngine.Mappers;

/// <summary>
/// Projiziert die Ergebnisse der Instanzmigration nach aussen. Der Anzeigename des Workflows
/// kommt wie in der Instanzliste aus dem Katalog; fehlt der Eintrag, bleibt die Kennung stehen.
/// </summary>
public static class InstanceMigrationMappingExtensions
{
    public static async Task<InstanceMigrationPreviewDto> ToDtoAsync(
        this InstanceMigrationPreview preview,
        IDefinitionStorage definitionStorage)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(definitionStorage);

        return new InstanceMigrationPreviewDto
        {
            RelatedDefinitionId = preview.RelatedDefinitionId,
            RelatedDefinitionName = await ResolveNameAsync(definitionStorage, preview.RelatedDefinitionId),
            SourceDefinitionId = preview.SourceDefinitionId,
            SourceVersion = ToDto(preview.SourceVersion),
            TargetDefinitionId = preview.TargetDefinitionId,
            TargetVersion = ToDto(preview.TargetVersion)
                            ?? throw new InvalidOperationException("An accepted preview always names its target version."),
            Instances = [.. preview.Instances.Select(item => new InstanceMigrationPreviewItemDto
            {
                InstanceId = item.InstanceId,
                Migratable = item.Migratable,
                Problems = [.. item.Problems.Select(ToDto)],
                Notices = [.. item.Notices.Select(ToDto)]
            })],
            Mapping = new InstanceMigrationMappingDto
            {
                Required = [.. preview.MappingRequired.Select(ToDto)],
                Targets = [.. preview.MappingTargets.Select(ToDto)]
            }
        };
    }

    public static InstanceMigrationResultDto ToDto(this InstanceMigrationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new InstanceMigrationResultDto
        {
            TargetDefinitionId = outcome.TargetDefinitionId,
            TargetVersion = ToDto(outcome.TargetVersion)
                            ?? throw new InvalidOperationException("An accepted migration always names its target version."),
            Instances = [.. outcome.Instances.Select(item => new InstanceMigrationResultItemDto
            {
                InstanceId = item.InstanceId,
                Migrated = item.Migrated,
                Problems = [.. item.Problems.Select(ToDto)]
            })]
        };
    }

    private static InstanceMigrationFindingDto ToDto(InstanceMigrationFinding finding) => new()
    {
        Code = finding.Code,
        FlowNodeId = finding.FlowNodeId,
        Message = finding.Message
    };

    private static MigrationFlowNodeDto ToDto(InstanceMigrationFlowNode flowNode) => new()
    {
        Id = flowNode.Id,
        Name = flowNode.Name,
        Type = flowNode.Type
    };

    private static VersionDto? ToDto(Model.Version? version) =>
        version is null ? null : new VersionDto(version.Major, version.Minor);

    private static async Task<string> ResolveNameAsync(
        IDefinitionStorage definitionStorage,
        string relatedDefinitionId)
    {
        var metaDefinitions = await definitionStorage.GetAllMetaDefinitions();
        return metaDefinitions
                   .FirstOrDefault(metaDefinition => string.Equals(
                       metaDefinition.DefinitionId, relatedDefinitionId, StringComparison.Ordinal))?.Name
               ?? relatedDefinitionId;
    }
}
