# Produkt-Roadmap: installierbare Open-Source-Workflow-Plattform

**Freigegeben am:** 8. September 2026. **Basis:** `212705a`.

Dieses Dokument hält die freigegebene Weiterentwicklung und ihren tatsächlichen
Umsetzungsstand fest. Eine Checkbox wird erst nach belegter Implementierung und
Verifikation geschlossen. Vorhandene Grundlagen sind kein Nachweis für ein ganzes
Paket. `docs/ROADMAP.md` verweist auf diesen führenden Plan.

## Aktuelles Arbeitsmandat

Am 8. September 2026 hat Christian die autonome Umsetzung aller Pakete beauftragt.
Terra/Luna dürfen für begrenzte Teilaufgaben unterstützen; vor der finalen
Zusammenführung prüft Astra mit hoher Reasoning-Stufe den Gesamtstand und erkannte
Probleme werden behoben. Bis zur belegten Gesamt-Abnahme bleibt die Arbeit in
Topic-Branches und PRs. Für den danach verifizierten Gesamtstand hat Christian den
Merge nach `main` und das dadurch ausgelöste Deployment ausdrücklich freigegeben;
direkte Zwischenstände werden weiterhin weder nach `main` noch `release` geschrieben
oder produktiv ausgerollt.

## Ziel und Grenzen

- Flowzer bleibt ein eigenständig installierbares Produkt unter MPL-2.0.
- Zunächst eine getrennte Installation mit eigener Datenbank und Identitätsanbindung
  je Kunde; echtes Mehrmandanten-Hosting ist ein späteres eigenes Vorhaben.
- Bestehenden modularen .NET-/React-Aufbau schrittweise verbessern, kein Rewrite.
- Flowzer kennt keine konsumierende Fachanwendung: keine projektspezifischen
  Abhängigkeiten, Modelle, Routen, Konfiguration oder Laufzeitnamen. Flowzer stellt
  ausschließlich generische, versionierte APIs, ein Headless-SDK und optionale
  React-Komponenten bereit. Ein Host integriert diese von außen und bleibt Eigentümer
  seiner Fachobjekte; es gibt keinen gemeinsamen Datenbankzugriff.
- Der Urlaubsantrag ist ein Beispiel für generische Fähigkeiten, keine vollständige
  Personalverwaltung und keine pauschale Übertragung von Rechten auf Vertretungen.
- Produktivkonfiguration, echte Anbieteraufrufe und Deployment sind separate Freigaben.

## Ausgangslage

React-Konsole, OIDC/Rollen, PostgreSQL, Service-Task-Worker, Startformulare,
Workflow-Ordner und eine BPMN-Gliederungsansicht sind bereits vorhanden.
Die älteren Reviews bleiben historische Dokumente; ihre offenen Listen sind nicht
automatisch der aktuelle Bestand. Ein visueller Audit des heutigen Stands ist noch offen.

**Aktiver Slice:** #228 / PR #229 ergänzt die zentrale, versionierte BPMN-Fähigkeitsmatrix und
dieselbe serverseitige Vorab-/Save-/Deploy-Prüfung. Diagramm und Gliederung zeigen
strukturierte Befunde dauerhaft und springen zum betroffenen Element. Die bereits
umgesetzten SDK-/React-Pakete und die Flowzer-Konsole bleiben frei von konkreten Hosts.
Offene Checkboxen bezeichnen noch nicht abgenommene Ergebnisse; weder dieser Slice
noch vorhandene Grundlagen schließen die gesamte Produktabnahme.

**Folgeslice:** #178 / PR #179 ergänzt issuergebundene Antragstellerrechte,
aufgabenbezogene Vorgangsübersichten und reduzierte API-/UI-Projektionen. Die
Aufgaben-Leseprojektion ersetzt noch keine immutable Formularbindung oder
serverseitige Submission-Validierung.

