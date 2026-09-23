import { describe, expect, it } from 'vitest';

import { readProcessName, suggestWorkflowName, withDefinitionId } from './bpmnImport';

const XML = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  id="Definitions_Fremd" targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:process id="bestellprozess" name="Bestellprozess" isExecutable="true" />
</bpmn:definitions>`;

const WITHOUT_NAME = XML.replace(' name="Bestellprozess"', '');

describe('bpmnImport', () => {
  // Testzweck: Der Prozessname ist der beste Vorschlag für den Workflownamen.
  it('liest den Prozessnamen', () => {
    expect(readProcessName(XML)).toBe('Bestellprozess');
    expect(readProcessName(WITHOUT_NAME)).toBeNull();
  });

  // Testzweck: Ohne Prozessnamen bleibt der Dateiname — ohne Endung, damit im Katalog
  // kein „.bpmn" steht.
  it('fällt auf den Dateinamen ohne Endung zurück', () => {
    expect(suggestWorkflowName(WITHOUT_NAME, 'Bestellung.bpmn')).toBe('Bestellung');
    expect(suggestWorkflowName(XML, 'Bestellung.bpmn')).toBe('Bestellprozess');
  });

  // Testzweck: Die API liest die Definitionskennung aus dem XML. Bliebe die fremde
  // Kennung stehen, gehörte die gespeicherte Version nicht zum neuen Katalogeintrag.
  it('setzt die Definitionskennung', () => {
    const result = withDefinitionId(XML, 'definition_neu');

    expect(result).toContain('id="definition_neu"');
    expect(result).not.toContain('Definitions_Fremd');
    expect(result).toContain('<bpmn:process id="bestellprozess"');
  });

  // Testzweck: Eine unlesbare Datei darf nicht still ein halbes Modell ergeben.
  it('wirft bei unlesbarem XML', () => {
    expect(() => withDefinitionId('kein xml', 'definition_neu')).toThrow();
  });
});
