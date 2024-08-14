namespace core_engine.Extensions;

public static class ListFlowElementsExtension
{
    public static ICollection<FlowNode> GetStartFlowNodes(this IEnumerable<FlowElement> flowElements)
    {
        var elements = flowElements.ToArray();
        return elements.Where(x => x is StartEvent or Activity)
            .Where(x => elements.OfType<SequenceFlow>().All(f => f.TargetRef != x))
            .Cast<FlowNode>().ToList();
    }
    
    public static ICollection<FlowNode> GetStartFlowNodes(this Process process)
    {
        return process.FlowElements.GetStartFlowNodes();
    }
}