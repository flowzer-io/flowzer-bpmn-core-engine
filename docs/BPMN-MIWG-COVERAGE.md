# BPMN-MIWG-Konformitätsnachweis

Flowzer belegte seine BPMN-Abdeckung bisher nur über den eigenen
[Fähigkeitsvertrag](BPMN-CAPABILITIES.md). Dieser Bericht stellt daneben eine fremde,
nicht von Flowzer gewählte Messlatte: die Referenzmodelle der
[BPMN Model Interchange Working Group](https://github.com/bpmn-miwg/bpmn-miwg-test-suite).

**Der Bericht ist ein Nachweis, keine Zusage.** Er hält fest, was Flowzer heute mit
diesen Modellen tut — einschließlich aller Ablehnungen und ihrer Gründe.

- **Stand:** 2026-09-19
- **Modellsatz:** `Reference/*.bpmn` der MIWG-Suite, Commit `121a0a5d6798233ee3ca09fba778e1ecc185ea14`
- **Lizenz der Suite:** Creative Commons Attribution 3.0 Unported (CC BY 3.0)
- **Herkunftsnachweis:** [`src/core-engine-tests/embeddings/miwg/MANIFEST.md`](../src/core-engine-tests/embeddings/miwg/MANIFEST.md)
- **Erzeugt aus:** [`src/core-engine-tests/miwg-expectations.json`](../src/core-engine-tests/miwg-expectations.json)
  über den Generatorlauf in `MiwgConformanceTest.RegenerateExpectationsAndReport`
- **Aktualisieren des Modellsatzes:** `scripts/ci/fetch-miwg-reference-models.sh`

## Zahlen

| Stufe | Ergebnis |
| --- | --- |
| Modelle im Satz | 22 |
| gelesen (Parser, mit ausführbarem Prozess) | 5 |
| gelesen, aber leer (kein `isExecutable="true"`) | 15 |
| veröffentlichbar (Fähigkeitsvertrag) | 4 |
| davon mit Warnung veröffentlichbar | 3 |
| ausgeführt bis Ende | 1 |

„gelesen, aber leer" ist bewusst getrennt: Der Parser liest ausschließlich Prozesse mit
`isExecutable="true"`. Ein Modell ohne solchen Prozess läuft ohne Ausnahme durch, ohne dass
ein einziges Element gelesen worden wäre. Das ist keine Abdeckung.

## Die drei Stufen

| Stufe | Was geprüft wird | Mögliche Werte |
| --- | --- | --- |
| Lesen | `ModelParser.ParseModel` ohne Ausnahme | `ok`, `empty`, `rejected` |
| Veröffentlichen | `BpmnCapabilityMatrix.ValidateForDeployment` | `ok` (ggf. mit Warnungen), `rejected` |
| Ausführen | Instanz starten und wartende Elemente generisch bedienen | `completed`, `stuck`, `exception`, `step_limit`, `skipped` |

Die Ausführungsstufe läuft nur, wenn die Veröffentlichung zusagt und das Modell einen
ausführbaren Prozess mit reinem Startereignis hat. Wartende Elemente werden generisch
bedient: User- und Worker-Tasks abgeschlossen, Nachrichten und Signale gesendet, die Zeit
für Timer vorgestellt — höchstens 200 Schritte.

## Ergebnis je Modell

| Modell | Lesen | Grund | Veröffentlichen | Grund | Ausführen |
| --- | --- | --- | --- | --- | --- |
| `A.1.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `A.2.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `A.2.1.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `A.3.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `A.4.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `A.4.1.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `B.1.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `B.2.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `C.1.0.bpmn` | `rejected` | `parser.ModelValidationException` — `Implementation not defined for Service task 'archiveInvoice'` | `rejected` | `bpmn.service_task.implementation_required` an `serviceTask` | übersprungen (deployment rejected) |
| `C.1.1.bpmn` | `rejected` | `parser.ModelValidationException` — `Implementation not defined for Service task 'archiveInvoice'` | `rejected` | `bpmn.service_task.implementation_required` an `serviceTask` | übersprungen (deployment rejected) |
| `C.2.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `C.3.0.bpmn` | `ok` | 1 ausführbare(r) Prozess(e) | `rejected` | `bpmn.exclusive_gateway.condition_required` an `sequenceFlow` | übersprungen (deployment rejected) |
| `C.4.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `C.5.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `C.6.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `C.7.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `C.8.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `C.8.1.bpmn` | `ok` | 1 ausführbare(r) Prozess(e) | `ok` | — | `exception` — NotSupportedException: getting values of object arrays is not implemented yet. |
| `C.9.0.bpmn` | `ok` | 1 ausführbare(r) Prozess(e) | `ok` | Warnung: `bpmn.user_task.form_missing` | `stuck` — no generic action for BusinessRuleTask 'BusinessRuleTask_CheckApplicationAutomatically' |
| `C.9.1.bpmn` | `ok` | 1 ausführbare(r) Prozess(e) | `ok` | Warnung: `bpmn.user_task.form_missing` | `completed` |
| `C.9.2.bpmn` | `ok` | 1 ausführbare(r) Prozess(e) | `ok` | Warnung: `bpmn.user_task.form_missing` | `exception` — KeyNotFoundException: The specified key 'applicant' does not exist in the ExpandoObject. |
| `C.10.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |

## Häufigste Ablehnungsgründe

### Lesen (Parser)

| Grund | Anzahl | Modelle | Deutung |
| --- | --- | --- | --- |
| `parser.ModelValidationException` | 2 | `C.1.0.bpmn`, `C.1.1.bpmn` | Ein benannter Modellfehler des Parsers: fehlende Kennung, Verweis ins Leere oder eine unvollständige Pflichtangabe. **Modellfehler, keine Fähigkeitslücke.** |

> Reines Diagramm-Beiwerk überliest der Parser (siehe
> [BPMN-CAPABILITIES.md](BPMN-CAPABILITIES.md), „Was überlesen wird"). An einem Element
> mit Ausführungssemantik, das er nicht kennt, bricht er dagegen ab. Gemeldet wird
> deshalb immer nur die **erste** Hürde eines Modells — hinter ihr können weitere liegen.

### Veröffentlichen (Fähigkeitsvertrag)

| Fehlercode | Elementart | Anzahl | Modelle | Deutung |
| --- | --- | --- | --- | --- |
| `bpmn.process.executable_required` | `definitions` | 15 | `A.1.0.bpmn`, `A.2.0.bpmn`, `A.2.1.bpmn`, `A.3.0.bpmn`, `A.4.0.bpmn`, `A.4.1.bpmn`, `B.1.0.bpmn`, `B.2.0.bpmn`, `C.2.0.bpmn`, `C.4.0.bpmn`, `C.5.0.bpmn`, `C.6.0.bpmn`, `C.7.0.bpmn`, `C.8.0.bpmn`, `C.10.0.bpmn` | Das Dokument enthält keinen Prozess mit `isExecutable="true"`. Die MIWG-Reihen A und B sind reine Modellierungs- und Layoutbeispiele. **Kein Befund über die Engine.** |
| `bpmn.service_task.implementation_required` | `serviceTask` | 2 | `C.1.0.bpmn`, `C.1.1.bpmn` | — |
| `bpmn.exclusive_gateway.condition_required` | `sequenceFlow` | 1 | `C.3.0.bpmn` | — |

> Auch die Veröffentlichungsprüfung meldet bewusst den **ersten** Fehler in
> Dokumentreihenfolge (siehe [BPMN-CAPABILITIES.md](BPMN-CAPABILITIES.md)). Die Zahlen sind
> also eine Rangfolge der ersten Hürde, keine vollständige Lückenzählung.

## Was das für die nächsten Pakete heißt

Die Befunde zerfallen in drei Gruppen, die nicht vermischt werden dürfen. Nur die dritte
ist eine Ausführungslücke.

### 1. Das Modell will gar nicht ausgeführt werden (15 Modelle)

Die MIWG-Reihen A und B prüfen Layout und Elementabdeckung von Modellierungswerkzeugen.
Ihre Prozesse tragen kein `isExecutable="true"`. Dass Flowzer sie nicht veröffentlicht,
sagt **nichts** über die Engine — eine Ausführungsengine darf solche Modelle ablehnen.
Diese Modelle gehören in einen Import- oder Anzeigenachweis, nicht in die Ausführungsbilanz.

Bei 15 davon läuft auch der Parser ohne Ausnahme durch — er liest dann schlicht nichts.
Keines scheitert zusätzlich schon beim Lesen.

### 2. Lese- und Prüfhürden vor der eigentlichen Semantik

Diese Fälle scheitern, **bevor** irgendeine Ausführungsfrage gestellt wird:

- **User-Tasks brauchen ein `zeebe:formDefinition`.** Das ist eine bewusste
  Produktentscheidung von Flowzer und keine BPMN-Lücke; werkzeugneutrale Modelle tragen
  diese Erweiterung nie. Sie ist in diesem Satz die verbliebene Lesehürde und steht vor
  jeder Aussage darüber, welche BPMN-Semantik Flowzer wirklich fehlt.

Drei frühere Hürden sind gefallen und stehen deshalb nicht mehr in dieser Liste:

- **Diagramm-Beiwerk wird überlesen.** `laneSet`, `lane`, `textAnnotation`, `association`,
  Datenobjekte, `ioSpecification` und Verwandtes tragen keine Ausführungssemantik. Parser
  und Veröffentlichungsprüfung gehen seither über dieselbe Liste hinweg, statt zu werfen.
  Überlesen heißt ausdrücklich **nicht** ausgeführt: Lanes weisen keine Arbeit zu.
- **`bpmn:message` ohne `name` wird gelesen.** Der Name ist in BPMN optional; die frühere
  `NullReferenceException` war ein Robustheitsmangel. Ein Element, das auf eine namenlose
  Nachricht zeigt, beanstandet nun die Veröffentlichungsprüfung mit
  `bpmn.message.name_required` am Knoten — dort, wo es die Konsole markieren kann.
- **Unbekannte Elemente heißen beim Namen.** Statt einer `NotSupportedException` mit rohem
  XML-Namen wirft der Parser eine `ModelValidationException` mit Elementart und Kennung.
  Bei einem Deployment sieht man sie ohnehin nicht: Dort läuft die Veröffentlichungs-
  prüfung vor dem Parser und meldet `bpmn.element.unsupported` am betroffenen Knoten.

### 3. Echte Ausführungslücken

Im aktuellen Satz erreicht kein Modell diese Stufe: Die erste Hürde ist überall eine
andere. Die Liste füllt sich, sobald die Hürden aus Gruppe 2 fallen.

Weil jede Stufe beim ersten Fehler abbricht, ist diese Tabelle **keine** vollständige
Lückenliste. Vom Vertrag v7 ausdrücklich nicht zugesagt sind darüber hinaus: Event-based
Gateway, Inclusive Gateway, Complex Gateway, Event-Subprozess, Escalation, Kompensation,
Signal-Throw und Signal-Ende, Script-Tasks, Transaktionen, Lanes und Pools sowie Datenobjekte
und Datenflüsse (siehe [BPMN-CAPABILITIES.md](BPMN-CAPABILITIES.md)).

## Grenzen dieses Nachweises

- Geprüft wird **Import**, nicht der MIWG-Export- oder Roundtrip-Teil. Flowzer schreibt
  kein BPMN zurück und tritt deshalb nicht als MIWG-Werkzeug an.
- Die Ausführungsstufe bedient generisch und ohne Fachdaten. Ein Modell, das eine
  bestimmte Variable braucht, kann daher hängen bleiben, obwohl die Engine es grundsätzlich
  könnte. Solche Fälle stehen als `stuck` mit dem Knoten, an dem es endete.
- Jede Stufe meldet den ersten Fehler. Die Zahlen sind eine Rangfolge, keine Gesamtbilanz.
- Solange kein Referenzmodell die Veröffentlichungsprüfung besteht, wird die dritte Stufe
  von keinem MIWG-Modell ausgeführt. Damit ein `0` dort nicht mit einem stillen Defekt im
  Durchlauf verwechselt werden kann, belegt `Execution_stage_completes_a_supported_model`
  denselben Durchlauf an fünf bekannt ausführbaren Flowzer-Modellen (Gateways, paralleler
  Fluss, Nachrichten, User-Task, Zeitereignis).
- Die Modelle sind eine eingecheckte Kopie. Der Test läuft offline; nur
  `scripts/ci/fetch-miwg-reference-models.sh` braucht Netz.