**Formularbindung:** #180 / PR #181 friert alle referenzierten Start-/Aufgabenformulare
beim Deployment ein, einschließlich Subprozessen. Externe Altbestände ohne Snapshot
benötigen ausdrückliche Klärung; es erfolgt keine automatische Migration auf `latest`.
**Formularprüfung:** #182 / PR #183 ergänzt das begrenzte Profil `flowzer.forms/1`,
verbindliche Submission-Prüfung, Read-only-Schutz, Feldfehler und die Migration der
Beispielskripte. Vollständige Form.io-Parität und Bestandsmigration bleiben offen.
Details: [Prüfprofil](FORM-VALIDATION-PROFILE.md).

**Aufgabenidentität:** #184 / PR #185 erhält IDs und gespeicherte Zuweisungen über
parallelen Fortschritt/Timer/Neuladen. Neue Tokens erhalten neue IDs, veraltete Aufgaben
werden entfernt. Claims/Revisionen/Entwürfe und Mehrprozessschutz folgen separat.
Details: [Aufgabenidentität](STABLE-TASK-IDENTITY.md).

**HTTP-Idempotenz:** #186 / PR #187 bindet optionale Schlüssel an Akteur,
Operation, Ziel und kanonischen Inhalt. PostgreSQL-Konkurrenztests belegen genau einen
Start/Abschluss; geänderter Inhalt endet mit 409. Externe Effekte folgen separat.
Details: [HTTP-Idempotenz](IDEMPOTENCY.md).

**BFF:** #188 ist der laufende M0-Slice in einem noch nicht nach `main` gemergten
PR. Die Web-API übernimmt den serverseitigen OIDC-Code-Flow mit vertraulichem
Client, hält Browser-Tokens und Client-Secret aus Storage/Antworten fern und
schützt `HttpOnly`/`Secure` Host-Cookies per `X-Flowzer-CSRF` und Origin-Prüfung.
Der Data-Protection-Keyring wird getrennt persistent gehalten; die externe
Bearer-API bleibt kompatibel.

Die Teil-PRs bleiben bis Merge und Abnahme separat; auch der laufende BFF-Slice
ist kein Produkt- oder vollständiger M0-Abschluss.

**Keycloak-Verzeichnis:** #190 / PR #191 implementiert den opt-in, lesenden und
vollstaendig paginierten Abgleich als atomaren Snapshot. `(Issuer, Subject)`, externe
Gruppen-ID, Hierarchie und Mitgliedschaften erhalten stabile lokale IDs; erfolgreiche
Folgesnapshots deaktivieren fehlende Historie. Teilfehler behalten die vorige Generation,
HTTP-/Gesamtlaufgrenzen verhindern blockierte Importe und eine PostgreSQL-Lease schuetzt
vor parallelen API-Prozessen. Operatorstatus und manueller Start geben keine Identitaeten
oder Secrets aus. Formular-/Task-/Ordnerauswahl ist bewusst der naechste M1-Slice.

**Typisierte Verzeichnissuche:** #192 / PR #193 führt die stabile Referenz
`{ kind: "user" | "group", id: <lokale UUID> }` und einen begrenzten Such-/Prüfkern
ein. Die öffentliche Suche verlangt einen konkreten Workflow und dessen tatsächliche
Modellierungsberechtigung; fremde und unbekannte Kontexte liefern identisch `404`.
Nur aktive Einträge werden neu angeboten. Dies ist noch kein Formularfeld und ändert
die bestehende Freitext-Zuweisung nicht.

**Expliziter Aufgabenmodus:** #194 / PR #195 implementiert den serverseitigen Vertrag für `text`
und `directory` durchgängig von der BPMN-Erweiterung über Parser und Deployment bis zur
persistierten Subscription und Autorisierung. Der Directory-Modus prüft ausschließlich
stabile Benutzer-/Gruppen-IDs, exaktes `(Issuer, Subject)` und aktuelle Mitgliedschaften;
ein Namensfallback ist ausgeschlossen. Legacy-Modelle bleiben Text. Die grafische
Modellerauswahl folgt in #196: Diagramm und Gliederung bieten Freitext oder workflowgebunden
gesuchte Benutzer/Gruppen an und schreiben denselben Vertrag. Das generische Formularfeld und
typisierte Ordnerrechte bleiben davon getrennte M1-Pakete.

