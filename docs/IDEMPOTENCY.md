# HTTP-Idempotenz

M0-Slice #186 / PR #187, aufbauend auf #185.

## Öffentlicher Vertrag

Direkte Workflow-Starts und beide historischen Aufgabenabschlussrouten akzeptieren
den optionalen Header `Idempotency-Key`. Ohne Header bleibt das bestehende Verhalten
kompatibel; Wiederholschutz besteht dann ausdrücklich nicht. Wer einen unsicheren
Netzwerkaufruf wiederholt, muss denselben Schlüssel bereits beim **ersten** Versuch senden.

- Länge 1–200, ausschließlich sichtbare ASCII-Zeichen, genau ein Headerwert.
- Der Scope bindet den Schlüssel an Operation, Zielressource und das verifizierte
  OIDC-Paar `(Issuer, Subject)`. Eine GUID oder Identität aus Body/freiem Header zählt nicht.
- Der Requestinhalt wird als kanonisches JSON mit sortierten Objektschlüsseln gehasht.
  Arrayreihenfolge und JSON-Typen bleiben fachlich relevant. Zwei Objekt-Reihenfolgen
  sowie mathematisch identische JSON-Zahlen (`1`, `1.0`, `1e0`) sind gleich. Die
  Zahlennormalisierung arbeitet dezimal und ohne `double`-Rundung. `1` und `"1"`
  oder fehlend und `null` bleiben dagegen verschieden.
- Identische Wiederholung liefert das gespeicherte Ergebnis: beim Start dieselbe
  Instanz-ID, beim Abschluss erneut Erfolg – auch über `/usertask` und `/form/result`
  hinweg. Es findet keine zweite Mutation statt.
- Derselbe Scope mit anderem Requesthash liefert `409 application/problem+json`.
  Weder ursprüngliche noch neue Eingabewerte werden zurückgegeben.
- Schlüssel anderer Personen, Operationen oder Zielressourcen teilen keinen Datensatz.
  Die Wiederholung verlangt weiterhin einen aufgelösten, authentifizierten Akteur.
  Ein erfolgreicher Abschluss-Replay gibt nur den bereits bekannten Erfolg zurück,
  keine Aufgaben- oder Prozessdaten.

`Idempotency-Key` ist in OpenAPI an allen drei Operationen dokumentiert. Neue
Konflikte verwenden Problem Details und enthalten vorläufig zusätzlich die kompatiblen
Felder `successful: false` und `errorMessage`.

## Persistenz und Konkurrenz

Persistiert werden ausschließlich SHA-256-Hashes von Scope und kanonischem Inhalt,
Operationsname, Status, Zeitpunkte und beim Start die resultierende Instanz-ID. Weder
Clientschlüssel, Principal noch Requestdaten stehen im Klartext im Datensatz.

PostgreSQL-Migration `004_idempotency.sql` legt einen eindeutigen Primärschlüssel auf
dem Scope-Hash an. Die Reservierung geschieht innerhalb derselben Transaktion wie
Instanz, Aufgaben und Subscriptions. `INSERT ... ON CONFLICT DO NOTHING` wartet auf
einen konkurrierenden uncommitted Gewinner; der Verlierer liest danach dessen Ergebnis.
Ein fehlgeschlagener Vorgang rollt Reservierung und Fachmutation gemeinsam zurück.
Tests verwenden getrennte `BpmnBusinessLogic`-Objekte, sodass keine gemeinsame
prozesslokale Sperre den Mehrprozess-Konflikt verdeckt.

Die Dateiablage schreibt atomare JSON-Dokumente und entfernt eine eigene Reservierung
nur, solange sicher noch keine dauerhafte Fachmutation begonnen hat. Scheitert das
Festschreiben des Ergebnisses erst danach, bleibt die Reservierung offen: Der Ausgang
ist unklar und weitere Ausführung mit demselben Schlüssel endet mit `409`, statt einen
möglichen Effekt zu duplizieren. Sie besitzt weiterhin weder Transaktionsrollback noch
ein prozessübergreifendes Lock. Sie ist für lokale Entwicklung, **nicht** als Nachweis
für mehrere API-Prozesse gedacht. Drittadapter dürfen das Default-Storageobjekt nur
nutzen, wenn die betreffenden Requests keinen Idempotenzheader senden; für den
öffentlichen Vertrag müssen sie `IIdempotencyStorage` implementieren.

## Aufbewahrung und Betrieb

**Abgeschlossene** Datensätze laufen sieben Tage nach Anlage ab und werden bei einem
folgenden idempotenten Request best-effort bereinigt. Danach darf derselbe
Clientschlüssel wieder eine neue Mutation bezeichnen; Clients müssen ihre Retry-Periode
darunter halten. Offene Reservierungen laufen bewusst nicht automatisch ab, weil sie
bei nichttransaktionaler Ablage einen unklaren Facheffekt markieren können. Ihre
operative Auflösung benötigt einen späteren, auditierten Klärungsweg. Eine
konfigurierbare Retention und ein eigener Cleanup-/Klärungsjob können später folgen;
sieben Tage für abgeschlossene Ergebnisse sind für Profil 1 Teil des Vertrags.

Vor Upgrade Migration 004 in einer Testinstallation anwenden und danach einen
idempotenten Start sowie einen Abschluss-Replay prüfen. Rollback des Anwendungscodes
bei bestehenbleibender Tabelle ist ungefährlich; die Tabelle darf erst nach Ablauf
aller benötigten Wiederholungsfristen separat entfernt werden. Dieser Slice führt
keine produktive Migration aus.

## Grenzen

- Keine BFF-/CSRF-Änderung und keine globale Pflicht zum Header.
- Keine Outbox und keine Idempotenz für Nachrichtenstarts, Timer, Service-/KI-Worker,
  Connectoren, Benachrichtigungen oder sonstige externe Effekte.
- Ein Client, der den ersten Versuch ohne Schlüssel sendet, kann ihn nicht nachträglich
  idempotent machen.
- Der Vertrag speichert den fachlichen Erfolg, nicht die byteidentische historische
  HTTP-Antwort oder damalige Darstellung. Startantworten werden erneut mit den heute
  geltenden Objekt-/Diagnoserechten projiziert.
- Allgemeine Zustandsrevisionen und Konkurrenzsicherheit aller Engine-Mutationen
  bleiben M6; belegt ist hier nur die eindeutige Idempotenzreservierung in PostgreSQL.
