# CodeQL- und Storage-Härtung

**Stand:** 9. September 2026, Issue #222 / PR #223. Der Slice ist noch nicht nach
`main` gemergt.

## Ziel

Der Slice beseitigt die offenen CodeQL-Befunde ohne Unterdrückungen und verkleinert
gleichzeitig die Vertrauensgrenze gespeicherter Arbeitsdaten. An öffentlichen HTTP-
Verträgen ändert sich dadurch nichts.

## Konkrete Dokumente statt polymorpher Arbeitsdaten

Formulare, Formularmetadaten, Nachrichten-, Signal- und Timer-Subscriptions sowie
Service-Task-Webhooks werden mit `TypeNameHandling.None` gelesen und geschrieben.
User-Task-Subscriptions und Service-Task-Jobs besitzen zusätzlich schlanke interne
Dateidokumente:

- Ein Job speichert `TokenId` und `FlowNodeId`, aber keinen duplizierten Runtime-Token.
- Eine User-Task-Subscription speichert ihre stabile Token- und Flow-Node-Referenz.
  Beim Lesen wird sie nur an genau einen weiterhin aktiven User-Task-Token der
  zugehörigen Instanz gebunden.
- Historische Dateien werden als begrenzter JSON-Baum gelesen. Ein vorhandenes
  `$type`-Feld wird dabei nie zur Instanziierung eines CLR-Typs verwendet.
- PostgreSQL speichert Service-Task-Jobs und Webhooks ebenfalls als konkrete
  Dokumente. Die bereits vorhandene `token_id`-Spalte bleibt für Jobs autoritativ.

Die nach außen gelieferten Worker-DTOs bleiben unverändert. Bestehende Jobs werden
beim ersten Lesen vorwärtskompatibel aus der alten Tokenstruktur aufgelöst.

## Verbleibende polymorphe Grenze

Prozessinstanzen und BPMN-Definitionen enthalten echte polymorphe Objektgraphen. Sie
verwenden vorerst weiterhin den gesonderten Legacy-Serializer mit Assembly-Allowlist.
Dateien beziehungsweise Datenbankinhalte innerhalb dieser Grenze gelten daher als
administrativ geschützte Betriebsdaten. Eine spätere explizite Runtime-Dokumentstruktur
muss die breite Assembly-Allowlist ersetzen; dieser Slice behauptet das nicht.

Die Dateiablage bleibt unabhängig davon ausschließlich ein Einzelprozess-
Entwicklungsweg. Sie erhält durch diese Härtung weder Rollback noch Crash-Konsistenz.

## Weitere beseitigte Befunde

- Das TypeScript-SDK entfernt abschließende URL-Slashes in linearer Zeit statt mit
  einem potenziell polynomialen regulären Ausdruck.
- Die Icon-Generierung maskiert Backslash, Anführungszeichen, Zeilenumbrüche und
  JavaScript-Zeilentrenner, bevor SVG-Markup in ein Stringliteral geschrieben wird.
- Worker-Typ, Worker-ID, Worker-Fehlermeldung und rohe Request-Pfade werden nicht in
  Betriebslogs übernommen. Fachlich benötigte Fehlerdetails bleiben am geschützten Job.
- Negative Draft- und Lifecycle-Revisionen passieren einen statisch erkennbaren
  Standard-Guard. Die vorhandenen HTTP-Verträge (`400` für Drafts, feldbezogenes
  `422 revision.invalid` für Lifecycle-Aktionen) bleiben erhalten.

## Abnahme

- Roundtrip- und Manipulationstests belegen konkrete JSON-Dokumente ohne `$type`.
- Legacy-Tests belegen, dass fremde CLR-Typnamen nicht instanziiert werden.
- Logtests verwenden Eingaben mit Steuerzeichen und prüfen Datenminimierung sowie
  unveränderte fachliche Persistenz.
- URL- und Icon-Tests verwenden lange beziehungsweise ausbrechende Eingaben.
- Nach dem Push muss CodeQL sowohl die Befunde auf `main` als auch die im gestapelten
  PR entstandenen Befunde als behoben bewerten. Ein bloßes Schließen oder
  Unterdrücken der Warnungen ist keine Abnahme.
