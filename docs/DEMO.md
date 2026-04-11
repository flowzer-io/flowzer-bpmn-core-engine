# Console-Demo

## Ziel

Die Console-Demo zeigt den bevorzugten Happy Path für neue Mitwirkende und Integratoren:

1. BPMN laden
2. Start-Subscription lesen
3. Instanz starten
4. User Task fortführen
5. Service Task fortführen
6. Prozessabschluss nachvollziehen

## Starten

```bash
dotnet run --project src/FlowzerDemoConsole/FlowzerDemoConsole.csproj
```

## Technische Grundlage

Die Demo verwendet bewusst den öffentlichen `ICore`-Vertrag und nicht die WebAPI.

Verwendet werden dabei:

- `CoreEngine`
- `CoreEventData`
- `CoreInteraction`
- `CoreInstance`

Zusätzlich wichtig: Die Demo läuft bewusst mit einer testfreundlichen
`FlowzerConfig` (z. B. `CreateForTests` beziehungsweise einem
`SimpleExpressionHandler`), damit sie lokal und in CI ohne native
V8-/ClearScript-Abhängigkeit ausführbar bleibt.

Diese Konfiguration dient der einfachen Demo- und Testbarkeit. Sie ist kein
automatischer Produktiv-Default, sondern ein bewusst vereinfachtes Setup für
den dokumentierten Happy Path.

## Verwendeter Beispielprozess

Die Demo nutzt den BPMN-Prozess aus:

- [`examples/simple-approval-process.bpmn`](../examples/simple-approval-process.bpmn)

Der Prozess ist absichtlich klein gehalten:

- Plain Start Event
- User Task
- Service Task
- End Event

## Erwartete Ausgabe

Die exakten GUIDs unterscheiden sich pro Lauf. In etwa sieht die Ausgabe so aus:

```text
Flowzer BPMN Core Engine – Console-Demo
======================================
Start-Subscription: Start (StartEvent_1)
InteractionFinished: Instanz=<guid>, State=Waiting, offene Interaktionen=1
Instanz gestartet: <guid>
Bearbeite UserTask: UserTask_1 (Review Document)
InteractionFinished: Instanz=<guid>, State=Waiting, offene Interaktionen=1
Bearbeite ServiceTask: ServiceTask_1 (Send Notification)
InteractionFinished: Instanz=<guid>, State=Completed, offene Interaktionen=0
Prozess abgeschlossen: Completed

Finaler Status: Completed
```

## Warum diese Demo wichtig ist

Die Demo dient als:

- schneller lokaler Smoke-Test
- Referenz für die empfohlene `ICore`-Nutzung
- Einstiegspunkt für neue Mitwirkende
- Grundlage für weitere Beispiele und Integrationsdokumentation
