import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { EmbeddedStartForm, type EmbedStartSnapshot } from './EmbeddedStartForm';
import { EmbedActionError, type EmbedActionChannel } from './EmbedChannel';
const controls = vi.hoisted(() => ({ data: { answer: '' }, validate: vi.fn(async () => true), mounts: 0 }));
vi.mock('@/components/forms/FormRenderer', async () => {
  const React = await import('react');
  return { FormRenderer: React.forwardRef(({ onChange, onReadyChange }: { onChange?: (data: unknown) => void; onReadyChange: (ready: boolean) => void }, ref) => {
    const [value, setValue] = React.useState(''); controls.data = { answer: value };
    const readyCallback = React.useRef(onReadyChange);
    React.useEffect(() => { controls.mounts++; readyCallback.current(true); }, []);
    React.useImperativeHandle(ref, () => ({ getData: () => controls.data, validate: controls.validate }));
    return <input aria-label="Antwort" value={value} onChange={event => { setValue(event.target.value); onChange?.({ answer: event.target.value }); }} />;
  }) };
});
const snapshot: EmbedStartSnapshot = { definitionId: '11111111-1111-1111-1111-111111111111', relatedDefinitionId: 'workflow',
  hostOrigin: 'https://host.test', form: { formData: '{"components":[]}' } };
