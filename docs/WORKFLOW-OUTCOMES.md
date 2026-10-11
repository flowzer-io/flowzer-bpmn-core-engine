# Sichere fachliche Ergebnisse einer Workflow-Instanz

## Kleiner, getrennter Vertrag

Die bestehende objektberechtigte Instanzübersicht erhält additiv `outcome`:

| JSON-Wert | Bedeutung |
| --- | --- |
| `"Unknown"` | Kein eindeutig belegtes, ausdrücklich freigegebenes fachliches Ergebnis. |
| `"Approved"` | Der freigegebene reguläre Genehmigungs-Endknoten wurde tatsächlich abgeschlossen. |
| `"Rejected"` | Der freigegebene reguläre Ablehnungs-Endknoten wurde tatsächlich abgeschlossen. |

`state` bleibt der **technische** Enginezustand. `Completed` ist keine Genehmigung,
`Terminated` keine Ablehnung. `wasWithdrawn` bleibt der separate persönliche Rückzug.
Es gibt keinen neuen Endpunkt, Formularrenderer, Workflowstatus-Sync oder Ticketstatus.

Der Projektor läuft innerhalb der bestehenden Rechteprüfung von Instanz-GET und
Instanzliste. Ein fremder Vorgang bleibt unsichtbar beziehungsweise `404`. Für das
minimale Ergebnis ist keine Diagnoseberechtigung nötig; Tokens, Variablen, Kommentare
und Personen werden dadurch nicht zusätzlich ausgegeben. Start-, Cancel- und
Withdraw-Antworten, die den direkten DTO-Mapper verwenden, bleiben konservativ
`Unknown`; der berechtigte erneute GET klärt den aktuellen Stand.

Ein Host bindet die Antwort weiterhin an die bereits serverseitig registrierte
Instanz- und Definitions-ID. Fehlende, unbekannte oder widersprüchliche Vertragswerte
werden niemals durch eigene Auswertung des technischen Zustands ersetzt.

## Freigabe nur für eine konkrete Version und deren Inhalt

Die Serverkonfiguration `WorkflowOutcomes:Definitions` ist standardmäßig **leer**.
Ohne ausdrückliche reviewte Zuordnung bleibt jedes Ergebnis `Unknown`. Jeder Eintrag
benennt eine konkrete Definitions-GUID, Katalog-ID, Prozess-ID, die beiden erlaubten
Root-Endknoten und den SHA256 des exakt gespeicherten UTF-8-BPMN-XMLs.

Beispiel mit ausschließlich **synthetischen**, nicht verwendbaren Zielwerten:

```json
{
  "WorkflowOutcomes": {
    "Definitions": [
      {
        "DefinitionId": "00000000-0000-4000-8000-000000000001",
        "CatalogId": "tickytask-demo-urlaub",
        "ProcessId": "Process_TickyTaskVacation",
        "ApprovedRootEndId": "End_Approved",
        "RejectedRootEndId": "End_Rejected",
        "BpmnSha256": "0000000000000000000000000000000000000000000000000000000000000000"
      }
    ]
  }
}
```

Die Beispiel-GUID und der Nullhash sind **keine** Livebindung. Vor einer tatsächlichen
Aktivierung werden die veröffentlichte GUID und der Hash des gespeicherten XMLs
unabhängig überprüft. Der Hash normalisiert weder Whitespace noch Zeilenenden und
stammt nicht ungeprüft aus dem historischen Metadatenfeld `Hash`.

Warum auch der Inhalt gebunden wird: Die Storage-Schnittstelle kann einen Binärinhalt
unter derselben GUID überschreiben. Eine GUID allein beweist daher keine physische
Unveränderlichkeit. Verändertes XML, fehlende Version oder eine mehrdeutige beziehungsweise
ungültige Zuordnung liefern kein positives Ergebnis. Es gibt keine Wildcards,
`latest`-Auflösung, Clientzuordnung oder neue Verwaltungsmaske.

Der unterstützte HTTP-Speicherpfad erzeugt für jede gespeicherte Definitionsversion
eine neue GUID. Die Prüfung des aktuellen Binärinhalts ist trotzdem keine signierte
Attestierung seiner gesamten Vergangenheit. Der Projektor vertraut dem verbindlich
gespeicherten Instanzdokument; er schützt nicht gegen beliebige direkte Manipulationen
der Ablage und behauptet keine vollständige historische Modellrekonstruktion.

