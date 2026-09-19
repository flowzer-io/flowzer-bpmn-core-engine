namespace WebApiEngine.Shared;

public class ProcessInstanceInfoDto
{
    public required Guid InstanceId { get; set; }
    
    public required Guid DefinitionId { get; set; }

    /// <summary>
    /// Version des Workflows, an die die Instanz gebunden ist. Null, wenn die gebundene
    /// Definition nicht mehr vorliegt — die Version wird dann nicht geraten.
    /// </summary>
    public VersionDto? DefinitionVersion { get; set; }

    public required string RelatedDefinitionId { get; set; }
    public required string RelatedDefinitionName { get; set; }
    public int MessageSubscriptionCount { get; set; }
    public int SignalSubscriptionCount { get; set; }
    public int UserTaskSubscriptionCount { get; set; }
    public int ServiceSubscriptionCount { get; set; }
    
    public ProcessInstanceStateDto State { get; set; }
    public List<TokenDto> Tokens { get; set; } = [];

    /// <summary>Nur bei expliziter Diagnoseberechtigung sind Tokens und technische Routen verfügbar.</summary>
    public bool CanInspect { get; set; }

    /// <summary>
    /// Warum die Instanz gescheitert ist, etwa „Unhandled BPMN error 'CODE' at 'Node'".
    /// Null bei jeder anderen Instanz und ohne Diagnoseberechtigung.
    /// </summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// Startzeitpunkt der Instanz (UTC), abgeleitet aus dem ältesten Token.
    /// Null, solange die Instanz noch kein Token besitzt.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// Endzeitpunkt der Instanz (UTC). Nur gesetzt, wenn die Instanz beendet ist.
    /// </summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>
    /// Die Instanz, deren Aufruf-Aktivität diesen Vorgang gestartet hat. Null bei einem
    /// Vorgang, der von Hand oder per Nachricht begonnen wurde.
    /// </summary>
    public Guid? ParentInstanceId { get; set; }

    /// <summary>
    /// Das wartende Token der Aufruf-Aktivität in der aufrufenden Instanz.
    /// </summary>
    public Guid? ParentTokenId { get; set; }
}

/// <summary>
/// Eine Kindinstanz in der Übersicht ihres Aufrufers: so viel, wie die Instanzansicht zum
/// Anzeigen und Verlinken braucht — ohne Tokens und ohne Prozessdaten.
/// </summary>
public class CalledInstanceDto
{
    public required Guid InstanceId { get; set; }
    public required string RelatedDefinitionId { get; set; }
    public required string RelatedDefinitionName { get; set; }

    /// <summary>Null, wenn die gebundene Definition nicht mehr vorliegt.</summary>
    public VersionDto? DefinitionVersion { get; set; }

    public ProcessInstanceStateDto State { get; set; }

    /// <summary>
    /// Die Aufruf-Aktivität im Elternprozess, an der dieser Vorgang hängt. Null, wenn das
    /// wartende Token im Aufrufer nicht mehr zu finden ist — etwa nach einem Abbruch.
    /// </summary>
    public string? CallActivityFlowNodeId { get; set; }
}
