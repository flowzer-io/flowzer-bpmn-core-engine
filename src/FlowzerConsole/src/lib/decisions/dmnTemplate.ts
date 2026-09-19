/**
 * Die Vorlage für eine neue Entscheidung.
 *
 * Eine leere DMN-Datei gibt es nicht: dmn-js öffnet eine Datei ohne Entscheidung mit einer
 * leeren DRD-Fläche, auf der sich zwar etwas anlegen lässt, die aber niemanden zum Ziel
 * führt. Die Vorlage bringt deshalb genau eine Entscheidung mit einer Entscheidungstabelle
 * mit — eine Eingabespalte, eine Ausgabespalte, keine Regel.
 *
 * Bewusst DMN 1.3 (`https://www.omg.org/spec/DMN/20191111/MODEL/`): Das ist der Stand, den
 * der Parser unter `src/FlowzerDmn` liest, und dieselbe Fassung, die dmn-js schreibt.
 *
 * Der `DMNDI`-Teil ist kein Beiwerk. Ohne Diagrammangaben zeigt die DRD-Ansicht die
 * Entscheidung nicht an; dmn-js öffnet dann ersatzweise direkt die Tabelle, und die
 * Übersicht bliebe dauerhaft leer.
 */

/** Trefferregel der Vorlage: genau eine Regel darf greifen. */
const HIT_POLICY = 'UNIQUE';

/**
 * Erzeugt das DMN-XML einer neuen Entscheidung.
 *
 * Die Kennungen sind fest und nicht gewürfelt: Eine DMN-Datei steht für sich, und der
 * Katalog unterscheidet die Einträge über die Kennung der Definition, die die API vergibt.
 * `decisionId` ist dagegen der Name, den ein Business-Rule-Task später aufruft — er soll
 * lesbar sein und aus dem Namen entstehen.
 */
export function newDecisionXml(name: string, decisionId = defaultDecisionId(name)): string {
  const safeName = escapeXml(name.trim() || 'Neue Entscheidung');
  const safeDecisionId = escapeXml(decisionId);

  return `<?xml version="1.0" encoding="UTF-8"?>
<definitions xmlns="https://www.omg.org/spec/DMN/20191111/MODEL/"
             xmlns:dmndi="https://www.omg.org/spec/DMN/20191111/DMNDI/"
             xmlns:dc="http://www.omg.org/spec/DMN/20180521/DC/"
             id="Definitions_${safeDecisionId}"
             name="${safeName}"
             namespace="http://flowzer.maass.it/dmn"
             exporter="Flowzer Console"
             exporterVersion="1.0">
  <decision id="${safeDecisionId}" name="${safeName}">
    <decisionTable id="DecisionTable_${safeDecisionId}" hitPolicy="${HIT_POLICY}">
      <input id="Input_1" label="Eingabe">
        <inputExpression id="InputExpression_1" typeRef="string">
          <text></text>
        </inputExpression>
      </input>
      <output id="Output_1" label="Ergebnis" name="ergebnis" typeRef="string" />
    </decisionTable>
  </decision>
  <dmndi:DMNDI>
    <dmndi:DMNDiagram id="DMNDiagram_1">
      <dmndi:DMNShape id="DMNShape_${safeDecisionId}" dmnElementRef="${safeDecisionId}">
        <dc:Bounds height="80" width="180" x="160" y="100" />
      </dmndi:DMNShape>
    </dmndi:DMNDiagram>
  </dmndi:DMNDI>
</definitions>
`;
}

/**
 * Eine DMN-Kennung aus dem Namen. Umlaute werden ausgeschrieben statt entfernt: „Prüfung"
 * soll `Pruefung` heißen und nicht `Prfung`. Bleibt nichts Brauchbares übrig, gilt ein
 * fester Ersatz — eine Kennung darf nicht leer sein und nicht mit einer Ziffer beginnen.
 */
export function defaultDecisionId(name: string): string {
  const transliterated = name
    .replace(/ä/g, 'ae').replace(/ö/g, 'oe').replace(/ü/g, 'ue')
    .replace(/Ä/g, 'Ae').replace(/Ö/g, 'Oe').replace(/Ü/g, 'Ue')
    .replace(/ß/g, 'ss');

  const identifier = transliterated
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/[^A-Za-z0-9]+/g, '_')
    .replace(/^_+|_+$/g, '');

  if (identifier.length === 0) return 'Entscheidung';
  return /^[0-9]/.test(identifier) ? `Entscheidung_${identifier}` : identifier;
}

/** Maskiert die fünf Zeichen, die in XML-Text und -Attributen eine Bedeutung haben. */
function escapeXml(value: string): string {
  return value
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&apos;');
}
