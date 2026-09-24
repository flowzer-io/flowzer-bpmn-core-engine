import { act, render, waitFor } from '@testing-library/react';
import { beforeEach, expect, it, vi } from 'vitest';
import { FormBuilder } from './FormBuilder';

const builder = vi.hoisted(() => vi.fn());
vi.mock('@formio/js', () => ({ Formio: { builder } }));
vi.mock('./FlowzerSubjectComponent', () => ({ registerFlowzerSubjectComponent: vi.fn() }));
vi.mock('./FormSectionComponent', () => ({ registerFormSectionComponent: vi.fn() }));

beforeEach(() => builder.mockReset());

// Testzweck: Ein verspäteter Aufbau des alten Schemas darf beim Aufräumen nicht
// den schon sichtbaren Nachfolger im gemeinsam verwendeten React-Container löschen.
it('isoliert asynchrone Builder-Generationen', async () => {
  let completeOld: (() => void) | undefined;
  builder.mockImplementationOnce((host: HTMLElement) => new Promise((resolve) => {
    completeOld = () => resolve({ destroy: () => host.replaceChildren() });
  })).mockImplementationOnce(async (host: HTMLElement) => {
    host.textContent = 'Neuer Editor';
    return { on: vi.fn(), destroy: () => host.replaceChildren() };
  });
  const view = render(<FormBuilder schema='{"components":[]}' />);
  await waitFor(() => expect(builder).toHaveBeenCalledTimes(1));
  view.rerender(<FormBuilder schema='{"components":[],"title":"neu"}' />);
  await waitFor(() => expect(view.getByText('Neuer Editor')).toBeVisible());
  await act(async () => completeOld?.());
  expect(view.getByText('Neuer Editor')).toBeVisible();
});

// Testzweck: Der Formular-Editor bleibt auch in einem englischen Browser deutsch — wie die
// übrige Konsole und die eigenen Editortexte („Übernehmen“, Palettengruppen). Nur der Renderer
// folgt der Browsersprache.
it('hält den Form.io-Editor unabhängig von der Browsersprache deutsch', async () => {
  builder.mockResolvedValue({ on: vi.fn(), destroy: vi.fn() });
  const languages = vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['en-GB']);

  render(<FormBuilder schema='{"components":[]}' />);

  await waitFor(() => expect(builder).toHaveBeenCalledOnce());
  expect(builder.mock.calls[0]?.[2]).toMatchObject({
    language: 'de',
    i18n: { de: { save: 'Übernehmen', searchFields: 'Komponenten suchen', addAnother: 'Weiteren Eintrag hinzufügen' } },
  });
  languages.mockRestore();
});
