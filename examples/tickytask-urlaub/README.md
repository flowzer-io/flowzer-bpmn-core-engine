# TT-Demo: Urlaubsantrag nur mit Human Tasks

Dieses **separate** Beispiel ersetzt den älteren Worker-/Fachsystemprozess unter
`examples/urlaubsantrag` nicht. Es benötigt weder Worker noch Mail, Kalender, KI,
Urlaubsbuchung oder Zeiterfassung. Außer einer lokalen konstanten DMN-Entscheidung
findet die gesamte Arbeit in menschlichen Aufgaben statt.

## Ablauf

1. Startformular: Zeitraum, beantragte Arbeitstage, Directory-Benutzer als Vertretung
   und Bemerkung. Die Antragstelleridentität stammt ausschließlich aus dem echten Start.
2. „Vorgesetzten ermitteln“ wertet die vorhandene DMN-Laufzeit aus. Eine einzige
   `UNIQUE`-Regel liefert die konfigurierte stabile Benutzerreferenz des Demo-Kontos `m2`.
3. Eine **eingebettete Prüfrunde** startet parallel Vorgesetzten-, Personalgruppen-
   und Vertretungsprüfung. Das Formular zeigt den Antrag unveränderbar und bietet
   **Genehmigen**, **Ablehnen** und **Zur Korrektur zurückgeben** als servergebundene Aktionen.
4. Drei aktuelle Zustimmungen führen zu „Urlaub genehmigt (Demo)“. Jede Ablehnung
   unterbricht sofort die ganze Prüfrunde und endet in „Urlaub abgelehnt (Demo)“.
5. Jede Rückgabe unterbricht ebenfalls die komplette Runde. Nur der tatsächliche
   Initiator erhält die Korrekturaufgabe. Nach deren Abschluss beginnen alle drei
   Prüfungen mit **neuen Task-IDs und einem neuen Rundenscope**. Alte Stimmen bleiben
   in Token-/Aufgabenhistorie, können aber keinen neuen Join erfüllen. Eine regulär
   genehmigte Runde exportiert nur `urlaubsentscheidung = "genehmigt"`; Einzelstimmen
   und Begründungen werden nicht implizit in den Root kopiert.
6. Der Antragsteller kann den laufenden Vorgang über den vorhandenen persönlichen
   Rückzug beenden. Das ist kein Operator-Abbruch und kompensiert keine anderen Systeme.

Für die Demo sind Selbstfreigabe und die eigene Person als Vertretung zulässig.
Anzahl der Arbeitstage und Urlaubskontingent werden **nicht** automatisch berechnet;
es gibt keine echte Personalverwaltung oder Urlaubsbuchung.

## Konfiguration und Veröffentlichung – kein automatischer Live-Import

Die Dateien sind bewusst **Templates ohne echte Benutzer-/Gruppenkennungen**:

| Datei | Bindung |
| --- | --- |
| `urlaubsantrag.bpmn` | `__DEMO_PERSONNEL_GROUP_ID__`: stabile Flowzer-Directory-ID der freigegebenen Personal-Untergruppe |
| `vorgesetzter.dmn` | `__DEMO_SUPERVISOR_USER_ID__`: stabile Flowzer-Directory-ID des verifizierten synthetischen Keycloak-Kontos `m2` |
| `formulare/antrag.json` | Formularname `TT Demo Urlaubsantrag` – Start |
| `formulare/korrektur.json` | Formularname `TT Demo Urlaubskorrektur` – Korrektur mit unveränderlicher Rückfrage |
| `formulare/entscheidung.json` | Formularname `TT Demo Urlaubsentscheidung` – alle drei Prüfungen |

Vor dem Ersetzen/Veröffentlichen müssen der gemeinsame Realm `TickyTask`, die
ausdrücklich freigegebene Demo-Wurzel samt Untergruppen, die Personalgruppe und
die unverwechselbare externe Issuer-/Subject-Identität von `m2` aktuell verifiziert
sein. **Nicht** Keycloak-UUIDs, Anzeigenamen, ausgedachte UUIDs, gleichnamige fremde
Gruppen oder Formularwerte als lokale Directory-ID verwenden. Die UUIDs werden im
publizierten Flowzer-Directory nachgesehen; kein neuer Grant wird daraus abgeleitet.

Danach zuerst die drei Formulare, dann die konfigurierte DMN-Datei, schließlich die
konfigurierte BPMN-Datei mit den bestehenden Modellierungswegen veröffentlichen.
Der Workflow-Katalogschlüssel ist `tickytask-demo-urlaub`. Die Workflow-Veröffentlichung
bindet Formularstände, Starts müssen die angezeigte Definitions-ID mitsenden.
Ein nicht ersetzter Gruppenplatzhalter verhindert die Veröffentlichung; ein ungültiges
DMN-Benutzerziel verhindert die Aufgabenerzeugung. Es gibt keinen Text-/Gruppenfallback.

