import { describe, expect, it } from 'vitest';

import { registerFormLibraryComponent } from './FormLibraryComponent';

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

describe('flowzerForm Form.io component', () => {
  it('bietet Subformulare direkt in der Palette und mit gebundener Auswahl an', () => {
    // Testzweck: Der Form.io-Dialog bietet eine Bibliotheksauswahl statt freier IDs und behält seinen tabs-Vertrag.
    const formio = fakeFormio();
    registerFormLibraryComponent(formio);
    const component = formio.Components.registered.get('flowzerForm') as unknown as {
      schema: () => Record<string, unknown>;
      builderInfo: { group: string; title: string };
      editForm: () => { components: Array<{ type: string; components: Array<{ components: Array<{ disabled: boolean }> }> }> };
    };

    expect(component.schema()).toMatchObject({ type: 'flowzerForm', input: false });
    expect(component.builderInfo).toMatchObject({ group: 'basic', title: 'Subformular' });
    expect(component.editForm().components[0]!.type).toBe('tabs');
    expect(JSON.stringify(component.editForm())).toContain('flowzerFormSettings');
  });
});
