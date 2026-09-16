# Formularpflege: einfaches Gestalten und verlässliche Dynamik

## Befund und Referenz (17.09.2026, #306)

Die lokale Oberfläche wurde im Browser betrachtet; als Bedienreferenz zusätzlich die
[offizielle form-js-Demo](https://demo.bpmn.io/form/new), die Camundas Formularwerkzeug
zeigt. Kein Wechsel des Formularformats oder der Renderbibliothek ist geplant.

1. **Formular öffnen – unnötiger Modusbegriff:** „Vorschau/Felder“ bezeichnete zwei
   Reiter, obwohl Nutzende ein Formular ansehen und bewusst bearbeiten wollen.
   Standard bleibt das interaktive Formular, der Einstieg heißt „Bearbeiten“.
2. **Subformular einsetzen – störender Sonderweg:** Eine große Bibliotheksleiste
   stand oberhalb der eigentlichen Palette. Die wiederverwendbare Komponente gehört
   als „Subformular“ in dieselbe Palette; Formular und feste Version werden dort
   konfiguriert. Referenzen bleiben unverändert, keine Kopie von Quellaktionen.
3. **Abschluss konfigurieren – technische Begriffe zuerst:** Stabile ID, Zielfeld,
   Werttyp und fester Wert waren sofort sichtbar. Zuerst stehen jetzt die sichtbaren
   Abschlussknöpfe und ihre Bedeutung, Details darunter eingeklappt.
4. **Camunda-Referenz – geeignetes Bedienmuster:** Palette links, Formularfläche,
   kontextbezogene Eigenschaften mit kleinen Gruppen; Vorschau getrennt von der
   Gestaltung. Die Demo zeigt zusätzlich technische Ein-/Ausgabepanels, die Flowzer
   nicht standardmäßig in die Formularpflege übernehmen sollte.

Die Browserprüfung belegt Layout und sichtbare Interaktionen, keine vollständige
Screenreader- oder Konformitätsprüfung. Einfügen per Klick und Tastatur, volle
Editorbreite und sichtbare mobile Komponentenwerkzeuge sind Teil von #306.
Die durchgängig barrierefreie Eigenschaftenpflege bleibt ein eigener Prüfpunkt.

## Zielbild – drei getrennte Dinge

### Bereiche und Subformulare

Ein **Bereich** gruppiert Felder desselben Formulars. Ein **Subformular** verwendet
eine veröffentlichte Formularversion aus der gemeinsamen Bibliothek wieder. Beides
ist kein eigener Prozessschritt und erzeugt keine zusätzliche Aufgabe.

Der heutige Vertrag prüft einfache Bedingungen auch an Layoutbereichen:
„Bereich anzeigen, wenn Feld X gleich Wert Y“. Einfache Feldvergleiche werden auf
Client und Server ausgewertet; beliebiges JavaScript ist kein unterstützter Ersatz.
Für eine bessere Regeloberfläche sind Feld-/Wertauswahl und lesbare Regelsätze
vorzuziehen. Nicht unterstützte Regeloptionen dürfen nicht als funktionierend
angeboten werden.

### Formularseiten und Navigation (eigenes Folgepaket)

Für größere Formulare: „Einseitig“ oder „Mehrere Schritte“. Seiten besitzen stabile
IDs und Titel. Zunächst genügt eine geordnete Seitenliste mit Bedingungen:

- „Allgemeine Angaben“ immer anzeigen.
- „Vertretung“ nur bei Abwesenheitsart „Urlaub“.
- „Reisekosten“ nur bei „Dienstreise“.
- Danach „Zusammenfassung“.

**Empfohlene erste Ausbaustufe:** „Weiter“ wählt die nächste sichtbare Seite;
„Zurück“ die tatsächlich besuchte vorherige sichtbare Seite. Das erreicht die
gewünschte Verzweigung ohne einen zweiten Prozesseditor im Formular.
Explizite Sprünge zu beliebigen Seiten erst später, falls ein konkreter Fall nicht
mit bedingten Seiten darstellbar ist. Dann müssen Zyklen, unerreichbare Seiten,
fehlende Ziele und widersprüchliche Regeln vor Veröffentlichung erklärt werden.

[Form.io unterstützt Wizard-Darstellung und bedingte Seiten](https://help.form.io/userguide/forms/form-types).
Das allein ist keine Flowzer-Produktfreigabe: unser Vertrag, die serverseitige
Validierung, Entwürfe, Wiederaufnahme und Start-/Aufgaben-Hosts müssen dieselbe
Semantik unterstützen. Kein ungeprüftes Aktivieren von `nextPage`-JavaScript.

Verbindliche Semantik für das Folgepaket:

- Werte ausgeblendeter/übersprungener Bereiche dürfen weder Pflichtfehler erzeugen
  noch unbemerkt als wirksame Ergebnisse gesendet werden. Client und Server prüfen
  dies anhand derselben finalen Eingaben.
- Ändert man eine frühere Antwort, wird der erreichbare Pfad neu berechnet. Verdeckte
  Eingaben können lokal für eine Rückkehr gepuffert werden; sie sind keine autorisierten
  Prozessausgaben.
- Aktuelle Seite ist Navigationszustand, keine Berechtigung. Direkte API-Abgaben müssen
  alle aktiven Pflichtfelder erfüllen, auch wenn eine Seite nicht besucht wurde.
- Entwürfe bleiben an Formularversion/Revision gebunden; laufende Aufgaben ändern sich
  nicht durch eine spätere Veröffentlichung.
- Vorschau zeigt denselben Pfad wie die echte Bearbeitung. Kein vorgetäuschter fester
  Fortschritt „x von y“, solange Antworten noch weitere Seiten freischalten können.

### Abschlussknöpfe

„Genehmigen“, „Ablehnen“ oder „Einreichen“ **beenden eine Human Task** und liefern ein
fachliches Ergebnis an den Prozess. Sie sind keine Seitennavigation. Die publizierte
Aktion bindet feste Ergebniswerte; weder Klicktext noch Browserpayload verleihen
Berechtigungen. Der technische Vertrag `flowzer.actions` bleibt bestehen.

Sinnvoller nächster Vereinfachungsschritt: Vorlagen mit ausdrücklich erzeugtem,
kollisionsfreiem Ergebnisfeld und einer Feld-/Wertauswahl statt freier Schlüssel.
Kein vorhandenes Fachfeld wird ohne Zustimmung ersetzt oder umtypisiert.

## Reihenfolge

1. Kleines kompatibles UI-Paket (#306): Bearbeiten-Einstieg, Subformular-Palette,
   verständliche Abschlussknöpfe, Tastatur-/Touch-Einfügen, volle Editorbreite und
   Browserregressionen.
2. Folgepaket [#307](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/307):
   einheitliche, kontextbezogene Eigenschaftenpflege nach dem Camunda-Bedienmuster:
   Allgemein, Auswahl, Regeln, Darstellung, Erweitert; deutsche Begriffe und nur
   nachweislich unterstützte Optionen.
3. Deklarative bedingte Bereiche und Seiten, gemeinsame Client-/Server-Testfälle,
   Startformular-/Aufgaben-/Entwurfs-Integration und manipulierte API-Abgaben.

Kein Rewrite, keine automatisch umgedeuteten Formulare und keine Migrationsansicht
für reine Bedienverbesserungen.
