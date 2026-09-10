import { useState } from 'react';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { SubjectRefDto } from '@/lib/api/types';

import {
  DirectorySubjectPicker,
  type DirectorySubjectSelection,
} from './DirectorySubjectPicker';

const searchMock = vi.hoisted(() => vi.fn());
const resolutionMock = vi.hoisted(() => vi.fn());
const formSearchMock = vi.hoisted(() => vi.fn());
const formResolutionMock = vi.hoisted(() => vi.fn());

vi.mock('@/lib/api/queries', () => ({
  useDirectorySubjectSearch: searchMock,
  useDirectorySubjectResolutions: resolutionMock,
  useFormDirectorySubjectSearch: formSearchMock,
  useFormDirectorySubjectResolutions: formResolutionMock,
}));

const userSubject: SubjectRefDto = { kind: 'user', id: 'user-1' };
const groupSubject: SubjectRefDto = { kind: 'group', id: 'group-1' };

function searchResult(items: Array<{ subject: SubjectRefDto; displayName: string; detail: string }> = []) {
  return { data: { generationId: 'generation-1', items }, isPending: false, isFetching: false, error: null };
}

function renderPicker(
  overrides: Partial<React.ComponentProps<typeof DirectorySubjectPicker>> = {},
) {
  const onChange = overrides.onChange ?? vi.fn();
  const props: React.ComponentProps<typeof DirectorySubjectPicker> = {
    definitionId: 'workflow-1',
    kind: 'user',
    selected: [],
    multiple: true,
    label: 'Benutzer',
    onChange,
    ...overrides,
  };
  return { onChange, ...render(<DirectorySubjectPicker {...props} />) };
}

