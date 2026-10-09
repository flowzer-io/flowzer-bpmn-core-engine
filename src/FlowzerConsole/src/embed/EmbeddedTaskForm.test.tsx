import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { EmbeddedTaskForm, type EmbedSnapshot } from './EmbeddedTaskForm';
import { EmbedActionError, type EmbedActionChannel } from './EmbedChannel';
const controls = vi.hoisted(() => ({ data: { answer: '' }, validate: vi.fn(async () => true), mounts: 0 }));
vi.mock('@/components/forms/FormRenderer', async () => {
  const React = await import('react');
  return { FormRenderer: React.forwardRef(({ initialData, onChange, onReadyChange }: { initialData: Record<string, unknown>; onChange: (data: unknown) => void; onReadyChange: (ready: boolean) => void }, ref) => {
    const [value, setValue] = React.useState(String(initialData.answer ?? '')); controls.data = { answer: value };
    const readyCallback = React.useRef(onReadyChange);
    React.useEffect(() => { controls.mounts++; readyCallback.current(true); }, []);
    React.useImperativeHandle(ref, () => ({ getData: () => controls.data, validate: controls.validate }));
    return <input aria-label="Antwort" value={value} onBlur={() => onChange(controls.data)} onChange={event => { setValue(event.target.value); onChange({ answer: event.target.value }); }} />;
  }) };
});
const snapshot: EmbedSnapshot = { userTaskId: '00000000-0000-0000-0000-000000000001', hostOrigin: 'https://host.example.test', taskRevision: 7,
  form: { formData: JSON.stringify({ components: [] }) }, context: {}, draft: { userTaskId: '00000000-0000-0000-0000-000000000001', revision: 1, data: { answer: 'alter Zwischenstand' } } };