Der vorhandene DMN-Katalog verwendet seine **jüngste** Decision-Version. Deshalb die
Demo-Decision nicht während laufender Anträge beliebig ändern; jede Korrekturrunde
ermittelt denselben verifizierten Demo-Vorgesetzten erneut. Versionsbindung des
DMN-Katalogs ist kein zusätzliches Feature dieses Beispiels.

Onlineinstallation, echte Keycloak-/HTTPS-Abnahme und TT-Demo-Rollout sind eigene Gates.
Diese Assets führen **keinen** Import, Grant, Deploy oder externen Effekt aus. Demo-Rollouts
und Live-Kontoproben erfolgen nur im bestätigten zentralen Koordinationsslot.

## Formulare, Entwürfe und Identität

- Pflichtwerte, `bis >= von`, positive Arbeitstage und die aktive einzelne
  Benutzerreferenz werden von der API geprüft. Ein freies Genehmigerfeld gibt es nicht.
- Die separate Korrekturform zeigt die Rückfrage der zurückgebenden Rolle unveränderlich.
  Nur diese ausdrücklich gemappte Rückfrage verlässt die unterbrochene Runde; frühere
  Zustimmungskommentare bleiben in der Historie. Jede erneute Rückgabe liefert den neuen Hinweis.
- Genehmigungsaktionen setzen die Entscheidung aus dem gebundenen Profil 4;
  widersprüchliche Browserwerte werden abgelehnt.
- Private Human-Task-Entwürfe benutzen das vorhandene manuelle Speichern. Auch eine
  unvollständige Korrektur kann gespeichert und wieder geöffnet werden; erst vollständiges
  Absenden setzt die Runde fort. Startformularentwürfe werden nicht ergänzt.
- Die Personalgruppe übernimmt atomar durch eine konkrete Person. Anzeigen, Entwurf
  und Abschluss bleiben danach exklusiv. Audit hält Issuer, Subject, echten Benutzer,
  Zeitpunkt und verifizierten TT-Vermittler fest, nicht eine behauptete Browseridentität.
- Persönliche Formularlinks und langlebige Einbettung folgen dem bestehenden
  [Einbettungsvertrag](../../docs/FORM-EMBED-LINKS.md). Die fünf Minuten betreffen nur
  die Linkeinlösung; dieses Beispiel führt keine neue Bearbeitungsfrist ein.

## Kleine Scope-Korrektur für die eingebettete Runde

Ein Subprozess mit ausdrücklich deklarierten Eingangsmappings liest aus seinem Parent
und führt einen geschlossenen lokalen Variablenscope. Aufgabenresultate und
Sequenzbedingungen dieser Runde benutzen den nächsten solchen Scope. Ein fehlendes
Feld wird dort nicht aus dem Root nachgeladen. Bei verschachtelten Abschlüssen darf
der historische Root-Alias ungemappter Subprozesse die geschlossene Projektion nicht
erweitern; explizite Subprozess-Ausgaben benutzen den aktuellen lokalen Ergebnisstand auch nach
einem persistierten Human-Task-Wartepunkt. Die Error-End-Eingangsprojektion transportiert
nur deklarierte Fehlerdaten; ohne Eingänge bleiben historische Fehler datenlos.

Historische **ungemappte** Modelle und der bestehende `GetProcessToken`-Instanz-Root
bleiben getrennt erhalten: Directory-Quellen, DMN, Aufruf-Aktivitäten und externe
Payloads erhalten nicht still eine andere Scope-Bedeutung. Die Benutzerreferenz der
Vertretung wird ausdrücklich als Task-Eingang gebunden. Normalisierte JSON-Dictionaries
werden beim FEEL-Roundtrip nach ihren Datenkeys, nicht ihren CLR-`Count`/`Keys`-Properties
umgesetzt; unerlaubte Zusatzfelder werden dabei niemals entfernt.

## Tests und verbleibende Abnahme

Die NUnit-Tests lesen **diese Originaldateien**, konfigurieren ausschließlich synthetische
stabile IDs und spielen den Prozess über signierte Test-HTTP-Identitäten und isolierte
Dateiablage durch. Geprüft werden alle sechs Genehmigungsreihenfolgen, jede Ablehnungs-
und Rückgaberolle, neue Runden/Task-IDs, historischer Audit, sichtbare und manipulationsgeschützte Rückfragen, Rückzug, unvollständiger
Entwurf, Feldfehler, Aktionsfälschung und exklusive konkurrierende Gruppenübernahme.
Core-Regressionsfälle decken lokale/nested Scopes, ausgeschlossene Root-Felder, MI-
Ausgabe und unveränderte Legacy-Mappings ab. Ein Moddle-Roundtrip prüft außerdem das
Originaldiagramm und seine vollständigen Diagrammelemente.

Das ist **keine** echte PostgreSQL-/Realm-/TT-/visuelle oder 45-Minuten-HTTPS-Abnahme.
Diese Betriebs-/Browsergates bleiben vor dem Demo-Livegang erforderlich. Der TT-Timer
bleibt ausschließlich im separaten Folge-Issue; es gibt hier keine Timer- oder Schemaänderung.
