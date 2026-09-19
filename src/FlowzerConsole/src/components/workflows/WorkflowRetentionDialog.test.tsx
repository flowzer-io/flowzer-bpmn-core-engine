import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { WorkflowRetentionDialog } from './WorkflowRetentionDialog';

const submit = vi.fn();

function open(retentionDays: number | null) {
  render(
    <WorkflowRetentionDialog
      open
      onOpenChange={vi.fn()}
      workflowName="Urlaubsantrag"
      retentionDays={retentionDays}
      onSubmit={submit}
    />,
  );
}

beforeEach(() => vi.clearAllMocks());

describe('Aufbewahrung eines Workflows', () => {
  // Testzweck: „Nie löschen" wird als 0 gespeichert. Die 0 ist der einzige Weg, einen
  // aufbewahrungspflichtigen Workflow von der installationsweiten Frist auszunehmen — sie darf
  // nicht als „keine Angabe" oder gar als „sofort" beim Server ankommen.
  it('speichert „nie löschen" als 0', async () => {
    const user = userEvent.setup();
    open(null);

    await user.click(screen.getByRole('radio', { name: /Nie löschen/ }));
    await user.click(screen.getByRole('button', { name: 'Speichern' }));

    expect(submit).toHaveBeenCalledWith(0);
  });

  // Testzweck: „Einstellung der Installation" wird als null gespeichert und ist damit vom
  // ausdrücklichen „nie löschen" unterscheidbar.
  it('speichert die Rückkehr zur installationsweiten Frist als null', async () => {
    const user = userEvent.setup();
    open(0);

    await user.click(screen.getByRole('radio', { name: /Einstellung der Installation/ }));
    await user.click(screen.getByRole('button', { name: 'Speichern' }));

    expect(submit).toHaveBeenCalledWith(null);
  });

  // Testzweck: Eine eigene Frist wird als Zahl übergeben.
  it('speichert eine eigene Frist als Tageszahl', async () => {
    const user = userEvent.setup();
    open(null);

    await user.click(screen.getByRole('radio', { name: /Eigene Frist/ }));
    const field = screen.getByLabelText('Aufbewahrung in Tagen');
    await user.clear(field);
    await user.type(field, '45');
    await user.click(screen.getByRole('button', { name: 'Speichern' }));

    expect(submit).toHaveBeenCalledWith(45);
  });

  // Testzweck: Eine unbrauchbare Eingabe wird nicht gespeichert, sondern erklärt. Ginge sie
  // durch, lehnte die API sie ab — die Rückmeldung käme dann als Fehlerbanner statt am Feld.
  it('verweigert das Speichern bei einer unbrauchbaren Tageszahl', async () => {
    const user = userEvent.setup();
    open(null);

    await user.click(screen.getByRole('radio', { name: /Eigene Frist/ }));
    const field = screen.getByLabelText('Aufbewahrung in Tagen');
    await user.clear(field);
    await user.type(field, '-3');

    expect(screen.getByRole('button', { name: 'Speichern' })).toBeDisabled();
    expect(screen.getByText(/zwischen 1 und 36500/)).toBeInTheDocument();
  });

  // Testzweck: Ohne Änderung bleibt „Speichern" gesperrt; ein Schreibzugriff ohne Unterschied
  // wäre ein unnötiger Versionswechsel am Katalogeintrag.
  it('sperrt das Speichern, solange sich nichts geändert hat', () => {
    open(30);

    expect(screen.getByRole('button', { name: 'Speichern' })).toBeDisabled();
  });
});