describe('Separates eingebettetes Startformular', () => {
  // Testzweck: Der Start benutzt den vorhandenen Renderer und allein den privaten
  // Hostkanal; keine künstliche Task/Draft-Aktion und keine Mehrfachstarts bei Doppelklick.
  it('startet ohne Task oder Draft genau einmal mit eingefrorenen Eingaben', async () => {
    let resolve!: (value: unknown) => void;
    const request = vi.fn<(operation: string, payload: unknown) => Promise<unknown>>(() => new Promise(r => { resolve = r; }));
    render(<EmbeddedStartForm snapshot={snapshot} channel={{ request } as unknown as EmbedActionChannel} />);
    expect(screen.queryByRole('button', { name: 'Zwischenstand speichern' })).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('Antwort'), { target: { value: 'Antrag' } });
    const button = screen.getByRole('button', { name: 'Workflow starten' });
    fireEvent.click(button); fireEvent.click(button);
    await waitFor(() => expect(request).toHaveBeenCalledOnce());
    expect(button).toBeDisabled(); expect(screen.getByLabelText('Antwort')).toBeDisabled();
    expect(request.mock.calls[0]?.[0]).toBe('workflow.start');
    expect(request.mock.calls[0]?.[1]).toEqual({ data: { answer: 'Antrag' }, idempotencyKey: expect.any(String) });
    await act(async () => resolve({ started: true }));
    await screen.findByText('Workflow gestartet.');
  });
  // Testzweck: Ein Timeout ist kein gescheiterter Start. Nur derselbe ursprüngliche
  // Auftrag darf wiederholt werden, ohne Renderer-Reload oder neue Idempotenzschlüssel.
  it('wiederholt einen unklaren Start unverändert und behält den Renderer', async () => {
    const request = vi.fn().mockRejectedValueOnce(new Error('Timeout')).mockResolvedValueOnce({ started: true });
    const before = controls.mounts; controls.validate.mockClear();
    render(<EmbeddedStartForm snapshot={snapshot} channel={{ request } as unknown as EmbedActionChannel} />);
    fireEvent.change(screen.getByLabelText('Antwort'), { target: { value: 'Original' } });
    fireEvent.click(screen.getByRole('button', { name: 'Workflow starten' }));
    await screen.findByRole('alert');
    expect(screen.getByLabelText('Antwort')).toBeDisabled();
    expect(screen.getByLabelText('Antwort').closest('fieldset')).toHaveAttribute('inert');
    fireEvent.change(screen.getByLabelText('Antwort'), { target: { value: 'intern später verändert' } });
    fireEvent.click(screen.getByRole('button', { name: 'Ursprünglichen Start erneut bestätigen' }));
    await screen.findByText('Workflow gestartet.');
    expect(request.mock.calls[1]).toEqual(request.mock.calls[0]);
    expect(request.mock.calls[1]?.[1]?.data.answer).toBe('Original');
    expect(controls.validate).toHaveBeenCalledOnce(); expect(controls.mounts).toBe(before + 1);
  });
  // Testzweck: Ein späterer Zugangs-/Vorprüfungsfehler beweist nicht, dass ein
  // früherer unklarer Start nichts erzeugt hat. Originaldaten/Key bleiben fest.
  it('vergisst einen unklaren Originalstart nicht durch spätere bekannte Fehler', async () => {
    const request = vi.fn().mockRejectedValueOnce(new Error('Timeout'))
      .mockRejectedValueOnce(new EmbedActionError('flowzer.access_denied')).mockResolvedValueOnce({ started: true });
    controls.validate.mockClear();
    render(<EmbeddedStartForm snapshot={snapshot} channel={{ request } as unknown as EmbedActionChannel} />);
    fireEvent.change(screen.getByLabelText('Antwort'), { target: { value: 'Original' } });
    fireEvent.click(screen.getByRole('button', { name: 'Workflow starten' }));
    await screen.findByRole('alert');
    fireEvent.click(screen.getByRole('button', { name: 'Ursprünglichen Start erneut bestätigen' }));
    await waitFor(() => expect(request).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Ursprünglichen Start erneut bestätigen' })).not.toBeDisabled());
    expect(screen.getByLabelText('Antwort')).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Ursprünglichen Start erneut bestätigen' }));
    await screen.findByText('Workflow gestartet.');
    expect(request.mock.calls[1]).toEqual(request.mock.calls[0]); expect(request.mock.calls[2]).toEqual(request.mock.calls[0]);
    expect(controls.validate).toHaveBeenCalledOnce();
  });
  // Testzweck: Ein bekannter Validierungsfehler gibt die ursprünglichen Eingaben frei,
  // ohne neuen Renderer; der Parent liefert ausschließlich sichere Feldmeldungen.
  it('behält korrigierbare Eingaben nach einem Fachfehler', async () => {
    const request = vi.fn().mockRejectedValueOnce(new EmbedActionError('flowzer.validation_failed'));
    const before = controls.mounts;
    render(<EmbeddedStartForm snapshot={snapshot} channel={{ request } as unknown as EmbedActionChannel} />);
    fireEvent.change(screen.getByLabelText('Antwort'), { target: { value: 'bleibt' } });
    fireEvent.click(screen.getByRole('button', { name: 'Workflow starten' }));
    await screen.findByRole('alert'); expect(screen.getByLabelText('Antwort')).not.toBeDisabled();
    expect(screen.getByLabelText('Antwort')).toHaveValue('bleibt'); expect(controls.mounts).toBe(before + 1);
  });
  // Testzweck: Ohne erfolgreiche Pflichtfeldprüfung wird kein Start an den Host vermittelt.
  it('prüft vollständig vor dem ersten Startauftrag', async () => {
    const request = vi.fn(); controls.validate.mockResolvedValueOnce(false);
    render(<EmbeddedStartForm snapshot={snapshot} channel={{ request } as unknown as EmbedActionChannel} />);
    fireEvent.click(screen.getByRole('button', { name: 'Workflow starten' }));
    await screen.findByRole('alert'); expect(request).not.toHaveBeenCalled();
  });
  // Testzweck: Nur eine ausdrücklich bestätigte Instanz bindet einen erfolgreichen Start;
  // ein leeres/malformes Ergebnis darf weder Eingaben entfernen noch einen Neustart erlauben.
  it('behandelt fehlende Erfolgsbestätigung als unklar', async () => {
    const request = vi.fn(async () => ({}));
    render(<EmbeddedStartForm snapshot={snapshot} channel={{ request } as unknown as EmbedActionChannel} />);
    fireEvent.click(screen.getByRole('button', { name: 'Workflow starten' }));
    await screen.findByRole('alert'); expect(screen.queryByText('Workflow gestartet.')).not.toBeInTheDocument();
    expect(screen.getByLabelText('Antwort')).toBeDisabled();
  });
  // Testzweck: Definitiver Versionskonflikt ist kein Timeout. Der alte Snapshot
  // wird nicht erneut gestartet; Eingaben bleiben bis zum bewussten Schließen sichtbar.
  it('stoppt eine nachweislich veraltete Startfassung ohne Unknown-Retry', async () => {
    const request = vi.fn().mockRejectedValue(new EmbedActionError('flowzer.definition_changed'));
    render(<EmbeddedStartForm snapshot={snapshot} channel={{ request } as unknown as EmbedActionChannel} />);
    fireEvent.change(screen.getByLabelText('Antwort'), { target: { value: 'bleibt sichtbar' } });
    fireEvent.click(screen.getByRole('button', { name: 'Workflow starten' }));
    await screen.findByRole('alert');
    expect(screen.getByRole('alert')).toHaveTextContent('Workflowfassung');
    expect(screen.queryByRole('button', { name: 'Ursprünglichen Start erneut bestätigen' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Workflow starten' })).toBeDisabled();
    expect(screen.getByLabelText('Antwort')).toHaveValue('bleibt sichtbar'); expect(request).toHaveBeenCalledOnce();
  });
  // Testzweck: Ein späterer Versionsfehler löst einen vorher unklaren Start nicht
  // auf; Originaldaten und ursprünglicher Key dürfen dadurch nicht ersetzt werden.
  it('behält Unknown bei einem erst später auftretenden Versionskonflikt', async () => {
    const request = vi.fn().mockRejectedValueOnce(new Error('Timeout'))
      .mockRejectedValueOnce(new EmbedActionError('flowzer.definition_changed')).mockResolvedValueOnce({ started: true });
    render(<EmbeddedStartForm snapshot={snapshot} channel={{ request } as unknown as EmbedActionChannel} />);
    fireEvent.click(screen.getByRole('button', { name: 'Workflow starten' })); await screen.findByRole('alert');
    fireEvent.click(screen.getByRole('button', { name: 'Ursprünglichen Start erneut bestätigen' }));
    await waitFor(() => expect(request).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Ursprünglichen Start erneut bestätigen' })).not.toBeDisabled());
    expect(screen.getByRole('alert')).not.toHaveTextContent('wähle die neue Fassung');
    expect(screen.getByLabelText('Antwort')).toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: 'Ursprünglichen Start erneut bestätigen' }));
    await screen.findByText('Workflow gestartet.'); expect(request.mock.calls[2]).toEqual(request.mock.calls[0]);
  });

});
