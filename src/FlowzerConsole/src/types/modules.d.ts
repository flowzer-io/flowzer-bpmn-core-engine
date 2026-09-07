/**
 * Typdeklarationen für bpmn.io-Pakete ohne mitgelieferte Typen.
 * Sie sind absichtlich schmal gehalten: nur das, was die Konsole tatsächlich nutzt.
 */

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
