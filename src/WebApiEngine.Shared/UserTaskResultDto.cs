using System.Dynamic;
using System.Text.Json.Serialization;

namespace WebApiEngine.Shared;

public class UserTaskResultDto
{
    public required string FlowNodeId { get; set; }
    public Guid TokenId { get; set; }
    public Guid? ProcessInstanceId { get; set; }
    /// <summary>Optionaler Schutz gegen Abschluss aus einem vor einer Übergabe geöffneten Tab.</summary>
    public long? ExpectedTaskRevision { get; set; }
    /// <summary>Stabile ID der im veröffentlichten Aufgabenformular gewählten Aktion.</summary>
    public string? ActionId { get; set; }

    [JsonConverter(typeof(ExpandoObjectConverter))]
    public ExpandoObject? Data { get; set; }
}