**Private Aufgabenentwürfe:** #202 / PR #203 speichert pro offener stabiler Task-ID
und authentifizierter Person einen begrenzten Entwurf. Revisionen verhindern stilles
Überschreiben, die Konsole bewahrt lokale Eingaben bei Refetch/409. PostgreSQL sichert
CAS und Lebenszyklus atomar; die Dateiablage bleibt ein Einzelprozess-Entwicklungsweg.

## M0 – Sicherheit und Verträge (zuerst)

- [x] Einheitlicher, transaktionsgebundener autorisierter Aufgabenabschluss für alle
  HTTP-Routen; fremde Ressourcen liefern `404`, keine freigebenden Fallbacks.
- [x] Ausführende Identität getrennt von untrusted Formulardaten speichern; eingehende
  `UserId` darf den authentifizierten Akteur nicht ersetzen.
- [x] Objektbezogene Instanzrechte und datensparsame Projektionen: Antragsteller sehen
  eigene Vorgänge, Bearbeiter nur benötigten Kontext, Modellierer nicht automatisch
  Personalvorgänge; Betrieb erhält ausdrücklich berechtigte Diagnoseansichten.
- [x] Serverseitige Validierung für Starts und Aufgabenabschlüsse (gemeinsam mit M2).
- [ ] BFF-Anmeldung mit HttpOnly-/Secure-`__Host-`-Cookies, persistentem API-Keyring
  und CSRF-Schutz (`X-Flowzer-CSRF`); keine Browser-Tokens oder Clientsecrets in
  Storage/Antworten, Bearer-Vertrag für externe Konsumenten bleibt bestehen. Umsetzung
  läuft in einem noch ungemergten PR und ist nicht als M0-Abnahme markiert.
- [x] Idempotente Starts und Abschlüsse; derselbe Schlüssel mit abweichendem Inhalt
  erzeugt einen Konflikt statt einen weiteren Vorgang.
- [x] Offene CodeQL-Befunde ohne Suppression beseitigen und konkrete Arbeitsdaten aus
  der polymorphen Storage-Grenze lösen. #222 / PR #223 ist implementiert, lokal getestet
  und einschließlich CodeQL grün; der Merge steht noch aus. Prozessinstanzen und
  Definitionen verbleiben dokumentiert in einer gesonderten Legacy-Grenze.
- [x] Bestandsissues #93–#96 und #98 bereinigt und #176 / PR #177 verknüpft;
  Mobil-PR #153 gegen `main` auf Überschneidungen geprüft, nicht dupliziert.
  Sein Review/Sync/Merge bleibt ein gesonderter Vorgang; hier wurde nichts daraus übernommen.

**Abnahme:** Kein alternativer Abschlussweg umgeht die Rechte; Wiederholungen
erzeugen keine weiteren Starts oder Abschlüsse.

## M1 – Verzeichnis und Auswahl von Benutzern/Gruppen

- [x] Keycloak bleibt führend; nur lesender, minimal berechtigter Servicezugang über
  die Admin REST API. Keine Passwörter oder unnötigen Profilattribute übernehmen.
- [x] Lokales Verzeichnis mit stabiler interner ID, `(Issuer, Subject)`, Anzeigename,
  Benutzerstatus, externer Gruppenkennung, Hierarchie und Mitgliedschaften.
- [x] Erst- und periodischer Abgleich mit Pagination, Retry und sichtbarem Status;
  Generation erst nach vollständigem Erfolg veröffentlichen. Teilfehler dürfen
  keine Massen-Deaktivierung auslösen.
- [x] Gelöschte/deaktivierte Identitäten historisch auflösbar halten, aber aus neuen
  Auswahlen entfernen. #190/#192 bewahren die stabile Historie und aktive Suche; #234 / PR #237
  ergänzt exakte, auf gespeicherte Workflow-, Ordner-, Formular- und Lifecycle-Referenzen
  begrenzte Batch-Auflösungen samt `isActive`/`isSelectable`. Manipulierte IDs bleiben
  ohne Treffer, inaktive Referenzen sichtbar, aber nicht erneut einreichbar.
- [x] Generisches Form.io-Feld: Einzel-/Mehrfachauswahl, nur aktive Benutzer (Default
  ja), erlaubte Benutzer/Gruppen, Untergruppen (Default nein), Gruppen auswählbar
  (Default nein), Suche, Auswahl-Chips, Mindest-/Höchstanzahl.
