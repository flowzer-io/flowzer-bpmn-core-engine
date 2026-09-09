# Flowzer-Konsole auf öffentlichen Human-Task-Paketen

## Architekturgrenze

Die Flowzer-Konsole ist der erste produktinterne Konsument von `@flowzer/sdk` und
`@flowzer/react`. Aufgabenliste, Task-Detail, Formular, privater Entwurf,
Lifecycle-Aktionen, gebundene Verzeichnissuche und Abschluss laufen damit über
denselben öffentlichen Vertrag, den auch unabhängige Integrationen verwenden.

Flowzer kennt weiterhin **keine konkrete konsumierende Fachanwendung**. Das
Repository enthält weder deren Namen noch Navigation, Fachobjekte, Tabellen oder
Authentisierungslogik. Externe Hosts integrieren Flowzer ausschließlich von außen
über HTTP, SDK und optionale React-Bausteine.

## Console-spezifische Adapter

Browser- und BFF-Details bleiben bewusst außerhalb der öffentlichen Pakete:

- `createConsoleFlowzerClient` bindet den Flowzer-BFF mit Same-Origin-Cookies und
  CSRF-Callback an den zustandslosen SDK-Client.
- Der Development-Benutzerheader existiert nur im Console-Adapter und ist weder
  SDK-Option noch Integrationsvertrag.
- Ein zufälliger `sessionScope` trennt React-Query-Daten pro Anmeldung. Er enthält
  keine Subject-ID, E-Mail, Token- oder Cookieinformation.
- Logout, `401` und ein Kontowechsel entfernen nur den abgelaufenen öffentlichen
  Scope. Ein Refresh desselben Subjects behält den Scope.

## Form.io und Directory

Form.io bleibt eine Darstellungsentscheidung der Konsole. Ein gebundener Adapter
übergibt dem verschachtelten React-Root ausschließlich den Feldschlüssel, Suchtext,
Identitätsart und ein Abbruchsignal. Task-ID und API-Client bleiben in der äußeren
Console-Komposition. Der Server leitet die erlaubte Auswahl weiterhin aus der
veröffentlichten Formularversion ab.

Die Bearbeitersuche wird entsprechend an die konkrete Aktion `assign` oder
`delegate` gebunden. Unbekannte künftige Subject-Arten werden an der Console-Grenze
nicht als Benutzer oder Gruppe dargestellt.

## Abschluss und Konflikte

Die Konsole erzeugt pro bewusstem Abschlussversuch einen Idempotenzschlüssel:

- Bei einem Netzwerkfehler mit unklarem Ausgang bleibt der Schlüssel für die
  bewusste Wiederholung erhalten.
- Bei einer eindeutigen HTTP-Ablehnung oder nach Erfolg wird er verworfen.
- Task-Revision, Token und Flow-Node stammen aus dem geladenen Detail-Workspace;
  unvollständige Laufzeitreferenzen werden nicht gesendet.
- Claim, Release, Assign und Delegate laden auch nach einem Konflikt Taskliste und
  Detail neu, ohne lokale Formular- oder Dialogeingaben zurückzusetzen.

Der frühere parallele Console-Transport für Human Tasks wurde entfernt. Andere
Console-Ressourcen verwenden vorerst weiter die bestehende API-Schicht; ihre
Migration ist kein Teil dieses Slices.

## Build und Prüfung

Console-CI und `Dockerfile.console` bauen die gelockten lokalen SDK-/React-Pakete
vor der Konsole. `node_modules` und lokale `dist`-Artefakte sind aus dem Docker-
Kontext ausgeschlossen. Damit beweist der Containerbuild, dass keine zufälligen
Host-Artefakte benötigt werden.

```bash
npm --prefix packages/flowzer-sdk run check
npm --prefix packages/flowzer-react run check
npm --prefix src/FlowzerConsole run typecheck
npm --prefix src/FlowzerConsole run lint
npm --prefix src/FlowzerConsole run test
npm --prefix src/FlowzerConsole run build
python3 scripts/ci/check_host_neutrality.py
docker build -f Dockerfile.console -t flowzer-console:package-smoke .
```

Eine reale externe HTTPS-/Identity-Integration bleibt eine gesonderte Abnahme; sie
ändert nicht die Richtung der Abhängigkeit.
