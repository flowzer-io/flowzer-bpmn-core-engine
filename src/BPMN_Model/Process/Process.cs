using System.Text.Json.Serialization;
using BPMN_Model.Common;
using BPMN_Model.Foundation;

namespace BPMN_Model.Process;

public record Process : BaseElement
{
    // [JsonIgnore]
    public required Definitions Definitions { get; init; }
}