- [x] Typisierte `SubjectRef` statt Freitext; ausgewählte Gruppen nicht still in
  Benutzer expandieren. Der öffentliche Referenz- und Prüfvertrag ist in #192 umgesetzt;
  #198 ergänzt die Ableitung erlaubter Werte aus der veröffentlichten Formularversion,
  gebundene Start-/Task-Suche und erneute Submission-Prüfung.
- [x] Dieselbe Auswahl in Aufgaben-Zuweisungen und Ordnerberechtigungen verwenden. #196
  integriert den expliziten Task-Modus; #200 ergänzt stabile Ordnerrechte samt bewusst
  erhaltenem Legacy-Freitextmodus.
- [x] **Ergänzung vom 8. September 2026:** Task-Zuweisungen behalten zusätzlich den
  freien Textmodus. Vor der Eingabe explizit „Bekannter Benutzer / bekannte Gruppe“
  oder „Text-String“ wählen. Verzeichniswahl speichert eine typisierte stabile
  Referenz; Text bleibt ein explizit als solcher markierter Wert und wird nicht
  automatisch anhand von Anzeigename/E-Mail einer Verzeichnisidentität zugeordnet.
  Bestehende Textzuweisungen ohne automatische Konvertierung erhalten. Auflösen und
  Berechtigungsprüfung bleiben serverseitig; Verzeichnisbeschränkungen nicht durch
  einen vom Aufrufer gewählten Modus umgehen. Der Modus gehört ins veröffentlichte
  Modell, nicht in den Abschluss-Request. Gruppen bleiben Kandidatengruppen bzw.
  Gruppenreferenzen und werden nicht zum behaupteten individuellen Bearbeiter.
  Modellierer zeigen Modus und eventuelle Mehrdeutigkeit verständlich an.
  Der serverseitige Modus-, Deployment-, Persistenz- und Rechtevertrag ist in #194 / PR #195
  umgesetzt. #196 ergänzt die Auswahl in Diagramm und Gliederung einschließlich stabiler
  XML-Roundtrips, ID-Auflösung, Lade-/Fehlerzuständen und historischen Warn-Chips.
  #234 / PR #237 trennt diese Anzeigeauflösung nun vollständig von der aktiven Suche und bindet
  jeden Treffer an eine bereits gespeicherte Referenz des berechtigten Fachkontexts.

**Abnahme:** Gleichnamige Identitäten bleiben unterscheidbar; manipulierte,
ausgeschlossene oder deaktivierte Werte werden serverseitig abgelehnt.

## M2 – Verlässliche und wiederverwendbare Formulare

- [x] Form.io behalten; versionierte, serverseitig prüfbare Profile 1/2 decken Typen,
  Pflichtwerte, Bereiche, Datumsvergleiche, Auswahlregeln und deklarative Bedingungen ab;
  #208 / PR #209 ergänzt einen gemeinsamen versionierten JSON-Katalog für .NET und Vitest.
  Directory-Snapshot und benannte Berechnungen bleiben darin ausdrücklich
  serverautoritativ statt im Browser nachgebildet zu werden.
- [x] Vorhandene Custom-JavaScript-Regeln inventarisieren und vor erneuter
  Veröffentlichung in unterstützte Regeln oder benannte Serverberechnungen überführen.
  #212 / PR #213 liefert dafür einen modellierergeschützten, datensparsamen Bericht über jede
  veröffentlichte Fassung und den aktuellen Entwurf. Die eigentliche Migration bleibt
  eine bewusste fachliche Bearbeitung; es gibt keinen automatischen Script-Fallback.
- [x] Eingaben, Ausgaben und readonly Kontext trennen; unbekannte Ergebnisse dürfen
  keine geschützten Prozessvariablen überschreiben.
- [x] Unveränderliche veröffentlichte Formularversionen sind beim Deployment gebunden;
  laufende Aufgaben behalten ihre Version. #210 / PR #211 trennt gemeinsame, revisionierte
  Autorenentwürfe, lokale Vorschau und ausdrückliche servervalidierte Veröffentlichung;
  PostgreSQL veröffentlicht Version und Draft-Löschung atomar. Die Dateiablage bleibt
  für diesen Mehrdateivorgang ausdrücklich Entwicklung ohne Rollback.
