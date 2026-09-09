import { describe, expect, it } from 'vitest';

import { registerFormSectionComponent } from './FormSectionComponent';

function fakeFormio() {
  class Field {
    component: Record<string, unknown> = {};
    static schema(defaults: Record<string, unknown>, ...extensions: Array<Record<string, unknown>>) {
      return Object.assign({}, defaults, ...extensions);
    }
    render(markup: string) { return markup; }
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

describe('flowzerSection Form.io component', () => {
  it('registriert eine nicht editierbare, konkrete Referenz', () => {
    // Testzweck: Abschnittsreferenzen müssen im Builder sichtbar bleiben, ohne freie IDs oder Versionen anzubieten.
    const formio = fakeFormio();
    registerFormSectionComponent(formio);
    const component = formio.Components.registered.get('flowzerSection') as unknown as {
      schema: () => Record<string, unknown>;
      builderInfo: { title: string; group: string };
      editForm: () => { components: Array<Record<string, unknown>> };
    };
    expect(component.schema()).toMatchObject({ type: 'flowzerSection', input: false });
    expect(component.builderInfo.title).toBe('Wiederverwendbarer Abschnitt');
    expect(component.builderInfo.group).not.toBe('basic');
    expect(component.editForm().components.every((entry) => entry.disabled === true)).toBe(true);
  });

  it('registriert den Typ nur einmal', () => {
    // Testzweck: Wiederholtes Öffnen von Formularen darf die globale Form.io-Registry nicht mit Klassen füllen.
    const formio = fakeFormio();
    registerFormSectionComponent(formio);
    registerFormSectionComponent(formio);
    expect(formio.Components.registered.size).toBe(1);
  });
});
