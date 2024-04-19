using Common;
using Data;

namespace Events;

public class IntermediateCatchEvent(string name, IFlowElementContainer container, OutputSet outputSet) 
    : CatchEvent(name, container, outputSet);