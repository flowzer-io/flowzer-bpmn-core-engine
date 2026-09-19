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
| gelesen (Parser, mit ausführbarem Prozess) | 0 |
| gelesen, aber leer (kein `isExecutable="true"`) | 15 |
| veröffentlichbar (Fähigkeitsvertrag) | 0 |
| ausgeführt bis Ende | 0 |

„gelesen, aber leer" ist bewusst getrennt: Der Parser liest ausschließlich Prozesse mit
`isExecutable="true"`. Ein Modell ohne solchen Prozess läuft ohne Ausnahme durch, ohne dass
ein einziges Element gelesen worden wäre. Das ist keine Abdeckung.

## Die drei Stufen

| Stufe | Was geprüft wird | Mögliche Werte |
| --- | --- | --- |
| Lesen | `ModelParser.ParseModel` ohne Ausnahme | `ok`, `empty`, `rejected` |
| Veröffentlichen | `BpmnCapabilityMatrix.ValidateForDeployment` | `ok`, `rejected` |
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
| `C.1.0.bpmn` | `rejected` | `parser.FlowzerModelParseException` — `User task 'approveInvoice' requires either formKey or formId in formDefinition.` | `rejected` | `bpmn.user_task.form_required` an `userTask` | übersprungen (deployment rejected) |
| `C.1.1.bpmn` | `rejected` | `parser.FlowzerModelParseException` — `User task 'approveInvoice' requires either formKey or formId in formDefinition.` | `rejected` | `bpmn.user_task.form_required` an `userTask` | übersprungen (deployment rejected) |
| `C.2.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `C.3.0.bpmn` | `rejected` | `parser.FlowzerModelParseException` — `User task '_c73a5f4a-72f1-4e11-bb40-2f98da75fb9a' requires either formKey or formId in formDefinition.` | `rejected` | `bpmn.user_task.form_required` an `userTask` | übersprungen (deployment rejected) |
| `C.4.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `C.5.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `C.6.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `C.7.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `C.8.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |
| `C.8.1.bpmn` | `rejected` | `parser.element.unsupported` — `businessRuleTask` | `rejected` | `bpmn.element.unsupported` an `businessRuleTask` | übersprungen (deployment rejected) |
| `C.9.0.bpmn` | `rejected` | `parser.FlowzerModelParseException` — `User task 'UserTask_HandleTimeout' requires either formKey or formId in formDefinition.` | `rejected` | `bpmn.element.unsupported` an `businessRuleTask` | übersprungen (deployment rejected) |
| `C.9.1.bpmn` | `rejected` | `parser.FlowzerModelParseException` — `User task 'UserTask_CallCustomer' requires either formKey or formId in formDefinition.` | `rejected` | `bpmn.user_task.form_required` an `userTask` | übersprungen (deployment rejected) |
| `C.9.2.bpmn` | `rejected` | `parser.FlowzerModelParseException` — `User task 'UserTask_AccelerateDecision' requires either formKey or formId in formDefinition.` | `rejected` | `bpmn.flow_node.unreachable` an `subProcess` | übersprungen (deployment rejected) |
| `C.10.0.bpmn` | `empty` | kein Prozess mit `isExecutable="true"` | `rejected` | `bpmn.process.executable_required` an `definitions` | übersprungen (deployment rejected) |

## Häufigste Ablehnungsgründe

### Lesen (Parser)

| Grund | Anzahl | Modelle | Deutung |
| --- | --- | --- | --- |
| `parser.FlowzerModelParseException` | 6 | `C.1.0.bpmn`, `C.1.1.bpmn`, `C.3.0.bpmn`, `C.9.0.bpmn`, `C.9.1.bpmn`, `C.9.2.bpmn` | Flowzer verlangt an jedem User-Task ein `zeebe:formDefinition` mit `formKey` oder `formId`. Werkzeugneutrale Modelle tragen keines. **Produktentscheidung, keine BPMN-Lücke.** |
| `parser.element.unsupported` an `businessRuleTask` | 1 | `C.8.1.bpmn` | Eine Elementart mit Ausführungssemantik, die `ModelParser.GetFlowElements` nicht kennt. Reines Diagramm-Beiwerk zählt nicht mehr dazu — es wird überlesen. **Echte Ausführungslücke.** |

> Reines Diagramm-Beiwerk überliest der Parser (siehe
> [BPMN-CAPABILITIES.md](BPMN-CAPABILITIES.md), „Was überlesen wird"). An einem Element
> mit Ausführungssemantik, das er nicht kennt, bricht er dagegen ab. Gemeldet wird
> deshalb immer nur die **erste** Hürde eines Modells — hinter ihr können weitere liegen.

### Veröffentlichen (Fähigkeitsvertrag)

| Fehlercode | Elementart | Anzahl | Modelle | Deutung |
| --- | --- | --- | --- | --- |
| `bpmn.process.executable_required` | `definitions` | 15 | `A.1.0.bpmn`, `A.2.0.bpmn`, `A.2.1.bpmn`, `A.3.0.bpmn`, `A.4.0.bpmn`, `A.4.1.bpmn`, `B.1.0.bpmn`, `B.2.0.bpmn`, `C.2.0.bpmn`, `C.4.0.bpmn`, `C.5.0.bpmn`, `C.6.0.bpmn`, `C.7.0.bpmn`, `C.8.0.bpmn`, `C.10.0.bpmn` | Das Dokument enthält keinen Prozess mit `isExecutable="true"`. Die MIWG-Reihen A und B sind reine Modellierungs- und Layoutbeispiele. **Kein Befund über die Engine.** |
| `bpmn.user_task.form_required` | `userTask` | 4 | `C.1.0.bpmn`, `C.1.1.bpmn`, `C.3.0.bpmn`, `C.9.1.bpmn` | Wie oben: kein `zeebe:formDefinition` am User-Task. **Produktentscheidung.** |
| `bpmn.element.unsupported` | `businessRuleTask` | 2 | `C.8.1.bpmn`, `C.9.0.bpmn` | Elementart, die der Fähigkeitsvertrag v7 nicht führt. **Echte Ausführungslücke.** |
| `bpmn.flow_node.unreachable` | `subProcess` | 1 | `C.9.2.bpmn` | Ein Knoten ohne Weg von einem Start- oder Boundary-Event. Hier ist es ein `subProcess triggeredByEvent="true"` — ein Event-Subprozess, den weder Vertrag noch Engine kennen. **Echte Ausführungslücke.** |

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

Elementarten, an denen ein Modell als **erste** Hürde scheitert, weil Vertrag und Engine
sie nicht führen:

| Elementart | Anzahl | Modelle |
| --- | --- | --- |
| `businessRuleTask` | 2 | `C.8.1.bpmn`, `C.9.0.bpmn` |
| `subProcess` | 1 | `C.9.2.bpmn` |

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
