namespace core_engine.Extensions;

public static class ListFlowElementsExtension
{
    public static ICollection<FlowNode> GetStartFlowNodes(this IEnumerable<FlowElement> flowElements)
    {
        var elements = flowElements.ToArray();
        return elements.Where(x => x is StartEvent or Activity)
            // Ein Event-Subprozess ist kein Startknoten seines Containers: Er beginnt erst,
            // wenn sein Startereignis im umschliessenden Scope eintrifft. Ohne diese Ausnahme
            // liefe er sofort mit los, weil auf ihn kein Sequenzfluss zeigt.
            .Where(x => x is not SubProcess { TriggeredByEvent: true })
            .Where(x => elements.OfType<SequenceFlow>().All(f => f.TargetRef != x))
            .Cast<FlowNode>().ToList();
    }

    /// <summary>
    /// Die Event-Subprozesse eines Containers. Sie sind die Ereignisfaenger des Scopes: Solange
    /// er laeuft, sind ihre Startereignisse scharf — wie Boundary-Events an einer Aktivitaet.
    /// </summary>
    public static IEnumerable<SubProcess> GetEventSubProcesses(this IFlowElementContainer container) =>
        container.FlowElements.OfType<SubProcess>().Where(subProcess => subProcess.TriggeredByEvent);

    /// <summary>
    /// Das eine Startereignis eines Event-Subprozesses. Der Faehigkeitsvertrag laesst genau eines
    /// zu; ein aelteres Modell ohne eines bleibt hier einfach ohne Faenger.
    /// </summary>
    public static StartEvent? GetEventSubProcessStartEvent(this SubProcess eventSubProcess) =>
        eventSubProcess.FlowElements.OfType<StartEvent>().FirstOrDefault();

    public static ICollection<FlowNode> GetStartFlowNodes(this Process process)
    {
        return process.FlowElements.GetStartFlowNodes();
    }
}
