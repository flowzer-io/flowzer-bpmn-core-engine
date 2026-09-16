/** Reine Lesesicht für BPMN außerhalb der verlustfrei bearbeitbaren Gliederungsteilmenge. */
export interface OverviewNode {
  id: string;
  name: string;
  type: string;
  next: string[];
}

const NODE_TYPES = new Set([
  'task', 'userTask', 'manualTask', 'serviceTask', 'scriptTask', 'businessRuleTask',
  'sendTask', 'receiveTask', 'callActivity', 'subProcess', 'transaction',
  'startEvent', 'endEvent', 'intermediateCatchEvent', 'intermediateThrowEvent', 'boundaryEvent',
  'exclusiveGateway', 'inclusiveGateway', 'parallelGateway', 'eventBasedGateway', 'complexGateway',
]);

export function readOnlyOverview(xml: string): OverviewNode[] {
  const document = new DOMParser().parseFromString(xml, 'application/xml');
  if (document.getElementsByTagName('parsererror').length > 0) return [];
  const elements = Array.from(document.getElementsByTagNameNS('http://www.omg.org/spec/BPMN/20100524/MODEL', '*'));
  const flows = elements.filter(element => element.localName === 'sequenceFlow');
  return elements.filter(element => NODE_TYPES.has(element.localName) && element.hasAttribute('id'))
    .map(element => ({
      id: element.getAttribute('id')!, name: element.getAttribute('name') ?? '', type: element.localName,
      next: flows.filter(flow => flow.getAttribute('sourceRef') === element.getAttribute('id'))
        .map(flow => flow.getAttribute('targetRef')).filter((id): id is string => Boolean(id)),
    }));
}
