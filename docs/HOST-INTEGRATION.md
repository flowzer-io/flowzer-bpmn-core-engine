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

## Atomare Bindung weiterer Aufgabenaktionen

Auch Claim, Release, privater Entwurfsabruf/-speichern/-löschen, Linkausgabe und
feldgebundene Directorysuche/-auflösung akzeptieren die optionalen Querybedingungen
`expectedProcessInstanceId`, `expectedDefinitionId` und
`requireAssignedToCurrentUser`. Sie **beschränken** bestehende Rechte; sie liefern
weder Identität noch Berechtigung. Der Host leitet Instanz/Version aus seiner
serverseitig registrierten Vorgangsbindung ab und sendet die persönliche
Übernahmebedingung fest für Daten-/Link-/Freigabeaktionen. Claim verwendet sie
bewusst nicht: Er übernimmt die bislang freie Aufgabe atomar.

Flowzer prüft aktuelle Identität, aktive Task-/Instanz-/Tokenbindung und diese
Bedingungen im selben vorhandenen Tasklock und derselben Storage-Transaktion wie
die eigentliche Aktion. Bei Dateiablage serialisiert zusätzlich die bestehende
Engine-Sperre; PostgreSQL verwendet den bestehenden Lifecyclelock pro Aufgabe.
Die Read-only-Projektion von Directorywerten bleibt bis zur fertigen Antwort in
dieser geschützten Sicht. Es gibt keinen neuen Taskcache, keine Hostsession und
keine zusätzliche Persistenz. Bestehende Konsolenaufrufe ohne Bedingungen bleiben
kompatibel; der Host darf fehlende Bedingungen niemals als Fallback verwenden.

Rechteentzug oder verlorener Claim bleibt `404`, auch für Operatoren mit
`requireAssignedToCurrentUser=true`. Ein Instanz-/Versionskonflikt einer sonst
berechtigten Aufgabe ist `409 user_task.binding_conflict` ohne Zielkennungen.
Ein nach Freigabe erneut gültiges Kandidatenrecht ist kein persönliches
Bearbeitungsrecht. Linkeinlösung bindet weiterhin die gespeicherte Taskrevision
und Definitionsversion; Freigabe oder Migration nach Ausgabe entwerten den Grant.
Die Einlösefrist begrenzt weiterhin nur den Einstieg, nicht die Bearbeitung.

Die echten HTTP-Regressionen reproduzierten vor dem Fix **17/23 rot**, anschließend
besteht der kombinierte fokussierte Alt-/Neuvertrag **88/88**, ohne Skips.
Zusätzliche PostgreSQL-Zwei-Host-Tests prüfen jeweils 20 Migration-/Claim-,
Migration-/Release- und Migration-/Save-Rennen: kein V1-Auftrag darf unter V2
Audit oder Entwurf erzeugen. Dieser echte Mehrprozessnachweis läuft getrennt in
der verbindlichen PostgreSQL-CI; ein hermetischer Grünlauf ersetzt ihn nicht.
Der lokale API-Hermetiklauf besteht **1466/1466**, Engine **372/372**, echter
OpenAPI-Vertrag **16/16** und SDK **33/33** samt Typprüfung/Build, ohne Skips.
Die generierten Queryparameter stammen aus dem tatsächlich gemounteten Swagger;
kein manuell parallel gepflegtes Schema. Das ist keine Demo-/HTTPS-Abnahme.

Der erste PostgreSQL-CI-Lauf des Nichtabschluss-Slices (`a1c1132`) ist mit
**1/1605 Fehlern, 1604 bestanden, 0 Skips** gescheitert: Die neue Save-Rennfixture
sendete `answer` an das leere Standardformular und erhielt folgerichtig 422 für
ein nicht deklariertes Feld. Beide Rennversionen erhalten jetzt ausdrücklich
dasselbe beschreibbare Feld; nur der geänderte Form-Key verwirft V1-Entwürfe.
Ein zusätzlicher hermetischer Fixturetest reproduziert diesen Fehler tatsächlich
rot. Die 200/409-, Audit- und Entwurfsassertions bleiben unverändert. Erst der
erneute echte PostgreSQL-CI-Lauf des korrigierten SHA ist der Konkurrenznachweis.

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
