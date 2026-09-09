import { describe, expect, it } from 'vitest';

import { appendSectionReference } from './sectionReferences';

describe('appendSectionReference', () => {
  it('fügt nur eine konkrete, geladene Abschnittsversion ein', () => {
    // Testzweck: Der Builder darf keine freie Section-ID oder latest-Referenz erzeugen.
    const result = JSON.parse(appendSectionReference('{"display":"form","components":[]}', {
      sectionId: '8e3b1840-08ec-468a-bf29-7c78c7ad1f4a',
      version: { major: 1, minor: 2 },
      label: 'Antragstellerdaten',
    })) as { components: Array<Record<string, unknown>> };

    expect(result.components).toEqual([expect.objectContaining({
      type: 'flowzerSection',
      sectionId: '8e3b1840-08ec-468a-bf29-7c78c7ad1f4a',
      version: '1.2',
    })]);
  });

  it('vergibt bei gleichem Abschnitt einen stabilen freien Key', () => {
    // Testzweck: Mehrfach eingefügte Referenzen müssen im späteren Formularvertrag eindeutige Keys behalten.
    const schema = JSON.stringify({ components: [{ type: 'text', key: 'antragstellerdaten' }] });
    const result = JSON.parse(appendSectionReference(schema, {
      sectionId: '8e3b1840-08ec-468a-bf29-7c78c7ad1f4a',
      version: { major: 0, minor: 1 },
      label: 'Antragstellerdaten',
    })) as { components: Array<Record<string, unknown>> };
    expect(result.components.at(-1)?.key).toBe('antragstellerdaten_2');
  });

  it('berücksichtigt auch Schlüssel in verschachtelten Komponenten', () => {
    // Testzweck: Der serverseitige Formularvertrag prüft Schlüssel global über den Komponentenbaum.
    const schema = JSON.stringify({ components: [{ type: 'panel', components: [{ key: 'daten' }] }] });
    const result = JSON.parse(appendSectionReference(schema, {
      sectionId: '8e3b1840-08ec-468a-bf29-7c78c7ad1f4a',
      version: { major: 0, minor: 1 },
      label: 'Daten',
    })) as { components: Array<Record<string, unknown>> };
    expect(result.components.at(-1)?.key).toBe('daten_2');
  });

  it('weist ungültige JSON-Schemata verständlich zurück', () => {
    // Testzweck: Ein Picker darf ein beschädigtes lokales Schema nicht still überschreiben.
    expect(() => appendSectionReference('{', {
      sectionId: '8e3b1840-08ec-468a-bf29-7c78c7ad1f4a',
      version: { major: 1, minor: 0 },
    })).toThrow('JSON-Objekt');
  });
});
