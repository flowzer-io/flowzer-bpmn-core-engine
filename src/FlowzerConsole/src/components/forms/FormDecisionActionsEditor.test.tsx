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
