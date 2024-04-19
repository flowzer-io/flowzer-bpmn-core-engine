using BPMN.Common;
using BPMN.Data;

namespace BPMN.Events;

public class IntermediateCatchEvent(string name, IFlowElementContainer container, OutputSet outputSet) 
    : CatchEvent(name, container, outputSet);