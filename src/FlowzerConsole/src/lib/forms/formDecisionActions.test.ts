import { describe, expect, it } from 'vitest';

import {
  inspectFormDecisionActions,
  readFormDecisionActions,
  writeFormDecisionActions,
} from './formDecisionActions';

describe('Formular-Entscheidungsaktionen', () => {
  // Testzweck: Der Editor bewahrt stabile IDs, Varianten und typisierte skalare
  // Feldbelegungen, ohne andere Root-Metadaten des Formulars zu verlieren.
  it('liest und schreibt deklarative Aktionen verlustfrei', () => {
    const schema = {
      display: 'form',
      flowzer: {
        existing: true,
        actions: [{
          id: 'approve',
          label: 'Freigeben',
          variant: 'primary',
          set: [
            { field: 'decision', value: 'approved' },
            { field: 'approved', value: true },
            { field: 'score', value: 1 },
            { field: 'note', value: null },
          ],
        }],
      },
      components: [],
    };

    const actions = readFormDecisionActions(schema);
    expect(actions).toEqual(schema.flowzer.actions);
    expect(writeFormDecisionActions(schema, actions)).toEqual(schema);
  });

  // Testzweck: Das Entfernen aller Aktionen entfernt nur die Aktionsliste; weitere
  // Flowzer-Vertragsmetadaten bleiben im Autorenentwurf erhalten.
  it('entfernt eine leere Aktionsliste ohne Nachbarwerte zu löschen', () => {
    expect(writeFormDecisionActions({
      flowzer: { contractVersion: 4, rules: [{ kind: 'dateOrder' }], actions: [{ id: 'old' }] },
      components: [],
    }, [])).toEqual({
      flowzer: { contractVersion: 4, rules: [{ kind: 'dateOrder' }] },
      components: [],
    });
  });

  // Testzweck: Ein gewöhnliches Altformular ohne Aktionen erhält beim bloßen Speichern
  // keinen bedeutungslosen leeren Flowzer-Metadatenblock.
  it('lässt ein Schema ohne Flowzer-Metadaten unverändert', () => {
    const schema = { display: 'form', components: [] };

    expect(writeFormDecisionActions(schema, [])).toBe(schema);
  });

  // Testzweck: Der visuelle Editor erkennt unbekannte oder unvollständige
  // Aktionsfragmente, statt sie beim nächsten Speichern stillschweigend zu verlieren.
  it('kennzeichnet nicht verlustfrei editierbare Aktionsfragmente', () => {
    expect(inspectFormDecisionActions({
      flowzer: {
        actions: [{
          id: 'approve',
          label: 'Freigeben',
          variant: 'primary',
          set: [{ field: 'decision' }],
        }],
      },
    })).toEqual({ actions: [], hasUnsupportedFragments: true });

    expect(inspectFormDecisionActions({
      flowzer: {
        actions: [{
          id: 'approve',
          label: 'Freigeben',
          variant: 'primary',
          set: [{ field: 'decision', value: 'approved' }],
          futurePolicy: true,
        }],
      },
    })).toEqual({ actions: [], hasUnsupportedFragments: true });
  });
});
