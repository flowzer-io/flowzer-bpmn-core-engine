using BPMN.Service;

namespace BPMN.Data;

public class InputOutputBinding
{
    public required Operation OperationRef { get; set; }
    public required InputSet InputDataRef { get; set; }
    public required OutputSet OutputDataRef { get; set; }
}