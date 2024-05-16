namespace BPMN.Flowzer;

public interface IFlowzerOutputMapping
{
    ImmutableList<FlowzerIoMapping>? OutputMappings { get; init; }
}