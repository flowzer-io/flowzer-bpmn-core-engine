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

declare module 'bpmn-moddle' {
  interface ParseResult {
    rootElement: unknown;
    warnings: Error[];
  }

  interface SerializeResult {
    xml: string;
  }

  export class BpmnModdle {
    constructor(packages?: Record<string, unknown>);
    fromXML(xml: string): Promise<ParseResult>;
    toXML(element: unknown, options?: { format?: boolean }): Promise<SerializeResult>;
  }

}

declare module 'bpmn-auto-layout' {
  /** Version 1.3 liefert XML; spätere Versionen besitzen einen anderen Rückgabevertrag. */
  export function layoutProcess(xml: string): Promise<string>;
}
