# Hostneutrale Einbettung

## Architekturgrenze

Flowzer ist ein eigenständig installierbares Open-Source-Produkt und kennt keine
konkrete konsumierende Anwendung. Produktcode, Konfiguration, Datenmodell und
Laufzeitnamen enthalten deshalb keine Host-spezifischen Abhängigkeiten.

Die Grenze ist eindeutig:

- Flowzer besitzt Workflowdefinitionen, gebundene Formularstände, Prozessinstanzen,
  Human Tasks, Entwürfe und seine Audit-/Betriebsdaten.
- Der Host besitzt seine Fachobjekte, Navigation, Darstellung und fachlichen
  Berechtigungen außerhalb von Flowzer.
- Integration erfolgt ausschließlich über versionierte HTTP-Verträge und die
  optionalen Pakete `@flowzer/sdk` und `@flowzer/react`.
- Es gibt keine gemeinsamen Tabellen und keinen direkten Datenbankzugriff zwischen
  Flowzer und einem Host.

## Pakete

`@flowzer/sdk` ist ein zustandsloser TypeScript-Client ohne UI-Abhängigkeit. Er deckt
Aufgabenliste und Task-Deep-Link, gebundene Formulare, private Entwürfe,
Claim/Release/Assign/Delegate, Directory-Suchen, idempotenten Abschluss und
Vorgangsstatus ab. DTOs werden aus dem eingecheckten OpenAPI-Snapshot erzeugt.

`@flowzer/react` ergänzt optionale Hooks und Render-Prop-Controller. Es enthält weder
CSS noch Form.io und schreibt dem Host kein Designsystem vor. Der neutrale
`FlowzerTaskFormAdapterProps`-Vertrag übergibt dem Host Formular, Entwurf,
Directory-Suche und Aktionen, ohne den veröffentlichten Serververtrag zu erweitern.

Eine unabhängig kompilierte Minimalintegration liegt unter
[`examples/react-host-embedding`](../examples/react-host-embedding/README.md).

Die Flowzer-Konsole verwendet dieselben Pakete als produktinterner Referenzkonsument.
Ihre BFF-, Form.io- und Development-Adapter sind jedoch kein Bestandteil des
öffentlichen Pakets und keine Vorgabe für externe Hosts. Details:
[Console-Paketintegration](CONSOLE-TASK-PACKAGE-INTEGRATION.md).

## Authentisierung

Interaktive Requests bleiben immer an den tatsächlichen Benutzer gebunden:

1. Bei direktem API-Zugriff liefert der Host dem SDK pro Request ein kurzlebiges,
   ausschließlich für Flowzer bestimmtes Bearer-Token.
2. Ein erforderlicher Token Exchange findet ausschließlich im vertrauenswürdigen
   Backend/BFF des Hosts und beim Identity-Provider statt, nie im Flowzer-SDK oder
   über ein Browser-Client-Secret.
3. Alternativ nutzt eine gleich-originige Einbettung den Flowzer-BFF beziehungsweise
   einen vertrauenswürdigen Same-Origin-Proxy mit Cookie und CSRF-Token.
4. Servicekonto, Operator-Token und frei übergebene Benutzer-Header sind kein Ersatz
   für einen benutzergebundenen Kontext.

Flowzer prüft Audience, Issuer, Subject, Rollen und Objektberechtigungen selbst. Ein
Host darf UI-Aktionen ausblenden, erweitert damit aber niemals die Serverrechte.

## Cache- und Mutationssicherheit

Der React-Provider verlangt zwei nicht geheime Werte:

- `cacheNamespace` identifiziert eine Flowzer-Installation.
- `sessionScope` wechselt bei jeder Host-Anmeldung.

Token, E-Mail oder Anzeigename gehören nicht in Query-Keys. Beim Logout entfernt
`clearFlowzerScope` die alte Sitzung. Ein Rechteentzug blendet bereits geladene
Formular-/Entwurfsdaten sofort aus und entfernt sie aus diesem Cache.

