# UI-/UX-Audit und Rundumschlag – April 2026

## Ausgangslage

Das Frontend war funktional nutzbar, fühlte sich aber in mehreren Kernflächen technisch und sperrig an:

- Aktionen waren teils nur über **Titel-Links** erreichbar
- Listen- und Detailseiten nutzten **uneinheitliche Hierarchien**
- Empty-, Error- und Loading-States waren nicht durchgängig hilfreich
- die Runtime-Ansichten zeigten Daten, aber nicht immer in einer **gut lesbaren Bedienreihenfolge**
- die App-Shell wirkte eher wie ein internes Test-Frontend als wie ein belastbares Produkt

## Leitprinzipien für die Überarbeitung

1. **Explizite Handlungen statt impliziter Klickziele**  
   Öffnen, Starten, Bearbeiten und Retry sollen als sichtbare Aktionen erkennbar sein.

2. **Konsistente Informationshierarchie**  
   Jede Kernseite sollte oben denselben Rhythmus haben:
   - Intro / Kontext
   - wichtige Kennzahlen
   - Toolbar / Filter / Suche
   - eigentliche Arbeitsfläche

3. **Produktive UX statt Debug-UX**  
   Bedienung soll an Workflow-Autoren und Operatoren ausgerichtet sein, nicht an interne Code-Strukturen.

4. **Runtime verständlich machen**  
   Die Instanzansicht soll nicht nur Daten anzeigen, sondern den aktuellen Zustand wirklich lesbar machen.

5. **Wartbarkeit mitdenken**  
   Wiederkehrende Darstellungslogik wurde in kleine Helper verschoben, damit die UI-Verbesserungen nicht nur optisch, sondern auch strukturell tragen.

## Umgesetzte Änderungen

## 1. App-Shell und Navigation

- modernisierte Sidebar mit stärkerer Flowzer-Identität
- klarere Primärnavigation mit separatem Einstieg in die tägliche Task-Arbeit
- user-facing Bezeichnung **Workflows** statt rein technischem **Models** in der Hauptnavigation
- stabilere Workspace-Struktur statt sperrigem Splitter-Gefühl

## 2. Dashboard / Home

- klareres Intro mit direkten Einstiegsaktionen
- Summary-Cards für offene Aufgaben, Workflows, aktive Instanzen und Formulare
- Summary-Cards zusätzlich als **direkte Navigationskacheln** für Kernbereiche nutzbar
- offene Aufgaben verweisen jetzt konsequent auf die dedizierte Task-Inbox, statt als versteckter Anker innerhalb des Dashboards zu wirken
- Fehlerinstanzen als eigener sichtbarer Einstieg in die Runtime ergänzt
- offenere Task-Karten statt rein technischer Listenwirkung
- nach abgeschlossener User-Task wird die Karte direkt aus der Übersicht entfernt

## 3. Task-Inbox

- eigene Seite **My tasks** für die operative Tagesarbeit ergänzt
- offene User Tasks und startbare Workflows sind getrennte, klar benannte Arbeitsbereiche
- Dashboard bleibt Übersicht und Einstieg, die eigentliche Aufgabenbearbeitung hat aber einen fokussierten Ort
- Refresh- und Start-Aktionen sind explizit sichtbar

## 4. Workflow-Katalog

- Seite fachlich klarer als **Workflow-Katalog** inszeniert
- explizite Aktionen pro Workflow:
  - **Open latest**
  - **Open deployed**
  - **Start instance**
- Version-/Lifecycle-Kontext sichtbarer
- konsistentere Search-/Sort-Toolbar
- expliziter **Clear search**-Pfad und kontextabhängige Empty States für Treffer- vs. Leer-Katalog-Fälle

## 5. Formular-Katalog

- explizite **Open form**-Aktionen statt versteckter Zeileninteraktion
- bessere Such- und Ergebnisrückmeldung
- klarere Empty States
- expliziter **Clear search**-Pfad bei aktiver Filterung

## 6. Instanzliste