describe('DirectorySubjectPicker', () => {
  beforeEach(() => {
    searchMock.mockReset();
    searchMock.mockReturnValue(searchResult());
    resolutionMock.mockReset();
    resolutionMock.mockReturnValue({ data: [], isPending: false, isFetching: false, error: null });
    formSearchMock.mockReset();
    formSearchMock.mockReturnValue(searchResult());
    formResolutionMock.mockReset();
    formResolutionMock.mockReturnValue({ data: [], isPending: false, isFetching: false, error: null });
  });

  // Testzweck: Der Picker wartet bis zur Mindestlänge und übergibt dem Hook den Workflow,
  // den Suchtext und die gewünschte Identitätsart — keine globale Directory-Suche.
  it('sucht erst ab zwei Zeichen workflowgebunden', async () => {
    const user = userEvent.setup();
    renderPicker();

    const input = screen.getByRole('searchbox', { name: 'Benutzer suchen' });
    await user.type(input, 'a');
    expect(searchMock).toHaveBeenLastCalledWith('workflow-1', '', 'user', false);

    await user.type(input, 'n');
    await waitFor(() => expect(searchMock).toHaveBeenLastCalledWith('workflow-1', 'an', 'user', true), {
      timeout: 500,
    });
  });

  // Testzweck: Formularfelder verwenden den fachlich gebundenen Startformular-Endpunkt
  // und dürfen nicht auf die allgemeine Workflow-Suche oder eine globale Suche ausweichen.
  it('übergibt Formularfeldkontext und Feldschlüssel an die Suche', async () => {
    const user = userEvent.setup();
    renderPicker({
      definitionId: '',
      kind: 'all',
      directoryContext: { kind: 'startForm', definitionId: 'workflow-1' },
      fieldKey: 'approvers',
      label: 'Benutzer oder Gruppen',
    });

    await user.type(screen.getByRole('searchbox', { name: 'Benutzer oder Gruppen suchen' }), 'an');
    await waitFor(() => expect(formSearchMock).toHaveBeenLastCalledWith(
      { kind: 'startForm', definitionId: 'workflow-1' },
      'approvers',
      'an',
      'all',
      true,
    ), { timeout: 500 });
    expect(searchMock).not.toHaveBeenCalled();
  });

  // Testzweck: Suchtreffer werden als stabile Referenzen zurückgegeben und doppelte IDs können
  // nicht mehrfach in einer Mehrfachauswahl landen.
  it('übernimmt einen Treffer und markiert doppelte Treffer als ausgewählt', async () => {
    const onChange = vi.fn();
    searchMock.mockReturnValue(
      searchResult([{ subject: userSubject, displayName: 'Anna Beispiel', detail: 'anna@example.test' }]),
    );
    const user = userEvent.setup();
    renderPicker({ onChange });

    await user.type(screen.getByRole('searchbox', { name: 'Benutzer suchen' }), 'an');
    await waitFor(() => expect(screen.getByRole('option', { name: /Anna Beispiel/ })).toBeInTheDocument(), {
      timeout: 500,
    });
    await user.click(screen.getByRole('option', { name: /Anna Beispiel/ }));

    expect(onChange).toHaveBeenCalledWith([
      {
        subject: userSubject,
        displayName: 'Anna Beispiel',
        detail: 'anna@example.test',
        available: true,
      },
    ] satisfies DirectorySubjectSelection[]);
  });

  // Testzweck: Ein gerade über die aktive Suche gewählter Treffer behält seine
  // serverseitige Anzeigeprojektion auch dann, wenn der Parent nur die UUID ins
  // noch ungespeicherte Modell zurückschreibt und die historische Auflösung ihn
  // deshalb bewusst noch nicht zurückliefert.
  it('bewahrt Suchprojektionen für noch nicht gespeicherte Referenzen im Kontextcache', async () => {
    searchMock.mockReturnValue(
      searchResult([{ subject: userSubject, displayName: 'Anna Beispiel', detail: 'subject-anna' }]),
    );
    function Harness() {
      const [ids, setIds] = useState<string[]>([]);
      return (
        <DirectorySubjectPicker
          definitionId="workflow-1"
          kind="user"
          selected={ids.map((id) => ({
            subject: { kind: 'user', id },
            displayName: id,
            detail: 'Stabile Verzeichnis-ID',
            available: false,
          }))}
          multiple
          label="Benutzer"
          onChange={(selection) => setIds(selection.map((entry) => entry.subject.id))}
        />
      );
    }
    const user = userEvent.setup();
    render(<Harness />);

    await user.type(screen.getByRole('searchbox', { name: 'Benutzer suchen' }), 'an');
    await waitFor(() => expect(screen.getByRole('option', { name: /Anna Beispiel/ })).toBeInTheDocument());
    await user.click(screen.getByRole('option', { name: /Anna Beispiel/ }));

    expect(screen.getByRole('button', { name: 'Anna Beispiel entfernen' })).toBeInTheDocument();
    expect(screen.queryByText(/Nicht auflösbar/)).not.toBeInTheDocument();
  });

  // Testzweck: Eine gespeicherte, nicht mehr aktive Referenz bleibt sichtbar und entfernbar,
  // damit ein erneutes Öffnen des Modelers keine Datenverluste durch die aktive Suche verursacht.
  it('zeigt unbekannte Referenzen als Warn-Chip', () => {
    renderPicker({
      selected: [
        {
          subject: groupSubject,
          displayName: 'Alte Gruppe',
          detail: 'group-1',
          available: false,
        },
      ],
      kind: 'group',
      label: 'Gruppen',
      multiple: false,
    });

    expect(screen.getByText('Alte Gruppe')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Alte Gruppe entfernen' })).toBeInTheDocument();
    expect(screen.getByText('Alte Gruppe').parentElement).toHaveClass('text-fail');
  });

  // Testzweck: Eine weiterhin aktive gespeicherte ID wird workflowgebunden aufgelöst und
  // dadurch nach dem erneuten Öffnen mit eindeutigem Anzeigenamen statt nur als UUID gezeigt.
  it('zeigt eine aufgelöste gespeicherte Referenz mit Namen und Detail', () => {
    resolutionMock.mockReturnValue({
      data: [{
        subject: userSubject,
        displayName: 'Anna Beispiel',
        detail: 'subject-anna',
        isActive: true,
        isSelectable: true,
      }],
      isPending: false,
      isFetching: false,
      error: null,
    });

    renderPicker({
      selected: [{ subject: userSubject, displayName: 'user-1', detail: 'Stabile Verzeichnis-ID', available: false }],
    });

    expect(screen.getByText('Anna Beispiel')).toBeInTheDocument();
    expect(screen.getByText(/subject-anna/)).toBeInTheDocument();
    expect(resolutionMock).toHaveBeenLastCalledWith('workflow-1', [userSubject], true);
  });

  // Testzweck: Eine serverseitig historisch aufgelöste, aber deaktivierte Identität
  // behält ihren eindeutigen Namen und zeigt zugleich sichtbar, dass sie nicht erneut
  // auswählbar ist.
  it('kennzeichnet deaktivierte aufgelöste Referenzen als nicht auswählbar', () => {
    resolutionMock.mockReturnValue({
      data: [{
        subject: userSubject,
        displayName: 'Anna Ehemalig',
        detail: 'subject-anna',
        isActive: false,
        isSelectable: false,
      }],
      isPending: false,
      isFetching: false,
      error: null,
    });

    renderPicker({
      selected: [{ subject: userSubject, displayName: 'user-1', detail: '', available: false }],
    });

    expect(screen.getByText(/Anna Ehemalig/)).toBeInTheDocument();
    expect(screen.getByText(/Deaktiviert/)).toBeInTheDocument();
    expect(screen.getByText(/Anna Ehemalig/).parentElement).toHaveClass('text-fail');
  });

  // Testzweck: Read-only darf bestehende Werte darstellen, aber weder den Such-Hook aktivieren
  // noch eine Eingabe- oder Löschaktion anbieten.
  it('bleibt im Read-only-Modus ohne Suche und Mutation', () => {
    const onChange = vi.fn();
    renderPicker({ disabled: true, onChange, selected: [{ subject: userSubject, displayName: 'Anna', detail: '', available: true }] });

    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument();
    expect(searchMock).toHaveBeenLastCalledWith('workflow-1', '', 'user', false);
    expect(screen.getByRole('button', { name: 'Anna entfernen' })).toBeDisabled();
  });

  // Testzweck: Verschachtelte Form.io-Roots verwenden ausschließlich die vom Host
  // gebundenen Callbacks; Task-, Feld- oder Aktionskennungen werden nicht aus dem
  // Suchtext rekonstruiert und kein Console-Query-Hook ist dafür erforderlich.
  it('verwendet einen gebundenen Adapter für Suche und Auflösung', async () => {
    const search = vi.fn().mockResolvedValue(
      searchResult([{ subject: userSubject, displayName: 'Anna Beispiel', detail: 'anna@example.test' }]).data,
    );
    const resolve = vi.fn().mockResolvedValue([{
      subject: userSubject,
      displayName: 'Anna Beispiel',
      detail: 'anna@example.test',
    }]);
    const adapter = { cacheKey: ['test'], search, resolve };
    const user = userEvent.setup();

    renderPicker({
      directoryAdapter: adapter,
      fieldKey: 'representative',
      selected: [{ subject: userSubject, displayName: 'user-1', detail: 'ID', available: false }],
      multiple: true,
    });

    await waitFor(() => expect(resolve).toHaveBeenCalledWith(
      'representative',
      [userSubject],
      expect.any(AbortSignal),
    ));
    await user.type(screen.getByRole('searchbox', { name: 'Benutzer suchen' }), 'an');
    await waitFor(() => expect(search).toHaveBeenCalledWith(
      'representative',
      { query: 'an', kind: 'user', signal: expect.any(AbortSignal) },
    ), { timeout: 1000 });
    expect(screen.getByRole('option', { name: /Anna Beispiel/ })).toBeInTheDocument();
  });

  // Testzweck: Fehler und leere Ergebnisse werden inline verständlich dargestellt, ohne
  // transport- oder serverinterne Details in das Eigenschaften-Panel zu leaken.
  it('zeigt Lade-, Fehler- und Leerzustände', async () => {
    searchMock.mockReturnValue({ data: undefined, isPending: true, isFetching: true, error: null });
    const user = userEvent.setup();
    renderPicker();
    await user.type(screen.getByRole('searchbox', { name: 'Benutzer suchen' }), 'an');
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Suche läuft'), { timeout: 500 });

    cleanup();
    searchMock.mockReturnValue({ data: undefined, isPending: false, isFetching: false, error: new Error('intern') });
    const errorUser = userEvent.setup();
    renderPicker();
    await errorUser.type(screen.getByRole('searchbox', { name: 'Benutzer suchen' }), 'an');
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('nicht verfügbar'), { timeout: 500 });
  });
});
