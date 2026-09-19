# Auswertungen auf der Laufzeithistorie

**Stand:** 19. September 2026

Flowzer schreibt je sichtbarem Knotenzustand ein datensparsames `RuntimeNodeEvent`
(siehe [RUNTIME-DIAGRAM.md](RUNTIME-DIAGRAM.md)). Daraus lassen sich drei Fragen
beantworten, ohne irgendetwas zusätzlich zu erfassen:

- Wie lange dauert ein Vorgang?
- Wo wartet er am längsten?
- Wie oft endet er wie?

Mehr ist es bewusst nicht. Es gibt keine Zeitreihendatenbank, keinen Export, keinen
Versionsvergleich und keine SLA-Schwellen; die Zahlen entstehen serverseitig im
Speicher aus dem, was ohnehin gespeichert ist.

## Öffentlicher Vertrag

Beide Endpunkte verlangen die Betriebsrolle. Sie beschreiben fremde Vorgänge in ihrer
Gesamtheit — das ist eine Betriebssicht, keine Auskunft über einzelne Menschen.

`GET /operations/analytics/workflows?from=&to=`

Je Katalogeintrag eine Zeile mit Kennung, Name, Anzahl der Instanzen nach Ausgang und
der Durchlaufzeit der abgeschlossenen Instanzen. Ein Katalogeintrag ohne Vorgang im
Zeitraum bleibt als Zeile mit Nullen stehen: Dass ein Workflow nicht benutzt wurde,
ist selbst eine Auskunft. Umgekehrt verschwindet eine Instanz ohne Katalogeintrag
(Direkt-Deploy) nicht — dann steht ihre Kennung als Anzeigename.

`GET /operations/analytics/workflows/{metaDefinitionId}?from=&to=&definitionId=`

Dieselben Kennzahlen für einen Katalogeintrag, dazu

- **je Flow-Knoten**: Kennung, Name, Anzahl der Durchläufe, Wartezeit als
  Median/p90/Mittel/Max und die Anzahl der gerade dort wartenden Tokens —
  sortiert nach Median-Wartezeit absteigend, Engpässe zuerst;
- eine **Zeitreihe** „gestartet je Tag“ und „beendet je Tag“, lückenlos über jeden
  Kalendertag des Zeitraums in UTC.

`definitionId` schränkt auf genau eine Version ein. Ein unbekannter Katalogeintrag
ohne jede Instanz antwortet mit `404`.

## Kennzahlen und ihre Definition

| Kennzahl | Definition | Datenquelle |
|---|---|---|
| Zeitraum | `from` zählt mit, `to` nicht. Ohne Angabe die letzten 30 Tage bis jetzt. | Anfrage |
| Instanz im Zeitraum | Die Instanz wurde darin **gestartet**. | abgeleiteter Start |
| Start einer Instanz | ältester `StartTime` ihrer Tokens | `ProcessInstanceInfo.Tokens` |
| Ende einer Instanz | jüngster `LastStateChangeTime`, nur bei beendeter Instanz | `ProcessInstanceInfo.Tokens` |
| laufend | Zustand ist keiner der drei Endzustände | `ProcessInstanceInfo.State` |
| abgeschlossen | `Completed` oder `Compensated` | `ProcessInstanceInfo.State` |
| abgebrochen | `Terminated` — Betriebsabbruch oder Terminate-Endereignis, **kein** Fehler | `ProcessInstanceInfo.State` |
| gescheitert | `Failed` | `ProcessInstanceInfo.State` |
| Durchlaufzeit | Ende minus Start, **nur der abgeschlossenen** Instanzen | abgeleitet |
| Durchlauf eines Knotens | ein `Active` und sein nächster Endzustand desselben Tokens am selben Knoten | `RuntimeNodeEvent` |
| Wartezeit eines Knotens | Spanne zwischen `Active` und dem nächsten `Completed` oder `Withdrawn` desselben Tokens | `RuntimeNodeEvent` |
| wartende Tokens | Tokens mit Zustand `Active`, die gerade an diesem Knoten stehen | `ProcessInstanceInfo.Tokens` |
| Name eines Schritts | aus der gewählten Version, sonst aus der deployten | BPMN der Version |

Start- und Endzeitpunkt werden genauso abgeleitet wie in der Instanzprojektion
(`GetStartedAt`/`GetFinishedAt`): Die Ablage speichert keinen eigenen
Instanz-Zeitstempel.

### Warum die Durchlaufzeit nur den Abschluss misst

Ein Abbruch und ein Fehler sagen etwas über den Ausgang, aber nichts darüber, wie
lange der Vorgang normalerweise dauert. Ein abgelehnter Urlaubsantrag, der nach zwei
Minuten am Terminate-Endereignis endet, würde die Kennzahl verkürzen, ohne dass das
jemand sieht. Die Anzahlen nach Ausgang stehen direkt daneben und tragen diese
Information.

### Perzentile

Median und p90 werden **linear zwischen den benachbarten Rängen interpoliert**
(Methode R-7, wie in Tabellenkalkulationen). Bei gerader Anzahl ist der Median damit
das Mittel der beiden mittleren Werte — genau das, was beim Wort „Median“ erwartet
wird. Der Rang-Sprung („nearest rank“) würde bei den kleinen Stichproben, die ein
Monatszeitraum liefert, einen der beiden Werte bevorzugen und den Median von vier
Vorgängen unerklärlich machen.

