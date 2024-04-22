using BPMN.Activities;
using BPMN.Common;
using BPMN.Events;
using BPMN.Gateways;
using BPMN.Infrastructure;
using BPMN.Process;
using CatchEvent = BPMN.Common.CatchEvent;

// ReSharper disable MemberCanBePrivate.Global

namespace core_engine;

public class BpmnModel(Definitions definitions)
{
    public Definitions Definitions { get; } = definitions;

    public List<Process> Processes => Definitions.RootElements.OfType<Process>().ToList();
    public List<Process> ExecutableProcesses => Processes.Where(p => p.IsExecutable).ToList();
    public List<StartEvent> StartEvents => Definitions.FlowNodes.OfType<StartEvent>().ToList();
    public string Id => Definitions.Id;

    public List<CatchEvent> GetCatchEvents(CatchEvent actualNode)
    {
        var nodes = new List<CatchEvent>();
        nodes.Add(actualNode);
        if (actualNode.GetType().IsAssignableTo(typeof(Activity)))
        {
            var intermediateEvents =
                Definitions.FlowNodes.Where(f =>
                        f.GetType().IsAssignableTo(typeof(BoundaryEvent)))
                    .Select(f => (BoundaryEvent)f)
                    .Where(f => f.AttachedToRef == (Activity)actualNode);
            nodes.AddRange(intermediateEvents);
        }
        else if (actualNode.GetType().IsAssignableTo(typeof(EventBasedGateway)))
        {
            // Hier herausfinden, welche Events geworfen werden können
        }
        return nodes;
    }
    
    public BpmnInstance ExecuteProcess(Process process)
    {
        var instance = new BpmnInstance { Model = this, Process = process };
        instance.Start();
        return instance;
    }
}