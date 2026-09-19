import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { ExtendedUserTask } from '@flowzer/sdk';
import type { UserTaskActions } from '@flowzer/react';

import { TaskFormCard } from './TaskFormCard';
import type { TaskDraftEditor } from '@/lib/taskDraft';
import type { TaskView } from '@/lib/taskView';

const task = {
  id: 'task-1',
  name: 'Rechnung freigeben',
  token: { id: 'token-1', currentFlowNodeId: 'Human', variables: { betrag: 12 } },
  processInstanceId: 'instance-1',
  formKey: '',
  workState: { revision: 3, canWork: true },
} as unknown as ExtendedUserTask;

const view: TaskView = {
  task,
  id: 'task-1',
  title: 'Rechnung freigeben',
  workflowName: 'Rechnungslauf',
  dueDate: null,
  dueRaw: null,
  dueLabel: 'ohne Frist',
  dueBucket: 'undated',
  priority: null,
  startedAt: null,
  formKey: null,
};

const draft: TaskDraftEditor = {
  currentData: { betrag: 12 },
  initialData: { betrag: 12 },
  revision: 0,
  hasDraft: false,
  updatedAtUtc: null,
  dirty: false,
  loadState: 'ready',
  saveState: 'idle',
  error: null,
  isRefreshing: false,
  isSaving: false,
  isDiscarding: false,
  formInstanceKey: 1,
  setData: vi.fn(),
  save: vi.fn(),
  discard: vi.fn(),
  adoptServerDraft: vi.fn(),
};

function createActions(complete: ReturnType<typeof vi.fn>) {
  const idle = { isPending: false, error: null, mutate: vi.fn(), reset: vi.fn() };
  return {
    claim: idle,
    release: idle,
    assign: idle,
    delegate: idle,
    saveDraft: idle,
    deleteDraft: idle,
    complete: { ...idle, mutate: complete },
    searchAssignees: vi.fn(),
    resolveAssignees: vi.fn(),
  } as unknown as UserTaskActions;
}

function renderCard(
  workspace: Partial<Parameters<typeof TaskFormCard>[0]['workspace']> = {},
  complete = vi.fn(),
) {
  render(
    <TaskFormCard
      view={view}
      workspace={{
        task,
        form: null,
        pending: false,
        error: null,
        directoryAdapter: undefined,
        ...workspace,
      }}
      controls={{ draft, actions: createActions(complete), lifecyclePending: false, taskRevision: 3 }}
      onCompleted={vi.fn()}
      onDefer={vi.fn()}
    />,
  );
  return complete;
}

describe('Aufgabe ohne Formular', () => {
  // Testzweck: Eine Aufgabe, deren Modell kein Formular bindet, ist kein Fehlerfall —
  // sie wird als reine Bestätigung angeboten, nicht als fehlgeschlagene Formularsuche.
  it('zeigt den Abschlussknopf statt einer Fehlermeldung', () => {
    renderCard();

    expect(screen.getByText('Aufgabe bestätigen')).toBeInTheDocument();
    expect(screen.queryByText(/kein Formular verfügbar/)).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Abschließen' })).toBeEnabled();
  });

  // Testzweck: Ohne Formular ist die Dokumentation des Knotens die einzige Erklärung,
  // die die Aufgabe ihrem Bearbeiter noch anbieten kann.
  it('zeigt die Dokumentation der Aufgabe, wenn das Modell eine trägt', () => {
    renderCard({ task: { ...task, documentation: 'Bitte sachlich prüfen.' } as ExtendedUserTask });

    expect(screen.getByText('Bitte sachlich prüfen.')).toBeInTheDocument();
  });

  // Testzweck: Der Abschluss ohne Formular darf keine Prozessvariablen zurückschreiben;
  // aus dem Entwurfsstand würde sonst ein stilles Überschreiben.
  it('schließt ohne Eingaben und ohne Variablen ab', async () => {
    const user = userEvent.setup();
    const complete = renderCard();

    await user.click(screen.getByRole('button', { name: 'Abschließen' }));

    expect(complete).toHaveBeenCalledTimes(1);
    expect(complete.mock.calls[0]?.[0]).toMatchObject({
      command: { flowNodeId: 'Human', tokenId: 'token-1', data: {} },
    });
  });

  // Testzweck: Solange das Formular noch geladen wird, darf die Oberfläche nicht
  // behaupten, es gäbe keines — sonst blitzt der Bestätigungsknopf kurz auf.
  it('behauptet während des Ladens nichts', () => {
    renderCard({ form: undefined, pending: true });

    expect(screen.queryByText('Aufgabe bestätigen')).not.toBeInTheDocument();
    expect(screen.getByText('Formular ausfüllen')).toBeInTheDocument();
  });
});
