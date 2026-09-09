import { useMemo, useState } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

import { FlowzerClient } from '@flowzer/sdk';
import {
  FlowzerProvider,
  InstanceStatusController,
  UserTaskListController,
  UserTaskWorkspaceController,
} from '@flowzer/react';

export interface HostAuthentication {
  getFlowzerAccessToken: () => string | Promise<string>;
  /** Opake, nicht geheime Kennung, die bei jeder Anmeldung wechselt. */
  sessionScope: string;
}

export interface AppProps {
  authentication: HostAuthentication;
}

/**
 * Absichtlich schmucklose Referenz: Sie beweist nur den öffentlichen Paketvertrag.
 * Produktive Hosts ersetzen alle HTML-Fragmente und den Formularplatzhalter selbst.
 */
export function App({ authentication }: AppProps) {
  const [selectedTaskId, setSelectedTaskId] = useState<string>();
  const queryClient = useMemo(() => new QueryClient(), []);
  const flowzer = useMemo(() => new FlowzerClient({
    baseUrl: '/flowzer-api',
    auth: { kind: 'bearer', getAccessToken: authentication.getFlowzerAccessToken },
  }), [authentication.getFlowzerAccessToken]);

  return (
    <QueryClientProvider client={queryClient}>
      <FlowzerProvider
        client={flowzer}
        cacheNamespace="embedded-flowzer"
        sessionScope={authentication.sessionScope}
      >
        <UserTaskListController>
          {({ data: tasks, isPending, error }) => (
            <section aria-label="Aufgaben">
              {isPending && <p>Lädt …</p>}
              {error && <p role="alert">{error.message}</p>}
              {tasks.map((task) => (
                <button key={task.id} type="button" onClick={() => setSelectedTaskId(task.id)}>
                  {task.name}
                </button>
              ))}
            </section>
          )}
        </UserTaskListController>

        {selectedTaskId && (
          <UserTaskWorkspaceController userTaskId={selectedTaskId}>
            {({ task, form, draft, canWork, actions, error }) => (
              <section aria-label="Aufgabenarbeitsbereich">
                <h2>{task?.name ?? 'Aufgabe'}</h2>
                {error && <p role="alert">{error.message}</p>}
                {!canWork && task?.workState?.canClaim && (
                  <button
                    type="button"
                    onClick={() => actions.claim.mutate({
                      expectedRevision: task.workState?.revision ?? 0,
                    })}
                  >
                    Übernehmen
                  </button>
                )}
                {canWork && form && draft && (
                  <pre aria-label="Formularadapter-Eingang">
                    {JSON.stringify({ form, draft }, null, 2)}
                  </pre>
                )}
                {task?.processInstanceId && (
                  <InstanceStatusController instanceId={task.processInstanceId}>
                    {({ data: instance }) => <p>Vorgang: {instance?.state ?? 'unbekannt'}</p>}
                  </InstanceStatusController>
                )}
              </section>
            )}
          </UserTaskWorkspaceController>
        )}
      </FlowzerProvider>
    </QueryClientProvider>
  );
}
