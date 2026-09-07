# Instanzrechte und öffentliche Vorgangsansichten

**M0-Slice #178.** Aufbauend auf #176 / PR #177; Abnahme erfolgt über eigene
API-/Storage-/UI-Tests. Keine automatische Migration historischer Formulardaten
zu vertrauenswürdigen Identitäten.

## Berechtigungsentscheidung

| Kontext | Übersicht | Tokens/Variablen/Subscriptions |
|---|---|---|
| Verifizierter Initiator eines HTTP-Starts | ja, auch nach Abschluss | nein |
| Aktuell berechtigter Bearbeiter einer offenen Aufgabe | ja | nur deklarierter Formularkontext über Aufgaben-API |
| Operator | ja | Diagnoseansicht |
| Nur Modeler, Ordnerverantwortlicher oder Worker | nein | nein |
| Fremde/fehlende Instanz | `404`, aus Listen entfernt | `404` |

Bestehende Zuweisungsregeln gelten weiter; unzugewiesene offene Aufgaben sind
bewusst für alle Zugelassenen verfügbar. Die spätere Einführung stabiler
Verzeichnisreferenzen und expliziter Textzuweisungen ändert diese Tabelle nicht.
Eine frühere Bearbeitung allein ist noch kein dauerhaftes Historienrecht.

## Identität und Persistenz

Der Initiator stammt vom authentifizierten Request (`Issuer`, `Subject`), niemals
aus `variables.UserId`, Anzeigename oder E-Mail. Die bestehende GUID-basierte
Benutzerauflösung bleibt für API-Aktionen bestehen; der neue Besitznachweis ist
zusätzlich issuergebunden. Development-Header verwenden eine eigene technische
Issuer-Kennung, die keine echte OIDC-Identität imitieren kann.

Bei Instanzmutationen bleibt der Initiator erhalten. Technische Starts sowie alte
Instanzen ohne verifizierten Initiator haben keinen Antragsteller-Besitznachweis;
sie bleiben über aktuelle Aufgabenrechte oder Operator-Diagnose erreichbar.
Der Storage-Vertrag ergänzt optionale Metadaten, ohne JSON-Variablen umzudeuten.

## Öffentlicher Vertrag

Bestehende Instanzrouten und Antwortumschläge bleiben erhalten. Das DTO kennzeichnet
mit `canInspect` ausdrücklich, ob Tokens und technische Subscription-Endpunkte
verfügbar sind. Die Vorgangsübersicht enthält ID, Workflow, Status, Zeitpunkte und
offene menschliche Aufgaben; `tokens` ist ohne Diagnoseberechtigung leer.
Startantworten unterliegen derselben Projektion wie spätere Detailabrufe.

Aufgabenlisten dürfen nicht über `Token.CurrentFlowElement`, `Variables` oder
`OutputData` den gesamten Prozesskontext umgehen. Ohne Betriebsrecht werden nur
im veröffentlichten Formular deklarierte Eingabefelder als Ausgangswerte geliefert;
fehlende/ungültige Formularauflösung liefert keine Variablen. Eigene Formularrechte
und serverseitige Submission-Validierung sind der anschließende M0/M2-Slice.

## Grenzen

- Leere konfigurierte Fähigkeitsrollen bleiben gemäß bestehendem Vertrag permissiv.
  `Roles:Operator` deshalb in produktiven Installationen ausdrücklich konfigurieren.
- Verzeichnisabgleich, Claims/Delegation und gemeinsame Formularverträge folgen separat.
- Kein Mehrprozess-/Rollbackversprechen für die dateibasierte Ablage.
- Externe Reviews sind im aktuellen autonomen Mandat ausdrücklich ausgesetzt;
  TDD, Selbstprüfung und CI bleiben Pflicht.
