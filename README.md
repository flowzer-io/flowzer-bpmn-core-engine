# flowzer-bpmn-core-engine

Eine BPMN 2.0 konforme Ausführungsengine für Business Process Model and Notation (BPMN) Workflows in C# .NET 8.

## Übersicht

Das flowzer-bpmn-core-engine Projekt stellt eine vollständige Implementierung einer BPMN 2.0 Execution Engine bereit, die sowohl die BPMN-Modelldefinitionen als auch die Ausführungslogik umfasst.

### Kernkomponenten

- **BPMN-Bibliothek**: Vollständige BPMN 2.0 Element-Modellierung
- **Core Engine**: Event-driven Ausführungsengine mit Token-basierter Prozesssteuerung
- **Service Integration**: Pluggable Architecture für externe System-Integration

## Architektur

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   BPMN Models   │    │   Core Engine    │    │ External        │
│                 │    │                  │    │ Services        │
│ • Activities    │◄──►│ • Token Engine   │◄──►│ • User Tasks    │
│ • Events        │    │ • Event Handler  │    │ • Service Tasks │
│ • Gateways      │    │ • Subscriptions  │    │ • Timers        │
│ • Flows         │    │ • Interactions   │    │ • Messages      │
└─────────────────┘    └──────────────────┘    └─────────────────┘
```

## Features

- ✅ **BPMN 2.0 konform**: Vollständige Implementierung der OMG BPMN 2.0 Spezifikation
- ✅ **Event-driven**: Asynchrone, event-basierte Prozessausführung
- ✅ **Token-basiert**: Präzise Verfolgung des Prozess-Ausführungsstands
- ✅ **Erweiterbar**: Plugin-Architecture für Custom Activities und Services
- ✅ **Modern C#**: .NET 8, Nullable Reference Types, Async/Await
- ✅ **Clean Architecture**: Klare Trennung von Model und Execution Logic

## Unterstützte BPMN-Elemente

### Activities
- Task, User Task, Service Task, Script Task
- Send Task, Receive Task, Business Rule Task
- Sub Process, Call Activity, Ad-hoc Sub Process
- Transaction

### Events
- Start Events, End Events, Intermediate Events
- Timer Events, Message Events, Signal Events
- Error Events, Escalation Events
- Boundary Events, Compensation Events

### Gateways
- Exclusive Gateway, Parallel Gateway
- Inclusive Gateway, Complex Gateway
- Event-based Gateway

### Data
- Data Objects, Data Stores
- Property, Data Input/Output
- Item Definitions

## Schnellstart

### Installation

```bash
# Repository klonen
git clone https://github.com/flowzer-io/flowzer-bpmn-core-engine.git
cd flowzer-bpmn-core-engine

# Projekt bauen
dotnet build
```

### Grundlegende Verwendung

```csharp
// BPMN-Engine initialisieren
var engine = new CoreEngine();

// BPMN-Definition laden
using var xmlStream = File.OpenRead("process.bpmn");
await engine.LoadBpmnFile(xmlStream, verify: true);

// Initialisierungs-Subscriptions abrufen
var subscriptions = await engine.GetInitialSubscriptions();

// Prozess durch Event starten
var eventData = new EventData 
{ 
    BpmnNodeId = "StartEvent_1", 
    InstanceId = Guid.NewGuid() 
};
var result = await engine.HandleEvent(instanceData, eventData);
```

## Entwicklung

Siehe [DEVELOPMENT-GUIDELINES.md](DEVELOPMENT-GUIDELINES.md) für detaillierte Entwicklungsrichtlinien.

### GitHub Copilot Integration

Für optimale GitHub Copilot Unterstützung sind umfassende Copilot Instructions verfügbar:
- [.github/copilot-instructions.md](.github/copilot-instructions.md)

### Grundprinzipien

1. **BPMN 2.0 Standard Compliance** - Strikte Einhaltung der OMG Spezifikation
2. **Clean Architecture** - Klare Trennung zwischen BPMN-Modell und Engine
3. **Event-driven Design** - Asynchrone, event-basierte Ausführung
4. **Modern C#** - Nutzung modernster .NET 8 Features

## Lizenz

Dieses Projekt steht unter der [Mozilla Public License 2.0](LICENSE).

## Beiträge

Beiträge sind willkommen! Bitte lesen Sie die [Entwicklungsrichtlinien](DEVELOPMENT-GUIDELINES.md) und beachten Sie die [Copilot Instructions](.github/copilot-instructions.md) für konsistente Code-Qualität.
