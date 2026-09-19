# Sicherheitsrichtlinie (Security Policy)

## Unterstützte Versionen

Flowzer BPMN Core Engine hat zwei langlebige Branches:

- **`release`** – der jeweils produktiv ausgerollte Stand. Nur dieser Stand wird aktiv mit
  Sicherheitsfixes versorgt.
- **`main`** – der laufende Entwicklungsstand. Fixes landen zuerst hier und gehen per Pull
  Request nach `release`.

Ältere, aus `release` verdrängte Stände (frühere Tags/Commits) werden **nicht** rückwirkend
gepatcht. Es gibt aktuell keine parallel gepflegten Versionslinien (kein LTS-Zweig); wer
Flowzer selbst betreibt, sollte auf dem aktuellen `release`-Stand bleiben, um
Sicherheitsfixes zeitnah zu erhalten.

| Stand | Unterstützt |
|---|---|
| `release` (aktueller Produktivstand) | ja |
| `main` (Entwicklung) | ja, im Rahmen der laufenden Weiterentwicklung |
| ältere Tags/Commits | nein |

## Eine Sicherheitslücke melden

Bitte meldet Sicherheitslücken **nicht** über ein öffentliches GitHub-Issue oder eine
öffentliche Diskussion, solange sie nicht behoben sind.

Bevorzugter Weg – **GitHub Private Vulnerability Reporting**: Im Repository
[`flowzer-io/flowzer-bpmn-core-engine`](https://github.com/flowzer-io/flowzer-bpmn-core-engine)
unter dem Reiter **„Security“ → „Report a vulnerability“** lässt sich ein privater
Sicherheitsbericht anlegen, der ausschließlich für die Maintainer und die von ihnen
hinzugezogenen Personen sichtbar ist. Voraussetzung ist, dass „Private vulnerability
reporting“ für dieses Repository in den GitHub-Einstellungen aktiviert ist; die Maintainer
sind dafür verantwortlich, das einzuschalten und diesen Abschnitt zu aktualisieren, falls
sich der Weg ändert.

Falls dieser Weg im Einzelfall nicht erreichbar ist oder eine direkte Kontaktaufnahme nötig
ist, wendet euch an: **`<security-kontakt>`** *(Platzhalter – wird von den Maintainern mit
einer tatsächlich erreichbaren Kontaktadresse gefüllt, sobald eine feststeht)*.

### Was in eine Meldung gehört

Bitte so konkret wie möglich, idealerweise mit:

- betroffene Version/Commit bzw. betroffener Branch (`main` oder `release`) und Komponente
  (z. B. `src/WebApiEngine`, `src/FlowzerConsole`, ein bestimmter BPMN-Ausführungspfad),
- Art der Schwachstelle (z. B. Rechteausweitung, Authentifizierungsumgehung, Injection,
  Offenlegung von Daten),
- Schritte zur Reproduktion bzw. ein minimales Beispiel (BPMN-Datei, API-Aufruf,
  Konfiguration),
- eingeschätzte Auswirkung (was kann ein Angreifer erreichen, welche Voraussetzungen
  braucht der Angriff),
- nach Möglichkeit ein Vorschlag für einen Fix oder ein Workaround.

Ein Proof of Concept ist hilfreich, aber kein Muss für eine erste Meldung.

### Reaktionszeiten (Absichtserklärung, keine vertragliche Zusage)

Dies ist ein von Einzelpersonen betreutes Open-Source-Projekt ohne Sicherheits-Team im
Hintergrund. Angestrebt wird:

- **Eingangsbestätigung** innerhalb von **5 Werktagen**.
- **Erste Einschätzung** (Einstufung Schweregrad, betroffene Versionen, grobe
  Zeitplanung) innerhalb von **14 Tagen** nach Eingangsbestätigung.
- **Fix oder Mitigation**: Zeitrahmen richtet sich nach Schweregrad und Komplexität;
  kritische, aktiv ausnutzbare Lücken haben Priorität vor allen anderen Arbeiten am
  Repository.

Diese Fristen sind eine Absicht, keine garantierte SLA. Bei Verzögerung informieren die
Maintainer die meldende Person über den Stand.

### Umgang mit Offenlegung (Disclosure)

- Es wird **Coordinated Disclosure** angestrebt: Details werden erst veröffentlicht, wenn
  ein Fix verfügbar ist (oder nach gemeinsamer Abstimmung mit der meldenden Person, falls
  kein Fix in angemessener Zeit möglich ist).
- Nach einem Fix landet ein kurzer Hinweis in den Release-Hinweisen bzw. im Changelog des
  betroffenen Release; sensible Details (die einen Angriff auf noch nicht aktualisierte
  Installationen erleichtern würden) werden dabei zurückhaltend formuliert.
- Meldende Personen werden auf Wunsch in der Danksagung/den Release-Hinweisen genannt.
- Ein CVE wird bei Bedarf über den GitHub-Security-Advisory-Workflow des Repositories
  beantragt.

## Bekannter Stand zu Abhängigkeiten

Ein Überblick über die direkten Abhängigkeiten und ihre Lizenzen steht in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Bekannte Schwachstellen in Abhängigkeiten
werden in der CI geprüft (`dotnet restore` mit NuGet-Audit sowie `npm audit` für die
JavaScript/TypeScript-Pakete, siehe [`.github/workflows/ci.yml`](.github/workflows/ci.yml)).

---

## In English (short version)

This project has two long-lived branches: `release` (the currently deployed state, actively
supported) and `main` (ongoing development). Older tags/commits are not patched
retroactively. To report a vulnerability, please use **GitHub Private Vulnerability
Reporting** on this repository (**Security → Report a vulnerability**) rather than a public
issue. If that is not reachable, contact `<security-kontakt>` (placeholder — to be filled in
by the maintainers with a real contact address). Please include affected version/branch,
vulnerability type, reproduction steps, and impact. We aim to acknowledge reports within 5
business days and give an initial assessment within 14 days; these are intentions, not a
contractual SLA. We aim for coordinated disclosure and will credit reporters on request.
