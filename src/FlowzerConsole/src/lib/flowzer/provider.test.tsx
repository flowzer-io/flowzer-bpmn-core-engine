import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, waitFor } from '@testing-library/react';
import { flowzerQueryKeys, useFlowzer } from '@flowzer/react';
import { describe, expect, it } from 'vitest';

import { useSession } from '@/stores/session';

import { CONSOLE_CACHE_NAMESPACE, ConsoleFlowzerProvider } from './provider';

function ScopeProbe() {
  const { cacheNamespace, sessionScope } = useFlowzer();
  return <output>{`${cacheNamespace}:${sessionScope}`}</output>;
}

describe('ConsoleFlowzerProvider', () => {
  // Testzweck: Die Console reicht den veröffentlichten Hooks nur den zufälligen
  // Sitzungsscope weiter, niemals die Benutzer-ID oder andere BFF-Identitätsdaten.
  it('stellt den öffentlichen Provider mit opakem Sitzungsscope bereit', () => {
    useSession.setState({
      status: 'signed-in',
      user: { id: 'subject-42', name: 'Ada', initials: 'A', capabilities: new Set(['access']) },
      sessionScope: 'random-session-scope',
    });
    const queryClient = new QueryClient();
    const screen = render(
      <QueryClientProvider client={queryClient}>
        <ConsoleFlowzerProvider><ScopeProbe /></ConsoleFlowzerProvider>
      </QueryClientProvider>,
    );

    expect(screen.getByText('flowzer-console:random-session-scope')).toBeInTheDocument();
    expect(screen.queryByText(/subject-42/)).not.toBeInTheDocument();
    screen.unmount();
  });

  // Testzweck: Signout und 401 entfernen die alten öffentlichen Task-/Instanzdaten
  // aus dem gemeinsamen QueryClient, ohne ungescopte Console-Queries zu löschen.
  it('entfernt beim Sitzungsende nur den alten öffentlichen Paketcache', async () => {
    useSession.setState({
      status: 'signed-in',
      user: { id: 'subject-42', name: 'Ada', initials: 'A', capabilities: new Set(['access']) },
      sessionScope: 'old-scope',
    });
    const queryClient = new QueryClient();
    const publicKey = flowzerQueryKeys.userTasks(CONSOLE_CACHE_NAMESPACE, 'old-scope');
    const consoleKey = ['definitions'];
    queryClient.setQueryData(publicKey, ['private-task']);
    queryClient.setQueryData(consoleKey, ['console-data']);

    const screen = render(
      <QueryClientProvider client={queryClient}>
        <ConsoleFlowzerProvider />
      </QueryClientProvider>,
    );

    useSession.getState().endSessionForUnauthorized();

    await waitFor(() => expect(queryClient.getQueryData(publicKey)).toBeUndefined());
    expect(queryClient.getQueryData(consoleKey)).toEqual(['console-data']);
    screen.unmount();
  });
});
