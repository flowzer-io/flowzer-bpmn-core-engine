using BPMN.Service;

namespace BPMN.Data;

public record InputOutputBinding
{
    public required Operation OperationRef { get; init; }
    public required InputSet InputDataRef { get; init; }
    public required OutputSet OutputDataRef { get; init; }
}