using Service;

namespace Data;

public class InputOutputBinding(Operation operationRef, InputSet inputDataRef, OutputSet outputDataRef)
{
    public Operation OperationRef { get; set; } = operationRef;
    public InputSet InputDataRef { get; set; } = inputDataRef;
    public OutputSet OutputDataRef { get; set; } = outputDataRef;
}