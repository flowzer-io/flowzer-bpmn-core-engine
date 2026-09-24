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

// Testzweck: Auch der Editor und seine Vorschau folgen der Browsersprache statt fest Deutsch;
// die eigenen deutschen Editortexte bleiben für deutsche Browser erhalten.
it('übergibt die Browsersprache an den Form.io-Editor', async () => {
  builder.mockResolvedValue({ on: vi.fn(), destroy: vi.fn() });
  const languages = vi.spyOn(navigator, 'languages', 'get').mockReturnValue(['en-GB']);

  render(<FormBuilder schema='{"components":[]}' />);

  await waitFor(() => expect(builder).toHaveBeenCalledOnce());
  expect(builder.mock.calls[0]?.[2]).toMatchObject({
    language: 'en',
    i18n: { de: { searchFields: 'Komponenten suchen', addAnother: 'Weiteren Eintrag hinzufügen' } },
  });
  languages.mockRestore();
});
