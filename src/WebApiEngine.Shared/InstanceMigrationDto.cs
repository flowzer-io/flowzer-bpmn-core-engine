namespace WebApiEngine.Shared;

/// <summary>Die Instanzen, deren Umzug geprueft werden soll.</summary>
public sealed class InstanceMigrationPreviewRequestDto
{
    public required Guid[] InstanceIds { get; init; }
}

/// <summary>Die Instanzen samt der Zielversion, die der Aufrufer gesehen hat.</summary>
public sealed class InstanceMigrationRequestDto
{
    public required Guid[] InstanceIds { get; init; }

    /// <summary>
    /// Die Version, auf die migriert werden soll. Ist inzwischen eine andere deployt,
    /// antwortet die API mit 409, ohne etwas zu veraendern.
    /// </summary>
    public required Guid TargetDefinitionId { get; init; }
}

/// <summary>Ein Hindernis oder ein Hinweis. Der Code ist stabil, die Meldung technisch.</summary>
public sealed class InstanceMigrationFindingDto
{
    public required string Code { get; init; }
    public string? FlowNodeId { get; init; }
    public required string Message { get; init; }
}

public sealed class InstanceMigrationPreviewItemDto
{
    public required Guid InstanceId { get; init; }
    public required bool Migratable { get; init; }

    /// <summary>Gruende, aus denen diese Instanz nicht migriert wird.</summary>
    public required InstanceMigrationFindingDto[] Problems { get; init; }

    /// <summary>Was der Umzug mitnimmt, ohne ihn zu verhindern.</summary>
    public required InstanceMigrationFindingDto[] Notices { get; init; }
}

/// <summary>Der Trockenlauf: Quell- und Zielversion sowie das Ergebnis je Instanz.</summary>
public sealed class InstanceMigrationPreviewDto
{
    public required string RelatedDefinitionId { get; init; }
    public required string RelatedDefinitionName { get; init; }
    public required Guid SourceDefinitionId { get; init; }

    /// <summary>Null, wenn die Quellversion nicht mehr vorliegt; sie wird nicht geraten.</summary>
    public VersionDto? SourceVersion { get; init; }

    public required Guid TargetDefinitionId { get; init; }
    public required VersionDto TargetVersion { get; init; }
    public required InstanceMigrationPreviewItemDto[] Instances { get; init; }
}

public sealed class InstanceMigrationResultItemDto
{
    public required Guid InstanceId { get; init; }
    public required bool Migrated { get; init; }
    public required InstanceMigrationFindingDto[] Problems { get; init; }
}

/// <summary>Das Ergebnis des Umzugs; eine gescheiterte Instanz laesst die uebrigen unberuehrt.</summary>
public sealed class InstanceMigrationResultDto
{
    public required Guid TargetDefinitionId { get; init; }
    public required VersionDto TargetVersion { get; init; }
    public required InstanceMigrationResultItemDto[] Instances { get; init; }
}
