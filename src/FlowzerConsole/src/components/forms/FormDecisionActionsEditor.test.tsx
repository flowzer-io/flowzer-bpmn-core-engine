import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { FormDecisionActionsEditor } from './FormDecisionActionsEditor';

describe('FormDecisionActionsEditor', () => {
  // Testzweck: Modellierende können eine explizite Aktion anlegen; die UI erzeugt
  // sofort eine stabile ID, eine sichtbare Beschriftung und eine feste Feldbelegung.
  it('legt eine begrenzte deklarative Aktion an', () => {
    const onChange = vi.fn();
    render(<FormDecisionActionsEditor actions={[]} onChange={onChange} />);

    fireEvent.click(screen.getByRole('button', { name: 'Aktion hinzufügen' }));

    expect(onChange).toHaveBeenCalledWith([{
      id: 'action_1',
      label: 'Aktion 1',
      variant: 'secondary',
      set: [{ field: 'decision', value: 'value_1' }],
    }]);
  });
});

// Testzweck: Technische IDs sind zunächst verborgen; sichtbare Knopftexte bleiben editierbar,
// die reine Knopfvorschau führt keine Aufgabe aus und ändert keine bestehenden Zuordnungen.
it('zeigt Abschlussknöpfe verständlich und erweitert technische Einstellungen nur bewusst', () => {
  const onChange = vi.fn();
  render(<FormDecisionActionsEditor actions={[{id:'approve',label:'Genehmigen',variant:'primary',set:[{field:'decision',value:'approved'}]}]} onChange={onChange} />);
  expect(screen.getByRole('heading', {name:'Abschlussknöpfe'})).toBeVisible();
  expect(screen.getByLabelText('Technische ID')).not.toBeVisible();
  fireEvent.click(screen.getByRole('button', {name:'Genehmigen'}));
  expect(onChange).not.toHaveBeenCalled();
  fireEvent.click(screen.getByText('Erweitert: Ergebnis und technische ID'));
  expect(screen.getByLabelText('Technische ID')).toBeVisible();
});

// Testzweck: Nach dem Löschen eines Knopfes darf Hinzufügen keine noch vorhandene ID erneut vergeben.
it('vergibt auch bei Lücken eindeutige technische IDs', () => {
  const onChange = vi.fn();
  render(<FormDecisionActionsEditor actions={[{id:'action_2',label:'Bestehend',variant:'primary',set:[]}]} onChange={onChange} />);
  fireEvent.click(screen.getByRole('button', {name:'Aktion hinzufügen'}));
  expect(onChange.mock.calls[0]![0].map((action: {id:string}) => action.id)).toEqual(['action_2','action_1']);
});
