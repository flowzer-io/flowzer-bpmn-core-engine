import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, render, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { BoundDirectorySubjectAdapter } from '@/components/bpmn/properties/DirectorySubjectPicker';

const createForm = vi.hoisted(() => vi.fn());

vi.mock('@formio/js', () => ({
  Formio: { createForm },
  Widgets: {},
}));
vi.mock('./dialogCalendarWidget', () => ({ registerDialogCalendarWidget: vi.fn() }));
vi.mock('./FlowzerSubjectComponent', () => ({ registerFlowzerSubjectComponent: vi.fn() }));

import { FormRenderer } from './FormRenderer';

describe('FormRenderer', () => {
  beforeEach(() => {
    createForm.mockReset();
    createForm.mockResolvedValue({
      submission: { data: {} },
      on: vi.fn(),
      checkValidity: vi.fn(() => true),
      destroy: vi.fn(),
    });
  });

  // Testzweck: Verschachtelte Form.io-Roots erhalten nur den vom Host gebundenen
  // Directory-Adapter; der Renderer reicht keine Task-ID oder freie API-Hook-Konfiguration
  // in das Drittanbieter-Formular durch.
  it('reicht den gebundenen Directory-Adapter in die Form.io-Optionen', async () => {
    const directoryAdapter: BoundDirectorySubjectAdapter = {
      cacheKey: ['task-1', 'field-1'],
      search: vi.fn(),
      resolve: vi.fn(),
    };

    render(<FormRenderer schema='{"components":[]}' directoryAdapter={directoryAdapter} />);

    await waitFor(() => expect(createForm).toHaveBeenCalledOnce());
    expect(createForm.mock.calls[0]?.[2]).toMatchObject({
      flowzerDirectoryAdapter: directoryAdapter,
      flowzerDirectoryContext: undefined,
    });
  });
});

// Testzweck: Ein später fertiggestellter alter Renderer darf die neue Vorschau
// nach einem Schemawechsel nicht durch sein destroy() leeren.
it('isoliert asynchrone Vorschaugenerationen', async () => {
  createForm.mockReset();
  let completeOld: (() => void) | undefined;
  createForm.mockImplementationOnce((host: HTMLElement) => new Promise((resolve) => {
    completeOld = () => resolve({ destroy: () => host.replaceChildren() });
  })).mockImplementationOnce(async (host: HTMLElement) => {
    host.textContent = 'Neue Vorschau';
    return { on: vi.fn(), destroy: () => host.replaceChildren() };
  });
  const view = render(<FormRenderer schema='{"components":[]}' />);
  await waitFor(() => expect(createForm).toHaveBeenCalledTimes(1));
  view.rerender(<FormRenderer schema='{"components":[],"title":"neu"}' />);
  await waitFor(() => expect(view.getByText('Neue Vorschau')).toBeVisible());
  await act(async () => completeOld?.());
  expect(view.getByText('Neue Vorschau')).toBeVisible();
});

// Testzweck: Der separate React-Root eines Startformular-Pickers muss denselben
// QueryClient wie sein Host erhalten, ohne einen zweiten Cache anzulegen.
it('reicht den vorhandenen QueryClient an verschachtelte Form.io-Roots weiter', async () => {
  createForm.mockReset();
  createForm.mockResolvedValue({ on: vi.fn(), destroy: vi.fn() });
  const client = new QueryClient();
  render(<QueryClientProvider client={client}><FormRenderer schema='{"components":[]}' /></QueryClientProvider>);
  await waitFor(() => expect(createForm).toHaveBeenCalledOnce());
  expect(createForm.mock.calls[0]?.[2].flowzerQueryClient).toBe(client);
});

// Testzweck: Der Vorschau-Host erfährt, wann Standardwerte wirklich lesbar sind,
// ohne ein fachliches Change-Ereignis oder eine Absendung künstlich auszulösen.
it('meldet den bereiten Renderer und beim Abbau nicht bereit', async () => {
  createForm.mockReset();
  const onReadyChange = vi.fn();
  createForm.mockResolvedValue({ submission: { data: { reason: 'Standard' } }, on: vi.fn(), destroy: vi.fn() });
  const view = render(<FormRenderer schema='{"components":[]}' onReadyChange={onReadyChange} />);
  await waitFor(() => expect(onReadyChange).toHaveBeenLastCalledWith(true));
  view.unmount();
  expect(onReadyChange).toHaveBeenLastCalledWith(false);
});

// Testzweck: Verzögerte Change-Ereignisse zerstörter Form.io-Instanzen dürfen nach
// Reset oder Formularwechsel keine alten Werte in die aktuelle Vorschau zurückschreiben.
it('ignoriert verspätete Änderungen abgebauter Renderer', async () => {
  createForm.mockReset();
  const listeners: Array<() => void> = [];
  createForm.mockImplementation(async () => ({
    submission: { data: { old: true } },
    on: (_event: string, callback: () => void) => listeners.push(callback),
    destroy: vi.fn(),
  }));
  const onChange = vi.fn();
  const view = render(<FormRenderer schema='{"components":[]}' onChange={onChange} />);
  await waitFor(() => expect(listeners).toHaveLength(1));
  view.rerender(<FormRenderer schema='{"components":[],"title":"neu"}' onChange={onChange} />);
  await waitFor(() => expect(listeners).toHaveLength(2));
  act(() => listeners[0]?.());
  expect(onChange).not.toHaveBeenCalled();
  act(() => listeners[1]?.());
  expect(onChange).toHaveBeenCalledOnce();
});
