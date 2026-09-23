import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { forwardRef, useEffect, useImperativeHandle } from 'react';
import { describe, expect, it, vi } from 'vitest';
import type { FormRendererHandle, FormRendererProps } from './FormRenderer';

vi.mock('./FormRenderer', () => ({
  FormRenderer: forwardRef<FormRendererHandle, FormRendererProps>(function Preview({ initialData, onChange, onReadyChange }, ref) {
    const data = { reason: 'Standard', ...initialData };
    useImperativeHandle(ref, () => ({ getData: () => data, validate: async () => true }));
    // Der Test-Doppelgänger meldet nur seine Montage, nicht jede Elternaktualisierung.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    useEffect(() => { onReadyChange?.(true); }, []);
    return <button onClick={() => onChange?.({ ...data, reason: 'Bearbeitet' })}>Testfeld ändern</button>;
  }),
}));

import { FormAuthoringPreview } from './FormAuthoringPreview';

const input = () => screen.getByLabelText('JSON-Testdaten');
const output = () => screen.getByLabelText('Aktuelle Formularwerte als JSON');

describe('FormAuthoringPreview', () => {
  // Testzweck: Ausgabewerte stammen vom Renderer einschließlich Defaults, nicht aus dem JSON-Texteditor.
  it('übernimmt erst ausdrücklich und zeigt danach tatsächliche Feldänderungen', async () => {
    render(<FormAuthoringPreview schema='{"components":[]}' />);
    await waitFor(() => expect(output()).toHaveValue(JSON.stringify({ reason: 'Standard' }, null, 2)));
    fireEvent.change(input(), { target: { value: '{"reason":"Urlaub"}' } });
    expect(output()).toHaveValue(JSON.stringify({ reason: 'Standard' }, null, 2));
    fireEvent.click(screen.getByText('Eingabe übernehmen'));
    await waitFor(() => expect(output()).toHaveValue(JSON.stringify({ reason: 'Urlaub' }, null, 2)));
    fireEvent.click(screen.getByText('Testfeld ändern'));
    expect(output()).toHaveValue(JSON.stringify({ reason: 'Bearbeitet' }, null, 2));
  });

  // Testzweck: Fehlerhafte Eingaben lassen ausgefüllte Werte stehen; Kopieren verwendet genau diese Ausgabe.
  it('erhält Werte bei Fehlern und kopiert den aktuellen JSON-Stand', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    render(<FormAuthoringPreview schema='{"components":[]}' />);
    await waitFor(() => expect(output()).toHaveValue(JSON.stringify({ reason: 'Standard' }, null, 2)));
    fireEvent.click(screen.getByText('Testfeld ändern'));
    fireEvent.change(input(), { target: { value: '{invalid}' } });
    fireEvent.click(screen.getByText('Eingabe übernehmen'));
    expect(screen.getByRole('alert')).toHaveTextContent('Ungültiges JSON');
    fireEvent.click(screen.getByText('JSON kopieren'));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith(JSON.stringify({ reason: 'Bearbeitet' }, null, 2)));
    expect(output()).toHaveValue(JSON.stringify({ reason: 'Bearbeitet' }, null, 2));
    fireEvent.click(screen.getByText('Testdaten zurücksetzen'));
    await waitFor(() => expect(output()).toHaveValue(JSON.stringify({ reason: 'Standard' }, null, 2)));
    expect(screen.queryByRole('alert')).toBeNull();
  });
});
