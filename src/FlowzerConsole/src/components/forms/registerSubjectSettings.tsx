import { createRoot, type Root } from 'react-dom/client';
import { SubjectSelectionSettings } from './SubjectSelectionSettings';

/** Nur im Komponentendialog verwendeter Policy-Editor; kein veröffentlichter Feldtyp. */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function registerSubjectSettings(Formio: any): void {
  const Components = Formio.Components;
  if (!Components?.components?.field || Components.components.flowzerSubjectSettings) return;
  class SubjectSettings extends Components.components.field {
    private reactRoot: Root | null = null;
    render() { return super.render('<div data-subject-settings="true"></div>'); }
    private updateReact() {
      this.reactRoot?.render(<SubjectSelectionSettings value={this.dataValue ?? {}}
        formId={this.options?.flowzerAuthoringFormId} onChange={value => this.setValue(value)} />);
    }
    attach(element: HTMLElement) {
      const attached = super.attach(element);
      const host = element.querySelector('[data-subject-settings="true"]');
      if (host) { this.reactRoot?.unmount(); this.reactRoot = createRoot(host); this.updateReact(); }
      return attached;
    }
    setValue(value: unknown, flags?: unknown) { const changed = super.setValue(value, flags); this.updateReact(); return changed; }
    destroy(all?: boolean) { this.reactRoot?.unmount(); this.reactRoot = null; return super.destroy(all); }
  }
  Components.addComponent('flowzerSubjectSettings', SubjectSettings);
}