Eine leere Stichprobe liefert `null`, nicht `0`: Eine Dauer, die niemand gemessen hat,
ist keine Dauer von null.

### Durchläufe ohne festgehaltenes Ende

Die Ereignisspur hält nur Zustände fest, die an einer Persistenzgrenze tatsächlich
sichtbar waren. Ein Durchlauf, dessen Ende im Zeitraum fehlt — weil er noch läuft oder
weil der Zeitraum davor endet —, zählt als Durchlauf, trägt aber keine Wartezeit bei.
Ein zweites `Active` ohne Abschluss dazwischen wird genauso behandelt.

## Grenzen

- **Speicherberechnung.** Die Aggregation läuft im Arbeitsspeicher des API-Prozesses.
  Der Zeitraum ist deshalb auf **366 Tage** begrenzt, und eine Auswertung mit mehr als
  **50 000** Laufzeitereignissen wird mit `422` und dem Hinweis auf einen kürzeren
  Zeitraum abgelehnt, statt langsam zu werden. Beide Grenzen sind bewusst fest: Erst
  mit realen Volumenmessungen lässt sich sagen, ob eine Vorverdichtung nötig ist.
  Für den Ausgang der Vorgänge liest die Auswertung zudem den gesamten Instanzbestand —
  wie das Betriebsbild unter `/operations/diagnostics`. Ein Zeitraumfilter in der Ablage
  gäbe es erst mit einem gespeicherten Instanz-Zeitstempel; den gibt es heute nicht.
- **Aufbewahrung.** Gelöschte Instanzen — technische Bereinigung oder Aufbewahrungs­frist —
  fehlen in der Auswertung. Ihre Laufzeitereignisse bleiben ohne Fremdschlüssel zur
  Instanztabelle zwar liegen, die Instanz selbst aber trägt Start, Ende und Ausgang; ohne
  sie zählt der Vorgang nicht mehr mit. Eine Auswertung über einen Zeitraum, aus dem
  bereits gelöscht wurde, zeigt deshalb weniger Vorgänge, als es damals gab.
- **Keine Personenbezüge.** Die Projektion enthält keine Akteure, keine Variablen, keine
  Formulardaten, keine Token- oder Korrelationskennungen und keine einzelnen Instanzen.
  Sie ist damit keine Leistungsmessung einzelner Menschen und soll auch keine werden.
- **Instanzen vor der Ereignisspur** besitzen keine rückwirkend erfundene Historie. Ihre
  Anzahlen und Durchlaufzeiten stimmen, ihre Schrittkennzahlen fehlen. Der aktuelle
  Tokenstand zählt trotzdem als „wartend“.
- **Migrierte Instanzen** werden unter dem Katalogeintrag geführt, dessen Version sie
  heute tragen. Ohne Versionswahl zählen die Ereignisse aller Versionen mit, unter denen
  sie gelaufen sind; mit Versionswahl nur die dieser einen.
- **Ablagen ohne Ereignisspur** liefern die Anzahlen nach Ausgang, aber keine
  Schrittkennzahlen. Die mitgelieferten Datei- und PostgreSQL-Adapter unterstützen den
  Vertrag vollständig.
- **Die Dateiablage** beantwortet die Abfrage mit einem Vollscan. Das ist nur für ihren
  dokumentierten Einzelprozess-Entwicklungsbetrieb vertretbar.

## Persistenz

Die Auswertung fragt die Ereignisspur nicht je Instanz ab, sondern je gebundener
Version und Zeitraum: `IRuntimeNodeEventStorage.GetByDefinitionIds(ids, fromUtc, toUtc)`.
Ein Lauf je Instanz wäre in PostgreSQL eine Abfrage je Vorgang.

Migration `018_runtime_node_event_analytics_index.sql` legt dafür den rein additiven
Index `(definition_id, occurred_at, token_id, id)` an. Der Index aus Migration `012`
beginnt mit `process_instance_id` und trägt diese Abfrage nicht.

## Console

Die Seite **Auswertungen** unter `/analytics` gehört zum Betrieb und erscheint nur mit
der Betriebsrolle. Sie zeigt die Übersicht als Tabelle mit Zeitraumwahl (7/30/90 Tage
oder eigener Zeitraum); ein Klick öffnet die Details mit den Kennzahlen, dem
Balkendiagramm „Wartezeit je Schritt“ (Median als Balken, p90 als Marke), der Tabelle
der Schritte und der Zeitreihe. Jeder Wert steht zusätzlich als Text — die Balkenlänge
ist nie das einzige Signal, und es wird keine Diagrammbibliothek geladen.

## Was noch fehlt

- **Export** als CSV oder PDF. Die Zahlen lassen sich heute nur lesen, nicht weitergeben.
- **Versionsvergleich**: „hat die neue Version den Engpass beseitigt?“ verlangt zwei
  Auswertungen nebeneinander und eine belastbare Aussage über ihre Vergleichbarkeit.
- **SLA-Schwellen und Alarme**: eine erwartete Dauer je Workflow oder Schritt, deren
  Überschreitung auffällt, statt gesucht werden zu müssen.
- **Vorverdichtung** für lange Zeiträume, sobald reale Volumen die Grenzen von 366 Tagen
  und 50 000 Ereignissen zu eng machen.
