import { QueryClientProvider, type QueryClient } from '@tanstack/react-query';
import { createRoot, type Root } from 'react-dom/client';
import { FormLibraryPicker } from './FormLibraryPicker';

/** Bibliotheksauswahl im normalen Form.io-Komponentendialog, ohne frei eingegebene IDs. */
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export function registerFormLibrarySettings(Formio: any): void {
  const Components = Formio.Components;
  if (!Components?.components?.field || Components.components.flowzerFormSettings) return;
  class LibrarySettings extends Components.components.field {
    private reactRoot: Root | null = null;
    render() { return super.render('<div data-form-library-settings="true"></div>'); }
    attach(element: HTMLElement) {
      const attached = super.attach(element);
      const host = element.querySelector('[data-form-library-settings="true"]');
      if (!host) return attached;
      this.reactRoot?.unmount();
      this.reactRoot = createRoot(host);
      this.updateReact();
      return attached;
    }
    private updateReact() {
      if (!this.reactRoot) return;
      const queryClient = this.options?.flowzerQueryClient as QueryClient | undefined;
      if (!queryClient) {
        this.reactRoot.render(<p>Bitte das Formular in der Formularbibliothek öffnen.</p>);
        return;
      }
      // createRoot erbt den Query-Kontext nicht. Den Cache des Editors weiterreichen.
      this.reactRoot.render(<QueryClientProvider client={queryClient}>
        <FormLibraryPicker currentFormId={this.options?.flowzerAuthoringFormId ?? ''}
          key={`${this.dataValue ?? ''}:${this.root?.data?.version || this.options?.editComponent?.version || ''}`}
          initialFormId={this.dataValue ?? ''} initialVersion={this.root?.data?.version || this.options?.editComponent?.version || ''}
          onInsert={(version, name) => {
            this.root?.getComponent('version')?.setValue(`${version.version.major}.${version.version.minor}`);
            this.root?.getComponent('label')?.setValue(name);
            this.setValue(version.formId);
          }} />
      </QueryClientProvider>);
    }
    setValue(value: unknown, flags?: unknown) {
      const changed = super.setValue(value, flags);
      // Form.io lädt bestehende Dialogwerte gegebenenfalls erst nach attach().
      this.updateReact();
      return changed;
    }
    destroy(all?: boolean) {
      this.reactRoot?.unmount(); this.reactRoot = null; return super.destroy(all);
    }
  }
  Components.addComponent('flowzerFormSettings', LibrarySettings);
}
