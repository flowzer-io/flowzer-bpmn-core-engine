using BPMN.Common;
using BPMN.Data;

namespace BPMN.Events;

public class StartEvent(string name, IFlowElementContainer container, OutputSet outputSet) : CatchEvent(name, container, outputSet);