# Wiederverwendbare Formulare

Vier Formulare, die in fast jedem Prozess vorkommen. Statt für jede Freigabe ein eigenes
Formular zu bauen, benutzen alle Prozesse dieselben — und sagen im Modell, was die Antworten
in *ihrem* Zusammenhang bedeuten.

```bash
node examples/formulare-generisch/import.mjs http://localhost:5182
```

## Die vier

| Formular (= Form-Key) | Wofür | Antwortet mit |
| --- | --- | --- |
| `Freigabe` | jemand entscheidet ja/nein | `entscheidung` (`freigegeben`/`abgelehnt`), `begruendung` |
| `Prüfung` | jemand sieht nach und meldet einen Wert | `pruefungBestanden` (`ja`/`nein`), `pruefwert`, `pruefkommentar` |
| `Erledigung bestätigen` | jemand hat etwas getan und quittiert es | `erledigt`, `referenz`, `anmerkung` |
| `Kenntnisnahme` | jemand soll etwas lesen und bestätigen | `kenntnisGenommen`, `kenntnisAnmerkung` |

Jedes beginnt mit einem nicht änderbaren Feld `vorgang`. Es zeigt, worum es geht — sonst
stünde die entscheidende Person vor einem „Freigabe erteilt?" ohne zu wissen, wofür.

## Wie ein Prozess sie benutzt

Das Formular ist allgemein, die Bedeutung steht im Modell. Der Eingang füllt die Anzeige,
der Ausgang legt fest, unter welchen Namen die Antworten in den Prozess zurückgehen:

```xml
<bpmn:userTask id="Task_Fachlich" name="Urlaub fachlich entscheiden">
  <bpmn:extensionElements>
    <zeebe:formDefinition formKey="Freigabe" />
    <zeebe:assignmentDefinition candidateGroups="Vorgesetzte" />
    <zeebe:ioMapping>
      <zeebe:input  source="=vorgang"     target="vorgang" />
      <zeebe:output source="=entscheidung" target="fachlicheEntscheidung" />
      <zeebe:output source="=begruendung"  target="fachlicheBegruendung" />
    </zeebe:ioMapping>
  </bpmn:extensionElements>
</bpmn:userTask>
```

Danach prüft das Tor auf den Namen aus *diesem* Prozess:

```xml
<bpmn:conditionExpression xsi:type="bpmn:tFormalExpression">=fachlicheEntscheidung = "freigegeben"</bpmn:conditionExpression>
```

`source` ist ein Ausdruck und braucht das führende `=`; ohne das steht der Name selbst als
Festwert da.

## Drei Dinge, die man wissen muss

**Ohne Ausgangszuordnung kollidieren zwei Freigaben.** Beide schrieben `entscheidung`, die
zweite überschriebe die erste. Sind Ausgänge deklariert, geht *nur* das Deklarierte in den
Prozess — `entscheidung` selbst landet nirgends.

**Woher `vorgang` kommt.** Das erste Formular eines Prozesses baut den Satz zusammen; im
Urlaubsantrag rechnet ihn ein verstecktes Feld aus Name, Zeitraum und Vertretung aus. Die
Engine könnte das nicht: Zeichenketten zusammensetzen kann der einfache Ausdrucks-Handler
nicht, der in Produktion läuft.

**Die Beschriftungen bleiben allgemein.** „Ermittelter Wert" statt „Verbleibender
Urlaubsanspruch". Wo das nicht genügt, ist ein eigenes Formular richtig — so wie der
Urlaubsantrag selbst eines hat.
