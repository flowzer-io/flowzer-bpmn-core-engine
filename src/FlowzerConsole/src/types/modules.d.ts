/**
 * Typdeklarationen für bpmn.io-Pakete ohne mitgelieferte Typen.
 * Sie sind absichtlich schmal gehalten: nur das, was die Konsole tatsächlich nutzt.
 */

declare module 'bpmn-js-properties-panel' {
  export const BpmnPropertiesPanelModule: unknown;
  export const BpmnPropertiesProviderModule: unknown;
  export const ZeebePropertiesProviderModule: unknown;
  export const CamundaPlatformPropertiesProviderModule: unknown;
}

declare module 'camunda-bpmn-js-behaviors/lib/camunda-cloud' {
  const behaviors: unknown;
  export default behaviors;
}

declare module 'bpmn-js/lib/Modeler' {
  const Modeler: unknown;
  export default Modeler;
}

declare module 'bpmn-js/lib/NavigatedViewer' {
  const NavigatedViewer: unknown;
  export default NavigatedViewer;
}

declare module 'bpmn-js/lib/Viewer' {
  const Viewer: unknown;
  export default Viewer;
}
