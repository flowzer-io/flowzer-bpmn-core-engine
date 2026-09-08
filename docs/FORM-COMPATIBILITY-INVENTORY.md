# Formular-Kompatibilitätsinventar

M2-Teilpaket #212 / PR #213. Das Inventar macht vor einem Upgrade oder einer erneuten
Veröffentlichung sichtbar, welche vorhandenen Formularfassungen nicht vom aktuellen
serverseitigen Vertrag unterstützt werden.

## API und Rechte

`GET /form/compatibility` ist ausschließlich mit der Modelliererrolle erreichbar.
Der optionale Query-Parameter `needsMigration` filtert serverseitig:

- ohne Wert: alle geprüften Fassungen,
- `true`: nur inkompatible Fassungen,
- `false`: nur kompatible Fassungen.

Jede veröffentlichte Version und der gegebenenfalls vorhandene gemeinsame
Autorenentwurf werden getrennt geprüft. Ein fehlerhaftes Schema verhindert nicht die
Bewertung anderer Fassungen. Die Antwort enthält nur Formular-ID und -Name, Quelle,
Versions- beziehungsweise Draft-Referenz, erkanntes Prüfprofil sowie einen stabilen
Fehlercode. Schema, Custom-JavaScript, eingesandte Daten und interne
Compiler-Nachrichten verlassen den Server nicht.

## Oberfläche

Die Formularpflege zeigt betroffene Formulare mit „Migration“ an und kann den Katalog
darauf filtern. Im Detail werden Quelle und sichere, lokal übersetzte Fehlercodes
aufgeführt. Unbekannte oder manipulierte Codes werden nicht als HTML oder freier
Servertext dargestellt.

Der Bericht verändert keine Daten. Insbesondere migriert er weder Custom-Regeln noch
laufende Instanzen und veröffentlicht keinen Autorenentwurf. Eine modellierende Person
muss die jeweilige Fassung fachlich in deklarative Regeln oder eine benannte
serverseitige Berechnung überführen und anschließend bewusst veröffentlichen.

## Betrieb und Grenzen

Das Inventar sollte vor einem Upgrade, nach einer Erweiterung des Prüfprofils und vor
der Wiederveröffentlichung von Altbeständen ausgeführt werden. Ein kompatibler Bericht
belegt nur die Unterstützung durch den aktuellen Formularcompiler, nicht die fachliche
Richtigkeit eines Formulars.

Fehler beim Lesen der Ablage bleiben Betriebsfehler des gesamten Aufrufs; sie werden
nicht als vermeintlicher Schemafehler kaschiert. Erst nachdem eine Fassung erfolgreich
geladen wurde, wird ihre Compilerbewertung von den übrigen Fassungen isoliert.
