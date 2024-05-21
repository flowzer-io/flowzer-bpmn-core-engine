namespace BPMN.Flowzer;

public interface IFlowzerOutputMapping
{
    FlowzerList<FlowzerIoMapping>? OutputMappings { get; init; }
}