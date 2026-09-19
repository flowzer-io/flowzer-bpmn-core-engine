import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { DecisionDryRunDialog } from './DecisionDryRunDialog';

const mocks = vi.hoisted(() => ({
  evaluate: vi.fn(),
}));

vi.mock('@/lib/api/queries', () => ({
  useEvaluateDecision: () => ({ mutate: mocks.evaluate, isPending: false }),
}));

const DECISIONS = [
  { decisionId: 'dish', name: 'Gericht' },
  { decisionId: 'season', name: 'Saison' },
];

function open() {
  render(
    <DecisionDryRunDialog
      open
      onOpenChange={vi.fn()}
      decisionDefinitionId="definition-1"
      decisions={DECISIONS}
    />,
  );
}

function setVariables(json: string) {
  fireEvent.change(screen.getByLabelText('Variablen (JSON)'), { target: { value: json } });
}

describe('Trockenlauf einer Entscheidung', () => {
  beforeEach(() => vi.clearAllMocks());

  // Testzweck: Die eingegebenen Variablen gehen als Objekt an die API, und das Ergebnis
  // zeigt Wert, getroffene Regeln und Zwischenergebnisse an.
  it('wertet mit den eingegebenen Variablen aus und zeigt das Ergebnis', async () => {
    mocks.evaluate.mockImplementation((_input, options) =>
      options.onSuccess({
        decisionId: 'dish',
        value: 'Spareribs',
        matchedRules: ['dishRuleSpareribs'],
        requiredResults: {
          season: { decisionId: 'season', value: 'Fall', matchedRules: ['seasonRule1'] },
        },
      }));

    open();
    setVariables('{ "guestCount": 4 }');
    fireEvent.click(screen.getByRole('button', { name: 'Auswerten' }));

    await waitFor(() => expect(mocks.evaluate).toHaveBeenCalledOnce());
    expect(mocks.evaluate.mock.calls[0]![0]).toEqual({
      decisionDefinitionId: 'definition-1',
      decisionId: 'dish',
      variables: { guestCount: 4 },
    });

    expect(await screen.findByText('Ergebnis von dish')).toBeInTheDocument();
    expect(screen.getByText('"Spareribs"')).toBeInTheDocument();
    expect(screen.getByText('dishRuleSpareribs')).toBeInTheDocument();
    expect(screen.getByText('seasonRule1')).toBeInTheDocument();
  });

  // Testzweck: Ungueltiges JSON wird gemeldet, statt die API mit einer kaputten Eingabe zu
  // behelligen — der Fehler steht am Feld und nicht als anonyme Serverantwort.
  it('meldet ungueltiges JSON, ohne die API zu rufen', () => {
    open();
    setVariables('{ guestCount: 4 ');
    fireEvent.click(screen.getByRole('button', { name: 'Auswerten' }));

    expect(mocks.evaluate).not.toHaveBeenCalled();
    expect(screen.getByRole('alert').textContent).toMatch(/kein gültiges JSON/);
  });

  // Testzweck: Auch gueltiges JSON, das kein Objekt ist, waere keine Variablenbelegung.
  it('verlangt ein JSON-Objekt statt eines Arrays', () => {
    open();
    setVariables('[1, 2, 3]');
    fireEvent.click(screen.getByRole('button', { name: 'Auswerten' }));

    expect(mocks.evaluate).not.toHaveBeenCalled();
    expect(screen.getByRole('alert').textContent).toMatch(/JSON-Objekt/);
  });

  // Testzweck: Enthaelt die Datei mehrere Entscheidungen, wird genau die ausgewaehlte
  // ausgewertet und nicht stillschweigend die erste.
  it('wertet die ausgewaehlte Entscheidung aus', async () => {
    mocks.evaluate.mockImplementation(() => {});
    open();

    fireEvent.change(screen.getByLabelText('Entscheidung'), { target: { value: 'season' } });
    setVariables('{}');
    fireEvent.click(screen.getByRole('button', { name: 'Auswerten' }));

    await waitFor(() => expect(mocks.evaluate).toHaveBeenCalledOnce());
    expect(mocks.evaluate.mock.calls[0]![0].decisionId).toBe('season');
  });
});
