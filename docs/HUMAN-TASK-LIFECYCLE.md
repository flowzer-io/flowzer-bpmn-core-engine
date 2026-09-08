# Human-Task-Lifecycle

**Stand:** 8. September 2026 · **Slice:** #204 / PR #205

Flowzer trennt die unveränderliche Zuweisung im veröffentlichten BPMN-Modell vom
tatsächlichen Bearbeiter einer laufenden Aufgabe. Modelle dürfen weiterhin bewusst
zwischen freien Textwerten und stabilen Directory-Benutzer-/Gruppenreferenzen wählen.
Eine Gruppe ist eine Kandidatenmenge, aber niemals selbst tatsächlicher Bearbeiter.

## Öffentlicher Vertrag

Jede Aufgabe liefert unter `workState` die monotone Lifecycle-Revision, den tatsächlichen
Directory-Bearbeiter (soweit vorhanden), dessen Anzeigenamen sowie serverseitig berechnete
Fähigkeiten `canWork`, `canClaim`, `canRelease`, `canAssign` und `canDelegate`.

| Methode | Pfad | Bedeutung |
| --- | --- | --- |
| `POST` | `/usertask/{taskId}/claim` | Freie Aufgabe mit `expectedRevision` übernehmen |
| `POST` | `/usertask/{taskId}/release` | Eigene Aufgabe mit Revision und Begründung zurückgeben |
| `POST` | `/usertask/{taskId}/assign` | Als Operator einem aktiven Directory-Benutzer zuweisen |
| `POST` | `/usertask/{taskId}/delegate` | An einen aktiven, zulässigen Directory-Kandidaten delegieren |
| `GET` | `/identity-directory/user-tasks/{taskId}/assignees` | Aktionsgebundene Suche nach zulässigen Zielbenutzern |

Gruppenreferenzen, inaktive oder unbekannte Benutzer und leere beziehungsweise mehr als
500 Zeichen lange Begründungen ergeben einen feldbezogenen `422`-Fehler. Fremde Aufgaben
und objektbezogen unzulässige Aktionen bleiben über `404` verborgen. Eine veraltete
Revision liefert Problem Details mit `409`, Code `user_task.revision_conflict` sowie
erwarteter und aktueller Revision.

Abschluss und Entwurfsmutationen akzeptieren zusätzlich `expectedTaskRevision`. Damit
kann ein vor einer Übergabe geöffneter Tab keinen inzwischen veralteten Stand schreiben.
Der Wert ist für bestehende API-Clients zunächst optional.

## Rechte

- Vor einer Übernahme gelten die im BPMN veröffentlichten Text- oder Directory-Kandidaten.
- Nach einer Übernahme darf ausschließlich der tatsächliche Bearbeiter arbeiten. Frühere
  Gruppenmitglieder verlieren Aufgabenliste, Formular, Entwurf, Abschluss und Instanzsicht.
- Der Operator darf Aufgaben sehen, bearbeiten und einem beliebigen aktiven
  Directory-Benutzer zuweisen. Das wird im Zustand als Zuweisung außerhalb des
  Kandidatenpools nachvollziehbar markiert.
- Akteur und tatsächlicher Claim-Besitzer werden ausschließlich aus dem authentifizierten
  Requestkontext abgeleitet. Vom Client gelieferte Akteursfelder bleiben wirkungslos.
- Private Entwürfe werden bei einer Delegation weder übertragen noch für den neuen
  Bearbeiter sichtbar.

## Persistenz und Konkurrenz

PostgreSQL sperrt die Aufgaben-Subscription innerhalb der Transaktion und schreibt Zustand
und genau ein Auditereignis per Compare-and-swap. Zwei API-Prozesse können deshalb nicht
dieselbe Revision erfolgreich übernehmen. Abschluss und Lifecycle-Wechsel verwenden dieselbe
Task-Sperre. Der aktuelle Zustand wird mit der Subscription gelöscht; die Append-only-
Auditspur bleibt nach Abschluss oder Abbruch erhalten.

Die Dateiablage serialisiert Zustandswechsel nur innerhalb eines API-Prozesses und bleibt
ausdrücklich ein Entwicklungsadapter. Sie besitzt keinen Rollback über mehrere Dateien und
ist nicht für produktiven Mehrprozessbetrieb freigegeben.

## Bewusste Grenzen

Dieser Slice enthält noch keine Fristen, Wiedervorlagen, Erinnerungen, Benachrichtigungen,
Kommentare oder automatische Rechtevertretung. Die getrennte Umsetzung von Fristen und
Benachrichtigungen steht in [Human-Task-Fristen und Benachrichtigungen](HUMAN-TASK-DEADLINES.md).
Ein öffentlicher Historien-Endpunkt folgt
erst mit der objektberechtigten Vorgangshistorie. TickyTask soll später denselben API-Vertrag
über das headless SDK verwenden und erhält keinen direkten Datenbankzugriff.