- [x] Serverseitige private Bearbeitungsentwürfe mit Wiederaufnahme, Größen-/Feldgrenzen
  und optimistischer Revision; Refetch setzt keine ungespeicherten Eingaben zurück.
  #202 / PR #203
  PostgreSQL-CAS und FK-Kaskade sichern Konkurrenz und Aufgabenlebenszyklus, die
  Dateiablage bleibt ausdrücklich auf einen Prozess begrenzt.
- [x] Wiederverwendbare Abschnitte, bedingte Felder, wiederholbare Gruppen, Hilfetexte
  und explizite Entscheidungsaktionen ergänzen.
  Bedingungen waren bereits Teil von Profil 1. #214 / PR #215 ergänzt Profil 3 für begrenzte
  Datagrids und Plaintext-Hilfetexte. #216 / PR #217 ergänzt Profil 4 mit servergebundenen
  Human-Task-Entscheidungsaktionen und einer begrenzten Autorenoberfläche. #230 / PR #231 ergänzt
  die hostneutrale, revisionsgeschützte Abschnittsbibliothek. Formular-Publish bindet
  ausschließlich konkrete Versionen und speichert einen eigenständigen Snapshot samt
  serverseitig erzeugter Bindungsmetadaten; verschachtelte Abschnitte bleiben gesperrt.
- [ ] Anhänge als eigener Slice: Größen-/Typgrenzen, Quarantäne, Prüfung,
  objektbezogene Downloadrechte und Aufbewahrung.
- [ ] Dynamische Kunden-/Projekt-/andere Auswahldaten nur über administrativ
  freigegebene Datenquellen anbinden.
- [ ] Urlaubsbeispiel auf strukturierte Benutzerwahl, Zeitraumprüfung, Freigabe und
  optionalen externen Abgleich umstellen.

## M3 – Human Tasks und generische Einbettung

- [x] Stabile Aufgabenidentität je Token; bestehende Subscriptions aktualisieren
  statt bei jedem Instanzfortschritt neue IDs zu vergeben.
- [x] Claim, Release, Zuweisung und berechtigte Delegation mit Revision, Akteur und
  Begründung; tatsächlicher Bearbeiter ist nicht die Kandidatengruppe. #204 / PR #205
  ist im Topic-Branch umgesetzt; Merge und Abnahme sind noch offen.
- [x] Fälligkeiten, Wiedervorlagen, Erinnerungen und Eskalationen serverseitig;
  dauerhafte, deduplizierte Benachrichtigungen. #206 / PR #207 bindet die unterstützte
  Zeitteilmenge einmalig in UTC, verarbeitet Meilensteine nachholbar und liefert einen
  objektberechtigten In-App-Feed. Externe Zustellung, automatische Vertretung und BPMN-
  Eskalationspropagation bleiben bewusst außerhalb dieses Slices; Merge und Abnahme sind
  noch offen.
- [x] Private Aufgabenentwürfe mit eigener Sichtbarkeitsregel und Revision.
  #202 / PR #203
- [ ] Kommentare und Vorgangshistorie mit eigenen Sichtbarkeitsregeln. #226 / PR #227
  liefert die datensparsame Human-Task-Auditprojektion; #232 / PR #233 ergänzt die getrennte
  objektberechtigte Engine-Ereignisspur. Fachliche Kommentare bleiben offen.
- [x] Headless TypeScript-SDK und optionale React-Komponenten für Aufgabenliste,
  Formular, Aktionen und Status; Host-Adapter für Styling und Auswahlkomponenten.
  #218 / PR #219 implementiert den unabhängigen Client samt generierter OpenAPI-Typen,
  Host-Auth-Callbacks und feld-/aktionsgebundener Verzeichnissuche. #220 / PR #221 ergänzt
  darstellungsfreie React-Controller, sichere Cache-Scopes, Mutations- und
  Formularadapter sowie eine unabhängige Hostfixture.
