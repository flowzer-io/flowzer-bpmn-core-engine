import { componentEditForm } from './componentEditForm';

/**
 * Form.io-Brücke für eine Abschnittsreferenz. Die Oberfläche zeigt im Editor nur
 * einen nicht bearbeitbaren Platzhalter; der Server expandiert die konkrete Version
 * beim Veröffentlichen und speichert den eigenständigen Snapshot.
 */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function registerFormSectionComponent(Formio: any): void {
  const Components = Formio?.Components;
  if (!Components?.components?.field || Components.components.flowzerSection) return;

  const BaseField = Components.components.field;
  class FlowzerSectionComponent extends BaseField {
    static schema(...extend: unknown[]) {
      return BaseField.schema({
        type: 'flowzerSection',
        label: 'Wiederverwendbarer Abschnitt',
        key: 'abschnitt',
        input: false,
        tableView: false,
        sectionId: '',
        version: '',
      }, ...extend);
    }

    static get builderInfo() {
      return {
        title: 'Wiederverwendbarer Abschnitt',
        // Der Typ muss registriert sein, damit bestehende Referenzen erhalten bleiben.
        // Eingefügt wird er jedoch nur über den servergebundenen Picker; eine nicht
        // konfigurierte Gruppe verhindert die freie Palette mit leerer ID/Version.
        group: 'flowzerSectionReferences',
        icon: 'view_agenda',
        weight: 90,
        schema: FlowzerSectionComponent.schema(),
      };
    }

    static editForm() {
      return componentEditForm([
        { type: 'textfield', key: 'label', label: 'Beschriftung', input: true, disabled: true },
        { type: 'textfield', key: 'sectionId', label: 'Abschnitt-ID', input: false, disabled: true },
        { type: 'textfield', key: 'version', label: 'Version', input: false, disabled: true },
      ]);
    }

    render() {
      const label = typeof this.component.label === 'string' ? this.component.label : 'Formularabschnitt';
      const version = typeof this.component.version === 'string' ? this.component.version : '';
      return super.render(`<div class="formio-flowzer-section-placeholder" role="status">${escapeHtml(label)}${version ? ` · v${escapeHtml(version)}` : ''}</div>`);
    }
  }

  Components.addComponent('flowzerSection', FlowzerSectionComponent);
}

// Label/Version kommen aus dem eigenen Schema. Das Escaping hält den Editor auch
// bei importierten, unerwarteten Texten frei von HTML-Injection.
function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (character) => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
  })[character] ?? character);
}