Bei einer Listenprojektion wird der Definitions-/XML-Beleg ausschließlich innerhalb
dieses einen Aufrufs je Definitions-GUID wiederverwendet. Mastermodell, Abschlusszustand
und Tokengraph werden weiterhin für jede einzelne Instanz geprüft. Es gibt keinen
aufrufübergreifenden Cache, der einen veränderten Binärinhalt verdecken könnte;
technische Ablagefehler folgen weiterhin dem vorhandenen API-Fehlervertrag.

## Was ein positives Ergebnis belegt

Ein positives Ergebnis verlangt gemeinsam:

- eine tatsächlich abgeschlossene gespeicherte Instanz mit technischem `Completed`;
- einen konsistenten abgeschlossenen Master des gebundenen Root-Prozesses;
- keine persönliche Withdrawal, Migration oder Betriebsmodifikation;
- die passende konkrete Definition samt unverändertem, freigegebenem XML;
- reguläre **None-Endevents** unmittelbar im Root-Prozess, nicht Fehler-, Terminate-,
  Nachrichten- oder Eskalationsenden und nicht interne Subprozessenden;
- genau einen tatsächlich abgeschlossenen, unmittelbar am Master hängenden erlaubten
  Root-Endtoken und keine widersprüchliche oder noch lebende Rootspur.

Der vollständige Tokenbestand und der Instanzzustand werden im selben Instanzdokument
gespeichert. Reguläre Endtoken bleiben nach dem Abschluss dauerhaft erhalten. Der
Projektor nutzt diesen vorhandenen verbindlichen Zustand **serverintern**, nicht
eine behauptete vollständige Ereignishistorie. Ein zusätzliches Runtime-Ledger oder
eine Datenbankmigration wird dafür nicht eingeführt.

Die äußere gespeicherte `InstanceId` bleibt die Grundlage der Objektberechtigung.
Die Engine vergibt daneben eine eigene interne `ProcessInstanceId` für den Master
und dessen Token; diese Kennung ist nicht mit der äußeren Instanz-ID identisch.
Der Projektor verlangt deshalb einen nichtleeren Master-Scope, denselben Scope für
sämtliche Token sowie eine konsistente Elternstruktur. Eine fremde Tokenkennung
oder eine verwaiste beziehungsweise zyklische Spur ist kein Ergebnisbeleg. Die
bestehenden Engine-Kennungen werden dafür nicht verändert.

Die allgemeinen Runtime-Ereignisse sind nur Momentaufnahmen an Persistenzgrenzen;
kurzlebige Zwischenzustände dürfen daraus nicht erfunden werden. Sie sind nicht die
Quelle dieser Ergebnisprojektion. Fehlende oder widersprüchliche Belege bleiben
`Unknown`, auch bei historisch abgeschlossenen Instanzen.

## TT-Demo-Urlaubsantrag und bewusste Grenzen

Das **separate** aktuelle Modell unter `examples/tickytask-urlaub` hat Root-Enden
`End_Approved` und `End_Rejected`. Die interne Prüfrunde besitzt eigene Enden;
ihre historischen Stimmen und Rückgaben sind kein Gesamtergebnis. Nach Korrektur
gelten nur die neu gestarteten Prüfungen. Eine Ablehnung darf die übrigen internen
Prüfungen zurückziehen, ohne dadurch als persönlicher Vorgangsrückzug zu gelten.

Das ältere Beispiel unter `examples/urlaubsantrag` verwendet andere Knoten und
Terminate-Semantik. Dessen Kennungen werden nicht auf das TT-Demo geraten.

Die Projektion ist zunächst auf explizit geprüfte reguläre Root-Enden begrenzt. Sie
behauptet keine allgemeine fachliche Ergebnisinterpretation beliebiger BPMN-Modelle,
Kompensation oder Urlaubsbuchung. Der TT-Timer bleibt ein separates Folge-Issue.

Source-Verifikation und synthetische lokale Tests sind keine PostgreSQL-, Keycloak-,
HTTPS-, Browser- oder Demo-Abnahme. Livekonfiguration und Rollout bleiben eigene,
koordinierte Gates; diese Dokumentation aktiviert oder importiert nichts.
