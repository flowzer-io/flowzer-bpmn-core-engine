namespace BPMN.Flowzer;

public interface IFlowzerInputMapping
{
    ImmutableList<FlowzerIoMapping>? InputMappings { get; init; }
}