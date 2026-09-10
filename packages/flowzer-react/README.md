# `@flowzer/react`

Optionale, darstellungsfreie React-Controller und Hooks für `@flowzer/sdk`. Das Paket
kennt weder die Flowzer Console noch eine konkrete konsumierende Anwendung. Es bringt
kein CSS und keinen Formularrenderer mit.

## Einrichtung

Der Host besitzt `QueryClient`, Authentisierung und sichtbare Komponenten:

```tsx
import { FlowzerClient } from '@flowzer/sdk';
import { FlowzerProvider, UserTaskListController } from '@flowzer/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';

const flowzer = new FlowzerClient({
  baseUrl: '/flowzer-api',
  auth: { kind: 'bearer', getAccessToken: () => hostAuth.flowzerToken() },
});
const queries = new QueryClient();

export function FlowzerTasks({ sessionScope }: { sessionScope: string }) {
  return (
    <QueryClientProvider client={queries}>
      <FlowzerProvider
        client={flowzer}
        cacheNamespace="customer-flowzer"
        sessionScope={sessionScope}
      >
        <UserTaskListController>
          {({ data, isPending, error }) => hostRendersTasks({ data, isPending, error })}
        </UserTaskListController>
      </FlowzerProvider>
    </QueryClientProvider>
  );
}
```

`cacheNamespace` unterscheidet Installationen. `sessionScope` muss bei jeder Anmeldung
wechseln, darf aber selbst kein Token, keine E-Mail und kein anderes Geheimnis enthalten.
Beim Logout entfernt `clearFlowzerScope(queryClient, namespace, sessionScope)` genau
diesen Sitzungsbestand.

## Öffentliche Bausteine

- `useUserTasks`, `useUserTask` und `UserTaskListController`
- `useUserTaskWorkspace` und `UserTaskWorkspaceController`
- `useUserTaskActions` für Claim/Release/Assign/Delegate, Draft und Abschluss
- `useInstanceStatus` und `InstanceStatusController`
- `useFormSections`, `useFormSectionDraft` und `useFormSectionActions` sowie die
  darstellungsfreien `FormSectionListController`/`FormSectionEditorController`
  für modellierungsberechtigte Abschnittsbibliotheken
- `useTaskFormData` für lokale, durch Refetches nicht überschriebene Eingaben
- `FlowzerTaskFormAdapterProps` als neutraler Formularadaptervertrag
- `flowzerQueryKeys` und `clearFlowzerScope` für kontrollierte Cache-Integration

Der Arbeitsbereich fragt Formular und privaten Entwurf erst bei serverseitigem
`workState.canWork` ab. Wird dieses Recht entzogen, gibt er bereits geladene Inhalte
nicht weiter und entfernt sie aus seinem Sitzungscache.

Mutationen setzen `retry: false`, auch wenn der Host-`QueryClient` global etwas anderes
vorgibt. Der Host entscheidet über einen erneuten Versuch und bewahrt dafür denselben
Idempotenzschlüssel. Fehler bleiben als `FlowzerApiError` aus dem SDK maschinenlesbar.

Abschnittsversionen sind stets konkrete serverseitig veröffentlichte Fassungen. Das
React-Paket erzeugt keine freie `latest`-Auswahl, rendert kein Schema und kennt keine
konkrete konsumierende Fachanwendung.

## Formularadapter

Das Paket interpretiert bewusst kein Form.io-Schema. Ein Host kann seinen Renderer über
`FlowzerTaskFormAdapterProps` anbinden. Directory-Suchen erhalten ausschließlich Task-ID,
Feldschlüssel und Suchoptionen; die zulässige Auswahl leitet der Flowzer-Server aus dem
veröffentlichten Formular ab.

Der Workspace stellt zusätzlich `resolveSubjects(fieldKey, subjects)` bereit. Lifecycle-
Aktionen besitzen `resolveAssignees(action, subjects)`. Beide Methoden verwenden die
serverseitig gebundene historische Batch-Auflösung und geben den aktuellen Aktiv-/
Auswahlstatus zurück; sie öffnen weder eine globale Suche noch eine Historienliste.

Eine kleine, unabhängig kompilierte Referenz steht unter
[`examples/react-host-embedding`](../../examples/react-host-embedding/README.md).

## Entwicklung

Zuerst das SDK bauen, danach dieses Paket prüfen:

```bash
npm --prefix ../flowzer-sdk run build
npm ci
npm run check
npm pack --dry-run
npm audit --audit-level=high
```

Das Paket folgt Semantic Versioning. Vor `1.0.0` können kleinere Versionen bewusste
Vertragsänderungen enthalten; React, TanStack Query und `@flowzer/sdk` bleiben
Peer-Abhängigkeiten und werden nicht doppelt in Host-Anwendungen gebündelt.
