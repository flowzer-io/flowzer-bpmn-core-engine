namespace BPMN.Flowzer;

public interface IFlowzerOutputMapping
{
    List<FlowzerIoMapping>? OutputMappings { get; init; }
}