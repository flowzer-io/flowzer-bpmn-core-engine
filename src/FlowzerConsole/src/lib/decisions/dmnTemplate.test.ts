import { describe, expect, it } from 'vitest';

import { defaultDecisionId, newDecisionXml } from './dmnTemplate';

describe('DMN-Vorlage', () => {
  // Testzweck: Die Vorlage ist wohlgeformtes DMN 1.3 mit genau einer Entscheidung und
  // Diagrammangaben — ohne sie bliebe die DRD-Ansicht von dmn-js leer.
  it('erzeugt eine Entscheidung samt Diagrammangaben', () => {
    const xml = newDecisionXml('Rabattstufe');
    const document = new DOMParser().parseFromString(xml, 'application/xml');

    expect(document.querySelector('parsererror')).toBeNull();
    expect(document.documentElement.namespaceURI).toBe('https://www.omg.org/spec/DMN/20191111/MODEL/');

    const decisions = document.getElementsByTagName('decision');
    expect(decisions).toHaveLength(1);
    expect(decisions[0]!.getAttribute('id')).toBe('Rabattstufe');
    expect(xml).toContain('DMNShape');
    expect(xml).toContain('hitPolicy="UNIQUE"');
  });

  // Testzweck: Ein Name mit Umlauten, Leerzeichen und Anfuehrungszeichen darf weder eine
  // unguelige DMN-Kennung noch kaputtes XML ergeben.
  it('macht aus einem umgangssprachlichen Namen eine gueltige Kennung', () => {
    expect(defaultDecisionId('Prüfung der Bonität')).toBe('Pruefung_der_Bonitaet');
    expect(defaultDecisionId('2026 Tarif')).toBe('Entscheidung_2026_Tarif');
    expect(defaultDecisionId('  ***  ')).toBe('Entscheidung');

    const xml = newDecisionXml('Tarif "Gold" & Co');
    expect(xml).toContain('name="Tarif &quot;Gold&quot; &amp; Co"');
    expect(new DOMParser().parseFromString(xml, 'application/xml').querySelector('parsererror')).toBeNull();
  });
});