- [ ] Identischer API-/Formularvertrag in Konsole und beliebigen Host-Anwendungen;
  Flowzer besitzt Prozesse/Aufgaben, der jeweilige Host seine Fachobjekte. Eine
  konkrete Host-Anwendung wird im Flowzer-Produktcode weder benannt noch referenziert.
- [ ] Benutzergebundene Einbettung mit Flowzer-Audience, bei gemeinsamem Keycloak
  über korrekt berechtigten Token Exchange; keine frei übergebenen Benutzerheader
  und kein pauschales Administratorkonto.
- [ ] Technische Konnektoren mit eigenen Rechten, Korrelation und Idempotenz.

**Abnahme:** Dieselbe Aufgabe in Flowzer oder im Host bearbeiten; Rechte, Entwurf
und Abschluss bleiben identisch.

## M4 – Diagramme und Bedienbarkeit

- [ ] Aktueller Browseraudit auf Desktop/Mobil für Finden, Starten, Aufgaben,
  Formularpflege, Modellierung und Störungsbehandlung, mit visueller Evidenz.
- [ ] BPMN bleibt führend; Gliederung und Diagramm verwenden gemeinsame Eigenschaften
  für Formulare, Identitäten, Fristen, Datenzuordnung, Konnektoren und KI.
- [x] Unterstützte Teilmenge zentral deklarieren; #228 / PR #229 trennt modellierbare, parsebare
  und ausführbare Elemente in `flowzer.bpmn-capabilities/1` und erzwingt den Vertrag
  vor Save und Deployment. Verlustbehaftete Gliederungsänderungen bleiben zusätzlich
  durch deren bestehende Teilmengenprüfung blockiert.
- [x] Anwählbare Validierungsfehler für unerreichbare Schritte, ungültige Referenzen,
  Exclusive-Gateway-Bedingungen sowie fehlende User-/Service-/Timer-Konfiguration.
  Diagramm und Gliederung verwenden denselben stabilen 422-Vertrag aus #228 / PR #229.
- [x] Laufzeitdiagramm und echte Ereigniszeitleiste für aktive, abgeschlossene,
  abgebrochene und gestörte Schritte. #232 / PR #233 bindet die unveränderliche Definitionsversion,
  bereinigt das BPMN-Dokument, speichert Engine-Ereignisse append-only und liefert den
  Vertrag über API, SDK, React und responsive Console. Kein scheinexaktes „Schritt x von y“
  bei offenen Verzweigungen.
- [ ] Versionsvergleich, Änderungsübersicht und atomare Veröffentlichung von
  zusammengehörigem BPMN-/Formularstand.
- [ ] Such-/Filterzustände, Tastatur, Fokus, Formularfehler, Ladezustände und mobile
  Dialoge vereinheitlichen.
- [ ] Danach Warte-/Durchlaufzeit- und Störungsdiagramme auf echter Historie mit
  Stichprobengröße und Rechtefiltern.

## M5 – KI-Tasks und Werkzeuge

- [x] KI-Kachel als BPMN-Service-Task mit dokumentierter Flowzer-Erweiterung:
  Verbindung, Modell, versionierte Anweisung, deklarierte Ein-/Ausgaben,
  Ergebnisschema und Limits. #242 / PR #243 implementiert den Autorenvertrag; #252 / PR #253 bindet
  Verbindungsrevision und Modell beim Deployment und gibt ihn mit
  `flowzer.bpmn-capabilities/3` als ausführbar frei. Die historischen Fähigkeitsverträge
  bleiben unverändert. #254 / PR #255 ergänzt den typisierten Autorenvertrag für Werkzeuge; die
  tatsächliche Ausführung und Freigaben bleiben Folgeslices.
- [x] Adapter für OpenAI, OpenAI-kompatible Cloud-/lokale Endpunkte und Anthropic;
  Fähigkeiten prüfen, keine universelle Kompatibilität unterstellen. #244 / PR #245 implementiert
  feste Standardziele, einen expliziten strukturierten Ausgabevertrag und keinen Provider-
  oder Modellfallback; die BPMN-Runtime bleibt bewusst noch getrennt.
