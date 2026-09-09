import { FlowzerProvider, clearFlowzerScope } from '@flowzer/react';
import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useMemo, type PropsWithChildren } from 'react';

import { registerPublicPackageScopeCleanup } from '@/lib/flowzer/sessionScope';
import { useSession } from '@/stores/session';

import { createConsoleFlowzerClient } from './client';

/** Konstanter, nicht geheimer Namensraum für die eine Flowzer-Console-Installation. */
export const CONSOLE_CACHE_NAMESPACE = 'flowzer-console';

/**
 * Bindet die veröffentlichten React-Bausteine an den Console-Client. Die BFF-Sitzung
 * selbst bleibt im Session-Store; die Pakete sehen nur einen opaken Zufallsscope.
 */
export function ConsoleFlowzerProvider({ children }: PropsWithChildren) {
  const queryClient = useQueryClient();
  const sessionScope = useSession((state) => state.sessionScope);
  const client = useMemo(() => createConsoleFlowzerClient(), []);

  useEffect(
    () => registerPublicPackageScopeCleanup((expiredScope) => {
      clearFlowzerScope(queryClient, CONSOLE_CACHE_NAMESPACE, expiredScope);
    }),
    [queryClient],
  );

  // Vor erfolgreicher Sitzung darf kein veröffentlichter Hook einen Cache-Key oder
  // Clientkontext erhalten. Die übrige Console kann weiterhin das Sign-in-Gate zeigen.
  if (!sessionScope) return children;

  return (
    <FlowzerProvider
      client={client}
      cacheNamespace={CONSOLE_CACHE_NAMESPACE}
      sessionScope={sessionScope}
    >
      {children}
    </FlowzerProvider>
  );
}
