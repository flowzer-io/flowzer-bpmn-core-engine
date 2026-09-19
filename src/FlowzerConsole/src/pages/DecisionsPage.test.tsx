import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { DecisionsPage } from './DecisionsPage';

const mocks = vi.hoisted(() => ({
  create: vi.fn(),
  update: vi.fn(),
  remove: vi.fn(),
  can: vi.fn((capability: string) => capability.length > 0),
  detail: {
    decisionDefinitionId: 'definition-1',
    name: 'Gerichtwahl',
    version: 3,
    deployedAt: '2026-09-09T16:00:00Z',
    deployedBy: 'christian',
    decisions: [
      { decisionId: 'dish', name: 'Gericht' },
      { decisionId: 'season', name: 'Saison' },
    ],
    xml: '<definitions />',
  },
}));

vi.mock('@/stores/session', () => ({ useCan: () => mocks.can }));
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
// Der Editor selbst hat seinen eigenen Rauchtest; hier zaehlt nur die Katalogseite.
vi.mock('@/components/decisions/DmnEditor', () => ({
  DmnEditor: () => <div data-testid="dmn-editor" />,
}));
vi.mock('@/lib/api/queries', () => ({
  useDecisions: () => ({
    data: [{ ...mocks.detail, xml: undefined }],
    isPending: false,
    error: null,
    refetch: vi.fn(),
  }),
  useDecision: () => ({ data: mocks.detail, isPending: false, error: null, refetch: vi.fn() }),
  useCreateDecision: () => ({ mutate: mocks.create, isPending: false }),
  useUpdateDecision: () => ({ mutate: mocks.update, isPending: false }),
  useDeleteDecision: () => ({ mutate: mocks.remove, isPending: false }),
  useEvaluateDecision: () => ({ mutate: vi.fn(), isPending: false }),
}));

describe('Seite „Entscheidungen"', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.can.mockReturnValue(true);
  });

  // Testzweck: Die Liste zeigt je Eintrag Name, Version und die enthaltenen Entscheidungen —
  // ohne die decisionIds wuesste niemand, was im Business-Rule-Task einzutragen ist.
  it('zeigt die Eintraege samt enthaltener Entscheidungen', () => {
    render(<DecisionsPage />);

    expect(screen.getAllByText('Gerichtwahl').length).toBeGreaterThan(0);
    expect(screen.getByText('v3')).toBeInTheDocument();
    expect(screen.getByText('Gericht · dish')).toBeInTheDocument();
    expect(screen.getByText('Saison · season')).toBeInTheDocument();
    expect(screen.getByTestId('dmn-editor')).toBeInTheDocument();
  });

  // Testzweck: „Anlegen" schickt die Vorlage als DMN-1.3-XML an die API, nicht eine leere
  // Datei, die dmn-js spaeter ohne Entscheidung oeffnen wuerde.
  it('legt eine Entscheidung aus der Vorlage an', async () => {
    render(<DecisionsPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Neue Entscheidung' }));
    fireEvent.change(screen.getByLabelText('Name der Entscheidung'), {
      target: { value: 'Rabattstufe' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Anlegen' }));

    await waitFor(() => expect(mocks.create).toHaveBeenCalledOnce());
    const payload = mocks.create.mock.calls[0]![0] as { name: string; xml: string };
    expect(payload.name).toBe('Rabattstufe');
    expect(payload.xml).toContain('https://www.omg.org/spec/DMN/20191111/MODEL/');
    expect(payload.xml).toContain('<decision id="Rabattstufe"');
  });

  // Testzweck: Eine hochgeladene DMN-Datei geht unveraendert an die API; der Dateiname ohne
  // Endung dient als Name des Katalogeintrags.
  it('uebernimmt eine hochgeladene DMN-Datei', async () => {
    render(<DecisionsPage />);
    const upload = screen.getByLabelText('DMN-Datei hochladen') as HTMLInputElement;
    const file = new File(['<definitions id="hochgeladen" />'], 'Bonitaet.dmn', { type: 'text/xml' });

    fireEvent.change(upload, { target: { files: [file] } });

    await waitFor(() => expect(mocks.create).toHaveBeenCalledOnce());
    expect(mocks.create.mock.calls[0]![0]).toEqual({
      name: 'Bonitaet',
      xml: '<definitions id="hochgeladen" />',
    });
  });

  // Testzweck: Ohne Modelliererrolle bleibt die Seite lesbar, bietet aber keine Schaltflaeche
  // an, die spaeter an der API scheitern wuerde.
  it('zeigt ohne Modelliererrolle keine schreibenden Schaltflaechen', () => {
    mocks.can.mockImplementation((capability: string) => capability === 'operator');
    render(<DecisionsPage />);

    expect(screen.queryByRole('button', { name: 'Neue Entscheidung' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'DMN hochladen' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Als neue Version speichern' })).not.toBeInTheDocument();
    // Der Trockenlauf aendert nichts und bleibt dem Betrieb erlaubt.
    expect(screen.getByRole('button', { name: 'Trockenlauf' })).toBeInTheDocument();
  });
});
