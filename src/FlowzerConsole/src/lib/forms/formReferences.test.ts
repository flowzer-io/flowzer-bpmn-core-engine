import { describe, expect, it } from 'vitest';

import { appendFormReference } from './formReferences';

describe('appendFormReference', () => {
  it('fügt eine konkrete Formularversion als Komponente ein', () => {
    // Testzweck: Weder freie IDs noch "latest" werden vom Picker erzeugt.
    const result = JSON.parse(appendFormReference('{"display":"form","components":[]}', {
      formId: '8e3b1840-08ec-468a-bf29-7c78c7ad1f4a',
      version: { major: 1, minor: 2 },
      label: 'Antragstellerdaten',
    })) as { components: Array<Record<string, unknown>> };

    expect(result.components).toEqual([expect.objectContaining({
      type: 'flowzerForm',
      formId: '8e3b1840-08ec-468a-bf29-7c78c7ad1f4a',
      version: '1.2',
      key: 'antragstellerdaten',
    })]);
  });

  it('vergibt über den gesamten Baum einen freien Komponentenschlüssel', () => {
    // Testzweck: Das expandierte Formular darf später keine vorhandenen Keys verdecken.
    const schema = JSON.stringify({ components: [{ type: 'panel', components: [{ key: 'adresse' }] }] });
    const result = JSON.parse(appendFormReference(schema, {
      formId: '8e3b1840-08ec-468a-bf29-7c78c7ad1f4a',
      version: { major: 0, minor: 1 },
      label: 'Adresse',
    })) as { components: Array<Record<string, unknown>> };
    expect(result.components.at(-1)?.key).toBe('adresse_2');
  });
});
