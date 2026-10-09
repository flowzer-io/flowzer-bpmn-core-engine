using System.Text.Json.Serialization;

namespace Model;

/// <summary>Formularentscheidung mit optionalen atomaren Bedingungen für eingebettete Hosts.</summary>
public class UserTaskResult
{
    public required string FlowNodeId { get; set; }
    public Guid TokenId { get; set; }
    public Guid? ProcessInstanceId { get; set; }
    public long? ExpectedTaskRevision { get; set; }
    // Neue Defaultfelder fehlen im kanonischen JSON bewusst: Bereits persistierte
    // Legacy-Abschlussbelege müssen ihren bisherigen Requesthash behalten.
    /// <summary>Optional die tatsächlich angezeigte Subscription, kein Berechtigungsnachweis.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ExpectedUserTaskId { get; set; }
    /// <summary>Optional die unveränderliche Definition-Version des angezeigten Formulars.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ExpectedDefinitionId { get; set; }
    /// <summary>Verlangt bei einer neuen Entscheidung persönliche Zuweisung statt bloßem Kandidatenrecht.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool RequireAssignedToCurrentUser { get; set; }
    public string? ActionId { get; set; }

    public Variables? Data { get; set; }
}
