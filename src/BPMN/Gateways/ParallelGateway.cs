using BPMN.Common;

namespace BPMN.Gateways;

public class ParallelGateway(string name, IFlowElementContainer container) : Gateway(name, container);