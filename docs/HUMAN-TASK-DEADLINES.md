# Human-Task-Fristen und Benachrichtigungen

**Stand:** 8. September 2026 · **Slice:** #206

Flowzer bindet `zeebe:taskSchedule/@dueDate` und `@followUpDate` beim ersten
Auftreten einer stabilen Human-Task-ID an absolute UTC-Zeitpunkte. Token-Updates,
Polling und Neustarts berechnen den Termin nicht neu. Offene Altaufgaben ohne
Deadline-Datensatz werden beim Start des Deadline-Schedulers einmalig anhand ihres
gespeicherten Token-Startzeitpunkts nachgezogen.

## Unterstütztes Zeitprofil

- ISO-8601-Zeitpunkt mit `Z` oder explizitem Offset
- ISO-8601-Dauer wie `P3D` oder `PT48H`, relativ zu `activatedAtUtc`
- lokale Zeitangaben ohne Offset: `unsupported`
- Ausdrücke wie `=now() + duration("PT2H")`: `unsupported`
- syntaktisch ungültige Angaben oder Wiedervorlage nach Fälligkeit: `invalid`

Nur `resolved` erzeugt Automatisierung. Rohwerte bleiben für Diagnose und
Kompatibilität erhalten, werden aber weder vom Browser noch vom Server als exakter
Termin geraten.

## Meilensteine

Der Scheduler erzeugt begrenzt und nachholbar:

1. `follow_up` am Wiedervorlagezeitpunkt,
2. `reminder` für die konfigurierten Vorlaufzeiten,
3. `due` bei Fälligkeit,
4. `escalation` nach der konfigurierten Überschreitungsdauer.

Die Eskalation dieses Slices ist eine persistente In-App-Meldung und der Status
`escalated`. Sie löst **kein** BPMN-Eskalationsereignis und keine automatische
Delegation aus.

Jeder Meilenstein trägt einen eindeutigen Schlüssel aus Task, Policy-Version, Art und
Zeitpunkt. PostgreSQL sichert Deduplizierung und Schedulerfortschritt transaktional;
mehrere API-Prozesse serialisieren sich zusätzlich an der Task-Subscription. Die
Dateiablage ist nur für einen API-Prozess gedacht.

## Berechtigter Feed

`GET /notifications` liefert höchstens 100 Meldungen und unterstützt `before`,
`limit` und `unreadOnly`. `POST /notifications/{id}/read` quittiert idempotent für
die aktuelle Person. Beide Wege prüfen unter der Task-Sperre dieselbe tatsächliche
Bearbeitungshoheit wie Liste, Formular, Entwurf und Abschluss. Nach Claim oder
Delegation sieht ein früherer Kandidat die Meldung nicht mehr; fremde IDs liefern
`404`.

Meldungen enthalten nur Task-ID, Art, Zeitpunkt, Lesestatus und serverseitige
Anzeigetexte. Prozessvariablen, Formulardaten und Identity-Claims werden nicht
übernommen. Beim Abschluss oder Abbruch entfernt der Task-Fremdschlüssel den noch
offenen Feed. Eine spätere Vorgangshistorie erhält einen eigenen Rechtevertrag.

## Konfiguration

```json
"UserTaskDeadlines": {
  "Enabled": true,
  "PollIntervalSeconds": 15,
  "BatchSize": 100,
  "PolicyVersion": "default-v1",
  "ReminderLeadTimes": [ "P1D", "PT1H" ],
  "EscalationAfterDue": "P1D"
}
```

Die Policy wird beim Taskstart mitgebunden. Eine spätere Konfigurationsänderung
verschiebt bestehende Termine nicht. Für bewusst geänderte Regeln ist eine neue
`PolicyVersion` zu verwenden.

## Bewusste Grenzen

E-Mail, Push, Chat-Zustellung, frei konfigurierbare Reminder pro Task, automatische
Vertretung sowie BPMN-Error-/Escalation-Propagation sind nicht Teil dieses Slices.