Task-Mutationen werden nicht automatisch wiederholt. Ein Abschluss benötigt die
aktuelle Task-Revision sowie einen vom Host erzeugten, über bewusste Wiederholungen
stabilen Idempotenzschlüssel. `409`, `422` und weitere Problem Details bleiben
maschinenlesbar; lokale Formulardaten werden bei Hintergrund-Refetches nicht ersetzt.

## Atomar gebundener Aufgabenabschluss

Ein Host kann die ursprünglich angezeigte Aufgabe zusätzlich mit
`expectedUserTaskId` und `expectedDefinitionId` binden. Die zweite Kennung ist die
unveränderliche Definition-Version (GUID), nicht die Katalogkennung. Ein späterer
Instanzumzug kann Task, Token, Knoten und Claimrevision erhalten; deshalb genügt
ein vorangehender Aufgabenabruf nicht als Abschlussprüfung.

`requireAssignedToCurrentUser: true` verlangt für eine **neue** Entscheidung die
tatsächliche persönliche Zuweisung. Kandidaten- und Betriebsrechte ersetzen sie
nicht. Ein solcher Host übernimmt die Aufgabe vorher über den bestehenden
revisionsgebundenen Claim. Die bestehenden allgemeinen Konsumenten bleiben ohne
diese optionalen Bedingungen kompatibel.

Beide Abschlussrouten prüfen die Bedingungen unter der vorhandenen Instanz- und
Aufgabensperre, bevor sie Formularvalidierung oder Effekte ausführen. Eine fremde
Taskkennung oder fehlende persönliche Zuweisung liefert wie andere nicht erlaubte
Aufgaben `404`; ein Versionswechsel einer autorisierten Aufgabe liefert
`409 user_task.binding_conflict`, ohne interne Zielkennungen. Der Host darf diesen
Konflikt nicht durch stilles Ersetzen der angezeigten Version umgehen.

Alle Bedingungen gehören zum ursprünglichen Idempotenzinhalt. Ein erfolgreicher
persönlicher Replay wird **vor** den aktuellen Task-/Claim-/Versionsprüfungen
beantwortet, auch nach Abschluss oder Umzug. Bei unklarem Ausgang bleiben daher
Schlüssel, Bindung, Entscheidung und Eingaben unverändert. Es entstehen keine
neuen TT-Daten, Entwurfstabellen oder Link-Secrets für Wiederholungen.

## Bewusste Grenzen des ersten Pakets

- keine fertigen sichtbaren Komponenten oder Form.io-Bündelung
- keine Vorgangskommentare oder vollständige Ereigniszeitleiste
- keine externe Benachrichtigungszustellung
- keine automatische Rechtevertretung
- keine Paketveröffentlichung aus diesem Slice
- keine allgemeine Produktionsfreigabe ohne reale Identity-, HTTPS- und
  Einbettungsabnahme

## Persönlicher Read-only-Einstieg (API-Slice)

Der einmalige, fünf Minuten einlösbare Formulareinstieg ist unter
[FORM-EMBED-LINKS.md](FORM-EMBED-LINKS.md) dokumentiert. Er verleiht keinerlei
Mutationsrecht. Der vorhandene Renderer und der gebundene Nachrichtenkanal sind
implementiert und synthetisch geprüft; die tatsächliche TT-Anbindung sowie reale
HTTPS-/Identity-Abnahme bleiben offen. Das Installations-Opt-in bleibt geschlossen.

Ein Host teilt keine Realm-Gruppen allein durch Token-Claims mit. Eine auf einen
Teilbaum begrenzte Installation kann `IdentityDirectory__RootGroupId` verwenden
([Betriebsvertrag](OPERATIONS.md#optionaler-gruppen-scope-einer-installation));
Flowzer prüft dann aktuelle stabile Directory-Mitgliedschaften zusätzlich zu Rollen
und Objektberechtigungen. Der Host erteilt dadurch keine eigenen Gruppenrechte.