describe('Langlebige eingebettete Human Task', () => {
  // Testzweck: Verzögerte unveränderte Form.io-/Directory-Signale nach einem Save
  // dürfen keinen falschen Ungespeichert-Zustand erzeugen; tatsächliche Edits schon.
  it('unterscheidet Inhaltsänderungen von identischen Renderer-Ereignissen', async () => {
    const request = vi.fn(async () => ({ ...snapshot.draft, revision: 2 }));
    render(<EmbeddedTaskForm snapshot={snapshot} channel={{ request } as unknown as EmbedActionChannel} />);
    fireEvent.click(screen.getByRole('button', { name: 'Zwischenstand speichern' }));
    await screen.findByText('Zwischenstand gespeichert');
    fireEvent.blur(screen.getByLabelText('Antwort'));
    expect(screen.getByRole('status')).toHaveTextContent('Zwischenstand gespeichert');
    fireEvent.change(screen.getByLabelText('Antwort'), { target: { value: 'echte Änderung' } });
    expect(screen.getByRole('status')).toHaveTextContent('Ungespeicherte Änderungen');
  });

  // Testzweck: Ein unklarer Abschluss hält ursprüngliche Daten, Aktion und
  // Idempotenzschlüssel unverändert fest und wiederholt nur exakt diesen Auftrag.
  it('wiederholt nach Timeout denselben eingefrorenen Abschlussauftrag', async () => {
    const request = vi.fn().mockRejectedValueOnce(new Error('Timeout')).mockResolvedValueOnce({ completed: true });
    controls.validate.mockClear();
    render(<EmbeddedTaskForm snapshot={snapshot} channel={{ request } as unknown as EmbedActionChannel} />);
    fireEvent.click(screen.getByRole('button', { name: 'Absenden' }));
    await screen.findByRole('alert');
    expect(screen.getByLabelText('Antwort')).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Zwischenstand speichern' })).toBeDisabled();
    // Emuliert eine interne Modeländerung; der Browser verhindert Benutzer-Edits.
    fireEvent.change(screen.getByLabelText('Antwort'), { target: { value: 'spätere Modeländerung' } });
    fireEvent.click(screen.getByRole('button', { name: 'Abschluss erneut bestätigen' }));
    await screen.findByText('Aufgabe abgeschlossen.');
    expect(request).toHaveBeenCalledTimes(2);
    expect(request.mock.calls[1]).toEqual(request.mock.calls[0]);
    expect(request.mock.calls[1]?.[1]?.data?.answer).toBe('alter Zwischenstand');
    expect(controls.validate).toHaveBeenCalledOnce();
  });

  // Testzweck: Ein langsamer Abschluss sperrt nur die weitere Eingabe, ohne den
  // bestehenden Renderer zu remounten; nach Fehler wird exakt derselbe Inhalt frei.
  it('verhindert verlorene Eingaben während eines laufenden Abschlusses', async () => {
    let reject!: (value: unknown) => void; const request = vi.fn(() => new Promise((_resolve, fail) => { reject = fail; }));
    const before = controls.mounts;
    render(<EmbeddedTaskForm snapshot={snapshot} channel={{ request } as unknown as EmbedActionChannel} />);
    fireEvent.click(screen.getByRole('button', { name: 'Absenden' }));
    await waitFor(() => expect(request).toHaveBeenCalledOnce());
    expect(screen.getByLabelText('Antwort')).toBeDisabled();
    expect(screen.getByLabelText('Antwort').closest('fieldset')).toHaveAttribute('inert');
    expect(controls.mounts).toBe(before + 1);
    await act(async () => reject(new EmbedActionError('flowzer.validation_failed')));
    expect(screen.getByLabelText('Antwort')).not.toBeDisabled();
    expect(screen.getByLabelText('Antwort')).toHaveValue('alter Zwischenstand');
    expect(controls.mounts).toBe(before + 1);
  });
  // Testzweck: Ein formal erfolgreicher Kanal ohne ausdrückliche fachliche
  // Abschlussbestätigung darf das Formular und seinen Inhalt nicht entfernen.
  it('behält den Renderer bei fehlender Abschlussbestätigung', async () => {
    const request = vi.fn(async () => ({}));
    render(<EmbeddedTaskForm snapshot={snapshot} channel={{ request } as unknown as EmbedActionChannel} />);
    fireEvent.click(screen.getByRole('button', { name: 'Absenden' }));
    await screen.findByRole('alert');
    expect(screen.getByLabelText('Antwort')).toHaveValue('alter Zwischenstand');
    expect(screen.queryByText('Aufgabe abgeschlossen.')).not.toBeInTheDocument();
  });

  // Testzweck: Pflichtfelder dürfen leer zwischengespeichert werden; keine Validierung/Completion und kein Renderer-Neuaufbau.
  it('speichert unvollständige Daten mit beiden Revisionen und behält den Renderer', async () => {
    const request = vi.fn(async () => ({ ...snapshot.draft, revision: 2 })); controls.validate.mockClear(); const before = controls.mounts;
    render(<EmbeddedTaskForm snapshot={snapshot} channel={{ request } as unknown as EmbedActionChannel} />);
    fireEvent.change(screen.getByLabelText('Antwort'), { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: 'Zwischenstand speichern' }));
    await screen.findByText('Zwischenstand gespeichert');
    expect(request).toHaveBeenCalledWith('draft.save', { expectedRevision: 1, expectedTaskRevision: 7, data: { answer: '' } });
    expect(controls.validate).not.toHaveBeenCalled(); expect(controls.mounts).toBe(before + 1);
  });
  // Testzweck: Mehrfachklicks erzeugen keinen doppelten Save; während des Saves weiterbearbeitete Daten bleiben ungespeichert erhalten.
  it('schützt Saves gegen Doppelklick und verliert parallele Eingaben nicht', async () => {
    let resolve!: (value: unknown) => void; const request = vi.fn(() => new Promise(r => { resolve = r; }));
    render(<EmbeddedTaskForm snapshot={snapshot} channel={{ request } as unknown as EmbedActionChannel} />);
    const button = screen.getByRole('button', { name: 'Zwischenstand speichern' }); fireEvent.click(button); fireEvent.click(button);
    expect(button).toBeDisabled(); expect(request).toHaveBeenCalledTimes(1);
    fireEvent.change(screen.getByLabelText('Antwort'), { target: { value: 'später bearbeitet' } });
    await act(async () => resolve({ ...snapshot.draft, revision: 2 }));
    expect(screen.getByLabelText('Antwort')).toHaveValue('später bearbeitet'); await screen.findByText('Ungespeicherte Änderungen');
  });
  // Testzweck: Revisionskonflikte und technische Savefehler dürfen keine Eingaben löschen oder fremde Entwürfe laden.
  it('behält Änderungen bei Speicherfehlern', async () => {
    const request = vi.fn(async () => { throw new Error('Speicherung fehlgeschlagen'); });
    render(<EmbeddedTaskForm snapshot={snapshot} channel={{ request } as unknown as EmbedActionChannel} />);
    fireEvent.change(screen.getByLabelText('Antwort'), { target: { value: 'bleibt' } }); fireEvent.click(screen.getByRole('button', { name: 'Zwischenstand speichern' }));
    await waitFor(() => expect(screen.getByRole('alert')).toBeVisible()); expect(screen.getByLabelText('Antwort')).toHaveValue('bleibt');
  });
  // Testzweck: Absenden besitzt eine getrennte fachliche Validierung und wird bei unvollständigen Daten nicht vermittelt.
  it('sendet bei fehlgeschlagener Formularprüfung nichts ab', async () => {
    const request = vi.fn(); controls.validate.mockResolvedValueOnce(false);
    render(<EmbeddedTaskForm snapshot={snapshot} channel={{ request } as unknown as EmbedActionChannel} />);
    fireEvent.click(screen.getByRole('button', { name: 'Absenden' })); await screen.findByRole('alert'); expect(request).not.toHaveBeenCalled();
  });
});
