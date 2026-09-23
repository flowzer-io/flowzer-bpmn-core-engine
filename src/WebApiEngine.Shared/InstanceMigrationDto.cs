namespace WebApiEngine.Shared;

/// <summary>Die Instanzen, deren Umzug geprueft werden soll.</summary>
public sealed class InstanceMigrationPreviewRequestDto
{
    public required Guid[] InstanceIds { get; init; }

    /// <summary>
    /// Zuordnung von der Kennung eines wartenden Quellknotens auf die Kennung seines
    /// Zielknotens. Sie gilt fuer alle Instanzen der Anfrage; sie laufen auf derselben
    /// Quellversion und teilen deshalb dieselben Knoten.
    /// </summary>
    public Dictionary<string, string>? FlowNodeMapping { get; init; }
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

    /// <summary>Dieselbe Zuordnung wie im Trockenlauf; ohne sie bliebe der Knoten unbeantwortet.</summary>
    public Dictionary<string, string>? FlowNodeMapping { get; init; }
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

/// <summary>Ein Knoten, den die Zuordnung erfragt oder zur Auswahl stellt.</summary>
public sealed class MigrationFlowNodeDto
{
    public required string Id { get; init; }

    /// <summary>Null, wenn der Knoten im Modell keinen Namen traegt oder das Modell fehlt.</summary>
    public string? Name { get; init; }

    /// <summary>Der Elementtyp, etwa "UserTask"; Quelle und Ziel muessen darin uebereinstimmen.</summary>
    public required string Type { get; init; }
}

/// <summary>
/// Was die Bedienung noch beantworten muss, und wovon sie dabei waehlen kann. Beides gilt fuer
/// die ganze Anfrage, nicht je Instanz.
/// </summary>
public sealed class InstanceMigrationMappingDto
{
    /// <summary>Wartende Quellknoten ohne brauchbaren Zielknoten.</summary>
    public required MigrationFlowNodeDto[] Required { get; init; }

    /// <summary>Die Knoten der obersten Ebene der Zielversion.</summary>
    public required MigrationFlowNodeDto[] Targets { get; init; }
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

    /// <summary>Die offenen Zuordnungen und die Knoten, die dafuer zur Wahl stehen.</summary>
    public required InstanceMigrationMappingDto Mapping { get; init; }
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
