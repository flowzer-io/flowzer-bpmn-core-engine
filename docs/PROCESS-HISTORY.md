# Append-only Vorgangshistorie

**Stand:** 9. September 2026 · **Slice:** #226 / PR #227

Flowzer stellt die bereits transaktional gespeicherte Human-Task-Auditspur als
erste echte Vorgangshistorie bereit. Der Vertrag ist absichtlich klein: Er behauptet
keine vollständige Engine-Historie, solange Start-, Token-, Timer-, Job- und
Störungsereignisse noch nicht als unabhängige Events gespeichert werden.

Flowzer bleibt dabei eigenständig und hostneutral. Einbettende Anwendungen verwenden
die öffentliche API beziehungsweise das TypeScript-SDK und erhalten keinen direkten
Zugriff auf Datenbanktabellen.

## Öffentlicher Vertrag

`GET /instance/{instanceId}/history` liefert einen Instanzbezug und deterministisch
sortierte Human-Task-Ereignisse:

- unveränderliche Ereignis- und Task-ID
- BPMN-Flow-Node-ID
- Aktion `claim`, `release`, `assign`, `delegate` oder `complete`
- monotone Taskrevision
- UTC-Zeitpunkt

Die Projektion enthält bewusst **keine** Owner-Hashes, Benutzer-IDs, Anzeigenamen,
Begründungen, Korrelationen, Variablen oder Formulardaten. Fremde und unbekannte
Instanzen liefern denselben `404`-Problem-Details-Vertrag. Sichtbarkeit folgt der
zentralen Instanz-Objektberechtigung: Initiator und aktuell berechtigte Bearbeiter
sehen die minimale Historie, frühere Bearbeiter verlieren den Zugriff; der Betrieb
behält die Diagnosesicht.

## Persistenz

PostgreSQL erhält mit Migration `010_process_history_task_lifecycle.sql` eine
indexierte `process_instance_id` an der bestehenden Append-only-Audittabelle. Ein
robuster Backfill übernimmt gültige Instanz-UUIDs aus bisherigen Eventkörpern; defekte
Altdokumente blockieren das Upgrade nicht. Es gibt bewusst keinen Fremdschlüssel zur
Instanztabelle, damit eine spätere technische Bereinigung die Auditspur nicht
unbemerkt mitlöscht.

Die Dateiablage liest und filtert die einzelnen Eventdateien. Dieser Vollscan ist nur
für den ausdrücklich auf Entwicklung und einen API-Prozess begrenzten Adapter
akzeptabel. PostgreSQL bleibt der produktive Mehrprozesspfad.

## Console und SDK

Das hostneutrale SDK bietet `client.instances.history(...)`; die React-Schicht trennt
den Cache wie alle geschützten Daten nach Installation und opakem Sitzungsscope. Die
Console zeigt die echte Task-Auditspur in der vorhandenen Verlaufsansicht und lädt sie
nur für die bereits freigegebene Diagnosesicht. Reine Übersichtsansichten lösen keinen
technischen History-Request aus.

## Aufbewahrung

Die Historie unterliegt der **Instanz-Retention**: Wird eine beendete Instanz gelöscht —
durch die konfigurierte Aufbewahrungsfrist oder von Hand über `DELETE /instance/{id}` —,
geht ihre Human-Task-Auditspur mit. Nach Ablauf der Frist ist der Vorgang Historie, und
eine Historie ohne Instanz wäre nur noch Datenbestand mit Personenbezug.

Das ist kein Widerspruch zum fehlenden Fremdschlüssel: Der verhindert, dass eine
technische Bereinigung die Spur *unbemerkt* mitnimmt. Die Aufbewahrung löscht sie
ausdrücklich, an einer nachlesbaren Stelle (`InstancePurge`) und in derselben
Transaktion wie die Instanz. Eine eigene, von der Instanz unabhängige Aufbewahrung der
Historie gibt es nicht und ist auch nicht vorgesehen.

Frist, Reihenfolge global/je Workflow, die vollständige Liste des Gelöschten und die
Grenzen der Dateiablage stehen in [OPERATIONS.md](OPERATIONS.md), Abschnitt
„Aufbewahrung".

## Bewusste Grenzen

- Eine noch unberührte Human Task besitzt kein künstlich nachträglich erzeugtes
  `created`-Ereignis.
- Die bisherige Tokenansicht bleibt eine Zustandsdiagnose, keine vollständige Historie.
- Personenbezogene Auditdetails und Begründungen bleiben intern.
- Kommentare, Formularänderungen, Timer, Worker-/KI-Läufe, Störungen und allgemeine
  Prozessschritte benötigen eigene spätere Ereignistypen. Ihre Aufbewahrung folgt dann
  derselben Regel: Sie hängen an der Instanz und verschwinden mit ihr.
- Archivierung oder Export vor dem Löschen gibt es nicht. Wer eine Historie über die
  Frist hinaus braucht, holt sie vorher über `GET /instance/{id}/history` ab oder nimmt
  den betroffenen Workflow mit `retentionDays = 0` von der Aufbewahrung aus.

## Verwandte Auswertungen

Die Human-Task-Auditspur beantwortet, was an *einem* Vorgang geschah. Die Frage, wie
lange Vorgänge *insgesamt* dauern und wo sie warten, beantwortet die verdichtete
Betriebsauswertung unter `/operations/analytics` aus der datensparsamen
Engine-Ereignisspur — ohne Personenbezug und ohne einzelne Instanzen. Kennzahlen,
Datenquelle und Grenzen stehen in [ANALYTICS.md](ANALYTICS.md).