- Filter-Navigation bleibt prominent in der Sidebar
- zusätzliche **Suche nach Workflowname, Workflow-Key oder Instanz-ID**
- klarere Runtime-Chips für Tokens, Messages, Signals, User Tasks und Services
- explizite **Open instance**-Aktion pro Zeile
- kontextabhängige Empty States und expliziter **Clear search**-Pfad

## 7. Instanzdetailansicht

- deutlich stärkere Kopfzeile mit Status, Workflow-Referenz und Aktionen
- direkte Aktionen:
  - **Open workflow**
  - **Refresh runtime**
- Summary-Cards für Tokens und wartende Subscriptions
- Diagramm als eigene Hauptfläche
- Token-Inspektion jetzt mit **getrennten Tabs für Variables und Output**
- Subscription-Panel klarer strukturiert; wartende Messages lassen sich gezielt auslösen
- Fehlerzustände getrennt nach **Headline + Detail**, damit Diagramm- und Ladefehler nicht mehr doppelt oder missverständlich erscheinen

## 8. Workflow-Editor

- bessere Kontextdarstellung für Workflow-Key, Version, Status und zuletzt gespeicherten Stand
- verständlichere Aktionen:
  - **Save draft**
  - **Deploy current version**
  - **Start instance**
- Diagramm und Properties-Panel bleiben als zusammengehörige Arbeitsfläche besser lesbar
- Workflow-Fehlermeldungen verwenden konsistent die Endnutzer-Sprache **Workflow** statt gemischter Model-/Workflow-Begriffe

## 9. Form-Editor

- ruhigere visuelle Rahmung des Builders
- konsistentere Feedback-Fläche für Save/Test-Aktionen
- Version und Metadaten sichtbarer im Kopfbereich

## Preview-Status

Eine echte gehostete Preview-CI/CD-Umgebung existiert aktuell noch nicht. Als pragmatische Zwischenstufe erzeugt der UI-Smoke-Job jetzt ein Artefakt `ui-design-preview` mit Desktop-Screenshots von Dashboard, Task-Inbox, Workflow-Katalog, Instanzen und Formularen sowie mobilen Screenshots der wichtigsten Einstiege. Damit lassen sich Design-Änderungen im PR visuell prüfen, ohne bereits Hosting-Infrastruktur vorauszusetzen.

## Strukturelle Codeverbesserungen

Zusätzlich zur Oberfläche wurden wiederverwendbare Helfer ergänzt bzw. ausgebaut:

- `FormListViewHelper`
- `InstanceListViewHelper`
- `ProcessInstanceStateViewHelper`
- `TokenSelectionViewHelper`

Dafür wurden passende Frontend-Unit-Tests ergänzt, damit UX-nahe Listen- und Darstellungslogik nicht nur visuell, sondern auch technisch stabil bleibt.

## Offene Folgeideen

Der Rundumschlag bringt die Kernpfade deutlich nach vorn, aber ein paar sinnvolle nächste Schritte bleiben:

1. **Unsaved-Changes-Guard** für Modeler und Form-Editor  
2. **Toast-/Notification-Strategie** statt einzelner Dialoge für Erfolgsmeldungen  
3. **noch klarere Domain-Sprache** (intern weiter `Models`, außen noch konsistenter `Workflows`)  
4. **Keyboard-/Power-User-Flows** für Autoren und Operatoren  
5. **Design-Tokens/Theme-Schicht** weiter systematisieren, damit spätere UI-Arbeit weniger ad hoc wird
6. **echte Preview-Umgebung** pro PR, sobald Hosting-Ziel, Secrets und Persistenzmodell geklärt sind

## Fazit

Das Frontend ist damit nicht „fertig“, aber deutlich näher an einem produktionsnahen Bediengefühl:

- klarere Navigationslogik
- explizitere Aktionen
- bessere Runtime-Lesbarkeit
- weniger versteckte Interaktionen
- konsistentere visuelle Sprache

Gerade für Demo-, Autoren- und Operatorenpfade ist der Unterschied erheblich.
