# `ICore`-Vertrag

## Ziel

`ICore` beschreibt den kleinsten bewusst öffentlichen Integrationspfad für die Engine, ohne WebAPI-, Storage- oder UI-Abhängigkeiten.

Der Vertrag richtet sich an Einbettungen wie:

- einfache Host-Anwendungen
- Console-Demos
- Tests auf Integrationsniveau
- Adapter, die den Kern ohne REST direkt ansprechen wollen

## Verantwortlichkeiten

`ICore` übernimmt aktuell genau diese Aufgaben:

1. **eine BPMN-Definition laden**
2. **initiale Einstiegspunkte ermitteln**
3. **ein Event starten oder eine aktive Interaktion fortführen**
4. **den aktuellen Instanz-Snapshot zurückgeben**

## Bewusst nicht Teil des Vertrags

Diese Themen gehören aktuell **nicht** in `ICore`:

- Persistenz von Definitionen oder Instanzen
- REST-/HTTP-spezifische DTOs
- Frontend-spezifische Modelle
- Benutzer- und Rollenverwaltung
- langfristige Subscription-Speicherung
- verteilte oder mehrknotige Laufzeit

## Öffentliche Typen

Der Vertrag besteht aus diesen Typen:

- `ICore`
- `CoreEngine`
- `CoreEventData`
- `CoreEventResult`
- `CoreInstance`
- `CoreInteraction`
- `CoreSubscription`

## Unterstützter Integrationspfad in der ersten Version

Die aktuelle erste Version unterstützt bewusst den häufigsten Happy Path:

- **genau eine geladene BPMN-Definition**
- **genau ein Prozess** pro geladener Definition
- Start über:
  - Plain Start Event
  - Message Start Event
  - Signal Start Event
- Fortführung aktiver:
  - User Tasks
  - Service Tasks
- Ergebnisrückgabe als in-memory Snapshot

## Aktuelle Grenzen

Die erste Version ist absichtlich klein. Aktuell gelten daher noch diese Grenzen:

- kein eingebauter Persistenz-Layer
- kein vollständiger Query-Vertrag für Instanzhistorie
- kein allgemeines externes Subscription-Management
- Plain-Starts werden aktuell nur sauber unterstützt, wenn die Definition genau **ein** explizites Plain-Start-Event enthält

## Beispiel

```csharp
using core_engine;

var core = new CoreEngine(FlowzerConfig.CreateForTests());

await using var xmlStream = File.OpenRead("examples/simple-approval-process.bpmn");
await core.LoadBpmnFile(xmlStream, verify: true);

var subscriptions = await core.GetInitialSubscriptions();
var startSubscription = subscriptions.Single();

var instanceId = Guid.NewGuid();

var startResult = await core.HandleEvent(new CoreEventData
{
    InstanceId = instanceId,
    BpmnNodeId = startSubscription.BpmnNodeId
});

var userTask = startResult.Instance.Interactions.Single();

var nextResult = await core.HandleEvent(new CoreEventData
{
    InstanceId = instanceId,
    BpmnNodeId = userTask.NodeId,
    AdditionalData = new Dictionary<string, object?>
    {
        ["approval"] = "approved"
    }
});
```

## Ereignismodell

`HandleEvent(...)` arbeitet immer nach demselben Muster:

- **neue `InstanceId`** + Start-`BpmnNodeId` → neue Instanz starten
- **bestehende `InstanceId`** + aktive Interaktions-`BpmnNodeId` → wartende Interaktion fortführen

## Event `InteractionFinished`

Nach jeder erfolgreich verarbeiteten Interaktion feuert `ICore` ein `InteractionFinished`-Event mit einem aktuellen `CoreInstance`-Snapshot.

Das ist nützlich für:

- Logging
- Demo-Anwendungen
- Adapterschichten
- einfache Reaktionen auf Statuswechsel

## Testabdeckung

Der bevorzugte Integrationspfad ist aktuell durch Tests abgesichert für:

- Laden einer BPMN-Datei
- Plain Start Events
- Message Start Events
- Signal Start Events
- User-Task-Fortsetzung
- Service-Task-Fortsetzung bis Prozessende
