import { describe, expect, it } from 'vitest';

import {
  ensureFlowzerSubjectContract,
  registerFlowzerSubjectComponent,
  toSubjectRefValue,
} from './FlowzerSubjectComponent';

function fakeFormio() {
  class Field {
    static schema(defaults: Record<string, unknown>, ...extensions: Array<Record<string, unknown>>) {
      return Object.assign({}, defaults, ...extensions);
    }
  }
  const registered = new Map<string, typeof Field>();
  return {
    Components: {
      components: { field: Field },
      addComponent: (name: string, component: typeof Field) => registered.set(name, component),
      registered,
    },
  };
}

// Testzweck: Das Form.io-Feld muss als Version-2-Vertrag starten und die deutschen
// Policy-Defaults unverändert in jedes neu angelegte Schema schreiben.
describe('flowzerSubject Form.io component', () => {
  // Testzweck: Submission-Daten enthalten nur typisierte SubjectRefs; Single und Multi
  // unterscheiden sich ausschließlich durch Objekt versus Array.
  it('serialisiert Single-/Multi-Auswahl ohne Anzeigeprojektion', () => {
    const selected = [{
      subject: { kind: 'user' as const, id: 'u-1' },
      displayName: 'Anna',
      detail: 'anna@example.test',
      available: true,
    }];
    expect(toSubjectRefValue(selected, false)).toEqual({ kind: 'user', id: 'u-1' });
    expect(toSubjectRefValue(selected, true)).toEqual([{ kind: 'user', id: 'u-1' }]);
  });

  // Testzweck: Der Root-Vertrag gehört zum gesamten Formularschema und wird beim Speichern
  // ergänzt, sobald mindestens ein flowzerSubject-Feld vorhanden ist.
  it('setzt contractVersion am Schema-Root', () => {
    expect(ensureFlowzerSubjectContract({ components: [{ columns: [[{ type: 'flowzerSubject' }]] }] })).toEqual({
      components: [{ columns: [[{ type: 'flowzerSubject' }]] }],
      flowzer: { contractVersion: 2 },
    });
  });

  // Testzweck: Der Form.io-Builder muss das Feld genau einmal unter dem erwarteten Typ
  // mit einer verständlichen und zum Serververtrag passenden Konfiguration registrieren.
  it('registriert Schema und verständliche Builder-Konfiguration', () => {
    const formio = fakeFormio();
    registerFlowzerSubjectComponent(formio);

    const component = formio.Components.registered.get('flowzerSubject') as unknown as {
      schema: () => Record<string, unknown>;
      builderInfo: { title: string };
      editForm: () => { components: Array<Record<string, unknown>> };
    };
    expect(component).toBeDefined();
    expect(component.schema()).toMatchObject({
      type: 'flowzerSubject',
      flowzer: {
        subjectSelection: {
          allowUsers: true,
          allowGroups: false,
          activeOnly: true,
          includeSubgroups: false,
          allowedUserIds: [],
          userMemberOfGroupIds: [],
          allowedGroupIds: [],
        },
      },
    });
    expect(component.builderInfo.title).toBe('Benutzer-/Gruppenauswahl');
    expect(component.editForm().components.map((entry) => entry.label)).toEqual(
      expect.arrayContaining(['Benutzer auswählbar', 'Gruppen auswählbar', 'Untergruppen einbeziehen']),
    );
    expect(component.editForm().components.find(
      (entry) => entry.key === 'flowzer.subjectSelection.allowedUserIds',
    )).toMatchObject({ as: 'json', editor: 'ace' });
  });
});
