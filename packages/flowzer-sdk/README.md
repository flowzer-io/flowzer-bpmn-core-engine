# `@flowzer/sdk`

Hostneutraler TypeScript-Client für die versionierte Flowzer-HTTP-API. Das Paket
kennt keine konsumierende Fachanwendung, speichert keine Sitzung und enthält keine
React-Abhängigkeit.

## Verwendung mit Bearer-Token

```ts
import { FlowzerClient } from '@flowzer/sdk';

const flowzer = new FlowzerClient({
  baseUrl: 'https://flowzer.example/api',
  auth: {
    kind: 'bearer',
    // Token kurz vor jedem Request aus dem Host-Kontext beziehen.
    getAccessToken: () => session.getFlowzerAccessToken(),
  },
});

const tasks = await flowzer.userTasks.list();
const taskSummary = tasks[0];
if (!taskSummary) throw new Error('Keine offene Aufgabe');

const task = await flowzer.userTasks.get(taskSummary.id);
const form = await flowzer.userTasks.getForm(task.id);

// Die zulässigen Treffer leitet Flowzer aus dem gebundenen Formularfeld ab.
const representatives = await flowzer.userTasks.searchFormSubjects(
  task.id,
  'representative',
  { query: 'Alex', kind: 'user' },
);

const flowNodeId = task.token.currentFlowNodeId;
const tokenId = task.token.id;
const taskRevision = task.workState?.revision;
if (!flowNodeId || !tokenId || taskRevision === undefined) {
  throw new Error('Unvollständiger Aufgabenvertrag');
}

await flowzer.userTasks.complete({
  flowNodeId,
  tokenId,
  processInstanceId: task.processInstanceId,
  expectedTaskRevision: taskRevision,
  actionId: 'approve',
  data: { comment: 'Geprüft' },
}, { idempotencyKey: crypto.randomUUID() });
```

Token Exchange ist eine Aufgabe der installierenden Host-Anwendung und ihres
Identity-Providers. Das SDK akzeptiert nur einen Token-Callback; es fordert selbst
keine beliebigen Tokens an und sendet keine Benutzer-Header.

## Cookiegebundene Sitzung

```ts
const flowzer = new FlowzerClient({
  baseUrl: '/api',
  auth: {
    kind: 'cookie',
    getCsrfToken: () => hostCsrfCache.get(),
  },
  onUnauthorized: () => session.expire(),
});
```

Bei schreibenden Cookie-Aufrufen ist der CSRF-Callback verpflichtend. Cookie- und
Bearer-Modus sind diskriminiert; das SDK mischt die Verfahren nicht. Der Cookie-Modus
ist für einen gleich-originigen Flowzer-Zugriff beziehungsweise einen vertrauenswürdigen
Host-Proxy gedacht. Direkte Cross-Origin-Integrationen verwenden ein auf Flowzer
begrenztes Bearer-Token.

## Verträge und Fehler

- Öffentliche DTO-Typen werden aus `docs/openapi.json` generiert.
- `FlowzerApiError` bewahrt HTTP-Status, Problem Details, `traceId` und feldbezogene
  Codes in `fieldErrors`.
- `AbortSignal` wird unverändert an `fetch` weitergereicht.
- Der Host vergibt Idempotenzschlüssel ausdrücklich. Ungültige Schlüssel werden vor
  dem Request abgelehnt; das SDK erzeugt nie still einen Ersatz und führt Mutationen
  nicht automatisch erneut aus. Der Aufgabenabschluss verlangt im SDK zusätzlich die
  geladene Task-Revision.
- `SubjectRef` bleibt eine stabile Benutzer-/Gruppenreferenz. Anzeigenamen sind kein
  Identitätsschlüssel.
- Formular- und Bearbeiter-Suchen sind immer an Aufgabe, Feld beziehungsweise Aktion
  gebunden. Der Host kann die serverseitige Auswahlpolicy nicht erweitern.
- `userTasks.resolveFormSubjects(...)` und `userTasks.resolveAssignees(...)` lösen kleine
  Mengen bereits gespeicherter Referenzen im selben gebundenen Kontext auf. `isActive`
  und `isSelectable` sind getrennt; nicht kontextgebundene UUIDs bleiben ohne Treffer.
- `formSections` bietet Modellierungsoberflächen einen revisionsgesicherten Entwurf
  und ausschließlich konkrete veröffentlichte `major.minor`-Versionen. Das SDK kennt
  bewusst keine freie oder implizite `latest`-Auflösung; gebundene Formularstände
  bleiben damit reproduzierbar.

## Kompatibilität

`@flowzer/sdk` folgt Semantic Versioning. Vor `1.0.0` können auch kleinere Versionen
bewusste Vertragsänderungen enthalten. Das SDK ruft ausschließlich den zentral
autorisierten Abschlussweg `/UserTask` auf; der ältere Adapter `/form/result` ist kein
Teil der öffentlichen SDK-Oberfläche. Additive Serverfelder bleiben kompatibel, während
entfernte oder umgedeutete Felder eine neue SDK-Version und den aktualisierten
OpenAPI-Snapshot erfordern.

## Entwicklung

```bash
npm ci
npm run generate:openapi
npm run check
npm audit --audit-level=high
```

Nach einer API-Änderung muss `schema.generated.ts` neu erzeugt werden. Die CI führt
die Generierung erneut aus und lehnt Drift ab.
