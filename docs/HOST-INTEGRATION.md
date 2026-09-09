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

## Bewusste Grenzen des ersten Pakets

- keine fertigen sichtbaren Komponenten oder Form.io-Bündelung
- keine Vorgangskommentare oder vollständige Ereigniszeitleiste
- keine externe Benachrichtigungszustellung
- keine automatische Rechtevertretung
- keine Paketveröffentlichung aus diesem Slice
- keine allgemeine Produktionsfreigabe ohne reale Identity-, HTTPS- und
  Einbettungsabnahme
