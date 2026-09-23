import { registerFormLibrarySettings } from './registerFormLibrarySettings';
import { componentEditForm } from './componentEditForm';

/** Registriert den Platzhalter für ein aus der Formularbibliothek gewähltes Formular. */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function registerFormLibraryComponent(Formio: any): void {
  if (!Formio?.Components) return;
  registerFormLibrarySettings(Formio);
  const Components = Formio.Components;
  if (!Components?.components?.field || Components.components.flowzerForm) return;

  const BaseField = Components.components.field;
  class FlowzerFormComponent extends BaseField {
    static schema(...extend: unknown[]) {
      return BaseField.schema({
        type: 'flowzerForm', label: 'Subformular', key: 'formular', input: false,
        tableView: false, formId: '', version: '',
      }, ...extend);
    }

    static get builderInfo() {
      return {
        title: 'Subformular',
        group: 'basic', icon: 'files', weight: 26,
        schema: FlowzerFormComponent.schema(),
      };
    }

    static editForm() {
      return componentEditForm([
        { type: 'flowzerFormSettings', key: 'formId', label: 'Subformular auswählen', hideLabel: true, input: true },
        { type: 'textfield', key: 'label', label: 'Beschriftung', input: true },
        { type: 'textfield', key: 'version', label: 'Gebundene Version', input: true, disabled: true },
      ]);
    }

    render() {
      const label = typeof this.component.label === 'string' ? this.component.label : 'Formular';
      const version = typeof this.component.version === 'string' ? this.component.version : '';
      return super.render(`<div class="formio-flowzer-section-placeholder" role="status">${escapeHtml(label)}${version ? ` · v${escapeHtml(version)}` : ''}</div>`);
    }
  }

  Components.addComponent('flowzerForm', FlowzerFormComponent);
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (character) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
  })[character] ?? character);
}
