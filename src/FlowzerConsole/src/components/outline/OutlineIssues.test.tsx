import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import type { OutlineIssue } from '@/lib/outline/model';

import { OutlineIssues } from './OutlineIssues';

const issue: OutlineIssue = {
  level: 'blocker',
  message: 'Element kann nicht in der Gliederung bearbeitet werden.',
  elementId: 'Task_Review',
};

describe('OutlineIssues', () => {
  // Testzweck: Auch lokale Gliederungsbefunde sollen das technische Element als
  // barrierearme Aktion anbieten, statt den Nutzer auf eine ID-Suche zu verweisen.
  it('meldet ein Element als anwählbaren Befund', async () => {
    const user = userEvent.setup();
    const onSelectIssue = vi.fn();
    render(<OutlineIssues issues={[issue]} outlineShown onSelectIssue={onSelectIssue} />);

    await user.click(screen.getByRole('button', { name: /Task_Review/ }));
    expect(onSelectIssue).toHaveBeenCalledWith(issue);
  });

  // Testzweck: Globale Hinweise ohne Element-ID bleiben reine Information und
  // erzeugen kein falsches Sprungziel.
  it('zeigt globale Hinweise ohne Sprungaktion', () => {
    render(<OutlineIssues issues={[{ ...issue, elementId: undefined, level: 'hinweis' }]} outlineShown />);

    expect(screen.getByText(issue.message)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Task_Review/ })).not.toBeInTheDocument();
  });
});
