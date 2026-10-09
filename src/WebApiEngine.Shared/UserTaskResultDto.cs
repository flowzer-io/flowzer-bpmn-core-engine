using System.Dynamic;
using System.Text.Json.Serialization;

namespace WebApiEngine.Shared;

/// <summary>Authentifiziert vermittelter Abschluss; Identität stammt niemals aus diesem Body.</summary>
public class UserTaskResultDto
{
    public required string FlowNodeId { get; set; }
    public Guid TokenId { get; set; }
    public Guid? ProcessInstanceId { get; set; }
    /// <summary>Optionaler Schutz gegen Abschluss aus einem vor einer Übergabe geöffneten Tab.</summary>
    public long? ExpectedTaskRevision { get; set; }
    /// <summary>Optional erwartete Subscription-ID; eine abweichende oder leere ID bleibt verborgen (404).</summary>
    public Guid? ExpectedUserTaskId { get; set; }
    /// <summary>Optional unveränderliche Definition-Version des geöffneten Formulars; Umzug ergibt 409.</summary>
    public Guid? ExpectedDefinitionId { get; set; }
    /// <summary>Bei true ist tatsächliche persönliche Zuweisung erforderlich, auch mit Betriebsrecht.</summary>
    public bool RequireAssignedToCurrentUser { get; set; }
    /// <summary>Stabile ID der im veröffentlichten Aufgabenformular gewählten Aktion.</summary>
    public string? ActionId { get; set; }

    [JsonConverter(typeof(ExpandoObjectConverter))]
    public ExpandoObject? Data { get; set; }
}
