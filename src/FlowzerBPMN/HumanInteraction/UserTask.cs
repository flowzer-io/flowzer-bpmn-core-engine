namespace BPMN.HumanInteraction;

/// <summary>
/// Legt fest, ob eine Aufgabe weiterhin über freie IdP-Texte oder über stabile,
/// serverseitig geprüfte Verzeichniskennungen zugewiesen wird.
/// </summary>
public enum UserTaskAssignmentMode
{
    Text = 0,
    Directory = 1
}

public record UserTask : Activities.Task, IFlowzerInputMapping, IFlowzerOutputMapping
{
    public required string Implementation { get; init; }

    public FlowzerList<Rendering> Renderings { get; init; } = [];

    public string? FlowzerAssignee { get; init; }
    public string? FlowzerCandidateGroups { get; init; }
    public string? FlowzerCandidateUsers { get; init; }

    /// <summary>
    /// Modelle ohne Flowzer-Erweiterung bleiben aus Kompatibilitätsgründen im Textmodus.
    /// </summary>
    public UserTaskAssignmentMode FlowzerAssignmentMode { get; init; } = UserTaskAssignmentMode.Text;

    /// <summary>Stabile lokale Benutzer-ID des direkten Bearbeiters im Verzeichnismodus.</summary>
    public Guid? FlowzerDirectoryAssigneeUserId { get; init; }

    /// <summary>Stabile lokale Benutzer-IDs der Kandidaten im Verzeichnismodus.</summary>
    public FlowzerList<Guid> FlowzerDirectoryCandidateUserIds { get; init; } = [];

    /// <summary>Stabile lokale Gruppen-IDs der Kandidatengruppen im Verzeichnismodus.</summary>
    public FlowzerList<Guid> FlowzerDirectoryCandidateGroupIds { get; init; } = [];

    public string? FlowzerDueDate { get; init; }
    public string? FlowzerFollowUpDate { get; init; }
    public string? FlowzerPriority { get; init; }
    public FlowzerList<FlowzerIoMapping>? InputMappings { get; init; }
    public FlowzerList<FlowzerIoMapping>? OutputMappings { get; init; }
}
