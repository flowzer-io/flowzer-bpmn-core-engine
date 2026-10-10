/** Testfixture: Der vorhandene Renderer unter echter Sandbox und CSP, kein neues Feldsystem. */
import { useRef } from 'react';
import { createRoot } from 'react-dom/client';
import { Formio } from '@formio/js';
import { FormRenderer, type FormRendererHandle } from './components/forms/FormRenderer';
import { initializeOpaqueFormioRuntime, type OpaqueFormioRuntime } from './components/forms/opaqueFormioRuntime';
initializeOpaqueFormioRuntime(Formio as unknown as OpaqueFormioRuntime);
const schema = JSON.stringify({ components: [
  { type: 'textfield', key: 'answer', label: 'Antwort', input: true, validate: { required: true } },
  { type: 'datetime', key: 'date', label: 'Datum', input: true, enableTime: false },
] });
function Probe() {
  const form = useRef<FormRendererHandle>(null);
  return <><FormRenderer schema={schema} ref={form} initialData={{ answer: 'Zwischenstand' }}
    onReadyChange={ready => { if (ready) document.documentElement.dataset.ready = 'true'; }} />
    <button onClick={() => { document.documentElement.dataset.saved = String(form.current?.getData().answer); }}>Zwischenstand lesen</button>
    <button onClick={() => { void (Formio as unknown as OpaqueFormioRuntime).requireLibrary('ckeditor')
      .catch(() => { document.documentElement.dataset.assetDenied = 'true'; }); }}>Assetgrenze prüfen</button></>;
}
createRoot(document.getElementById('root')!).render(<Probe />);
