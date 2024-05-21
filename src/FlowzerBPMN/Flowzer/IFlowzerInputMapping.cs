namespace BPMN.Flowzer;

public interface IFlowzerInputMapping
{
    FlowzerList<FlowzerIoMapping>? InputMappings { get; init; }
}