- [x] Benutzerdefinierte Cloudziele nach DNS-Auflösung auf öffentliche Adressen begrenzen
  und den Socketaufbau an den geprüften Host, Port und Adressvorrat binden (#248 / PR #249). Lokale
  private Ziele bleiben nur bei ausdrücklichem Installations-Opt-in erreichbar.
- [x] Cloud-Verarbeitung explizit je Installation freigeben, kein stiller Wechsel
  von lokalen Modellen in die Cloud. #240 / PR #241 setzt die installationsweiten Opt-ins und
  die explizite Standortangabe bereits am Verbindungsvertrag durch; #244 / PR #245 erzwingt dieselben
  Grenzen unmittelbar vor jedem internen Provideraufruf erneut.
- [x] Verbindungen und Secret-Referenzen administrieren; Verwenden und Verwalten
  getrennt berechtigen. #240 / PR #241 implementiert revisionsgeschützte Metadaten in Dateiablage
  und PostgreSQL, fail-closed Rollen, einen austauschbaren Secret-Store sowie Konsole,
  OpenAPI und SDK. Deaktivierte Verbindungen bleiben historisch erhalten und sind für
  reine Verwender nicht sichtbar.
- [x] Keine Secrets in BPMN, Formularen, Exporten, Prompts oder Browserantworten;
  lokale Endpunkte nur mit expliziter administrativer Freigabe. #240 / PR #241 hält Secret-Wert
  und -Referenz bereits aus allen API-/Browserantworten und erlaubt lokale Ziele nur
  nach Installations-Opt-in. #242 / PR #243 lehnt Secret-Attribute im BPMN-Vertrag ab; die
  #252 / PR #253 speichert auch in Definition und Lauf ausschließlich die opake Verbindungskennung
  und Revision; die Secret-Referenz bleibt in der internen Verbindungshistorie und wird erst
  unmittelbar vor dem Provideraufruf aufgelöst. Werkzeuge bleiben ein eigener Folgeslice.
- [x] Worker-Vertrag um eine besitzergebundene, atomare Lease-Verlängerung ergänzen
  (#238; PR #239). #246 / PR #247 ergänzt dauerhafte KI-Laufzustände mit getrennten Provider-/
  Ergebnis-Claims, Revisionen und konservativer Recovery; der ausführende Hintergrunddienst
  folgt mit #250 / PR #251 bis zum validierten `ResultReady`. #252 / PR #253 erzeugt den Lauf aus dem
  BPMN-Token und committed das Ergebnis zusammen mit Instanz, Subscriptions und Historie;
  die vollständige Störungsbedienung bleibt offen.
- [x] Typisierte Werkzeugregistry mit Schemas und expliziten Rechten. #254 / PR #255 bindet
  registrierte Version, Vertragshash, Außenwirkung und Verbindungserlaubnis, stellt den
  sicheren Katalog über API/SDK bereit und führt weder freie Shell-/SQL-Ausführung noch
  beliebige HTTP-Ziele ein. Die Runtime ist noch bewusst blockiert.
- [ ] Effektive Rechte als Schnittmenge von Verbindung, Workflow-Freigabe,
  Task-Werkzeugliste und fachlichem Kontext, niemals aus dem Prompt.
- [ ] Außenwirkung standardmäßig mit menschlicher Freigabe; administrative
  Vorabfreigaben pro Workflow nur für einzelne begrenzte Aktionen. Freigabe an
  konkrete Parameter binden; Änderungen verlangen eine neue Freigabe.
- [ ] Modell-/Tool-Ausgaben als untrusted behandeln; Prompt-Injection darf keine
  Rechte, Secret-Referenzen oder Freigaben verändern.
- [ ] Limits für Aufrufe, Laufzeit, Tokens und Aktionen; Kosten nur mit belegbarer
  Preisgrundlage anzeigen.
- [ ] Persistentes Ausführungsjournal und Idempotenz für Seiteneffekte; unklare
  Schreibausgänge anhalten, nicht blind wiederholen.
- [ ] Testmodus ohne Außenwirkung, nachvollziehbare Modell-/Prompt-/Tool-Versionen.
- [ ] Vorlagen: Klassifizieren, Extrahieren, Antwortentwurf, freigegebene Konnektoraktion.

## M6 – Runtime, Betrieb und Open Source

- [ ] Lokale Call Activities, Boundary Errors und erforderliche Eskalationen.
- [ ] Inclusive Gateway, Parallel-/Multi-Instance- und Timer-Recovery-Tests.
- [ ] Legacy-Abweichung zwischen `Token.ProcessInstanceId` und persistierter
  `InstanceId` bereinigen; laufende Instanzen vorwärtskompatibel migrieren. Bis dahin
  Aufgaben über die tatsächliche Mitgliedschaft in geladenen Instanz-Tokens prüfen.
- [ ] Explizites, am Deployment gespeichertes Expression-Profil, kein stiller
  Semantikwechsel durch V8-Fallback.
- [ ] Störungszentrum mit Diagnose, sicherem Retry, Eingabekorrektur, Abbruch und Audit.
- [ ] PostgreSQL-Revisionen, atomare Lease-Prüfung und gemeinsamer Commit von
  Instanz/Aufgaben/Jobs; Mehrprozessbetrieb erst nach Konkurrenztests freigeben.
- [ ] Dateiablage auf Entwicklung begrenzen; bestehende No-op-Transaktionen sind
  kein Rollback- oder Crash-Konsistenzversprechen.
- [ ] Große Einheiten nach Verantwortung aufteilen, nicht allein nach Zeilenzahl.
- [ ] Installation, Konfigurationsprüfung, Gesundheitsübersicht, Backup/Restore
  und Upgrade mit laufenden Instanzen reproduzierbar machen.
- [ ] MPL-2.0, Abhängigkeits-/Lizenzhinweise, SBOM, Sicherheitsmeldestelle und
  Beitragsdokumentation vervollständigen.
- [ ] Prozesspakete aus BPMN, Formularen, Verträgen und Fähigkeiten exportieren;
  Verbindungen/Identitäten beim Import explizit zuordnen, niemals Secrets exportieren.

Prozessverbund #154 folgt auf lokale Call Activities und Fehlerbehandlung;
vollständige Kompensation und echtes Mehrmandanten-Hosting bleiben separate Stränge.

## Verträge, Migration und Fertigkriterien

- Task-Zuweisungen erhalten einen diskriminierten Vertrag für stabile Referenzen
  und explizite Textwerte. Legacy-Text wird beim Laden als Text behandelt; Änderungen
  am Modus erfordern eine berechtigte Modelländerung/Veröffentlichung. Tests für
  beide Modi, unveränderte Legacy-Roundtrips, gleichnamige Einträge und manipulierte
  Moduswechsel gehören zum M1-/M3-Abnahmepaket.
- Versionierte OpenAPI-Verträge und generierte Clients; Problem Details für neue
  Fehlerverträge, kompatible Adapter statt abruptem Bruch vorhandener Endpunkte.
- Append-only-Historie mit Akteur, Zeitpunkt, Korrelation und datensparsamen Änderungen.
- Vorwärtsmigrationen; laufende Instanzen behalten Definition und gebundene Formulare.
- Reihenfolge M0 → M1/M2 → M3/M4 → M5; notwendige M6-Bausteine jeweils vorziehen.
- TDD, Testzweck-Kommentare und fokussierte PRs nach `main`. Für dieses autonome Mandat
  entfallen Zwischenreviews; vor der finalen Zusammenführung prüft ein direkter
  Astra-Subagent mit hoher Reasoning-Stufe den Gesamtstand und behebt Findings.
- Negative Rechte-/Verzeichnis-/Formulartests, Konkurrenz und Neustart, Host-Parität,
  KI-Injection/Freigabe/Limits/unklarer Ausgang sowie echte DB-/Upgrade-/Restore-Tests.
- CI um Architektur, OpenAPI-/Client-Drift, Migration, Secret- und Lizenzprüfungen
  erweitern. KI-Tests verwenden Fake-Provider; echte Aufrufe bleiben Opt-in.

**Erste Produktabnahme:** Eine frische Installation synchronisiert Keycloak-Identitäten,
veröffentlicht einen versionierten Workflow mit Auswahlfeldern, bearbeitet Aufgaben
gleichberechtigt in Flowzer und einem Host und setzt einen begrenzten KI-Task samt
Werkzeugfreigabe nach einem Neustart korrekt fort. Diese Abnahme ist noch offen.
