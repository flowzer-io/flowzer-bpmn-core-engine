using WebApiEngine.Shared;
using Version = Model.Version;

namespace WebApiEngine.Mappers;

/// <summary>
/// Enthält explizite Abbildungen für Versions- und Definitionsobjekte.
/// Dadurch bleibt das Mapping nachvollziehbar und unabhängig von AutoMapper.
/// </summary>
public static class DefinitionMappingExtensions
{
    public static BpmnDefinitionDto ToDto(this BpmnDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new BpmnDefinitionDto
        {
            Id = definition.Id,
            DefinitionId = definition.DefinitionId,
            PreviousGuid = definition.PreviousGuid,
            Hash = definition.Hash,
            SavedByUser = definition.SavedByUser,
            SavedOn = definition.SavedOn,
            DeployedByUser = definition.DeployedByUser,
            DeployedOn = definition.DeployedOn,
            Version = definition.Version.ToDto()
        };
    }

    public static BpmnMetaDefinitionDto ToDto(this BpmnMetaDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new BpmnMetaDefinitionDto
        {
            DefinitionId = definition.DefinitionId,
            Name = definition.Name,
            Description = definition.Description
        };
    }

    public static ExtendedBpmnMetaDefinitionDto ToDto(this ExtendedBpmnMetaDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new ExtendedBpmnMetaDefinitionDto
        {
            DefinitionId = definition.DefinitionId,
            Name = definition.Name,
            Description = definition.Description,
            LatestVersion = definition.LatestVersion?.ToDto(),
            LatestVersionDateTime = definition.LatestVersionDateTime,
            DeployedId = definition.DeployedId,
            DeployedVersion = definition.DeployedVersion?.ToDto(),
            DeployedVersionDateTime = definition.DeployedVersionDateTime ?? default
        };
    }

    public static BpmnMetaDefinition ToModel(this BpmnMetaDefinitionDto definitionDto)
    {
        ArgumentNullException.ThrowIfNull(definitionDto);

        return new BpmnMetaDefinition
        {
            DefinitionId = definitionDto.DefinitionId,
            Name = definitionDto.Name,
            Description = definitionDto.Description
        };
    }

    public static VersionDto ToDto(this Version version)
    {
        ArgumentNullException.ThrowIfNull(version);

        return new VersionDto
        {
            Major = version.Major,
            Minor = version.Minor
        };
    }

    public static Version ToModel(this VersionDto versionDto)
    {
        ArgumentNullException.ThrowIfNull(versionDto);

        return new Version
        {
            Major = versionDto.Major,
            Minor = versionDto.Minor
        };
    }
}
