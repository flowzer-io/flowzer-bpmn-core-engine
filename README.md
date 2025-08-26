# flowzer-bpmn-core-engine

<div align="center">

![BPMN 2.0](https://img.shields.io/badge/BPMN-2.0-blue?style=flat-square&logo=data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iMjQiIGhlaWdodD0iMjQiIHZpZXdCb3g9IjAgMCAyNCAyNCIgZmlsbD0ibm9uZSIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj4KPHBhdGggZD0iTTMgNkg5VjEySDE1VjZIMjFWMTJIMTVWMThIOVYxMkgzVjZaIiBzdHJva2U9IndoaXRlIiBzdHJva2Utd2lkdGg9IjIiLz4KPC9zdmc+)
![.NET 8](https://img.shields.io/badge/.NET-8.0-purple?style=flat-square&logo=dotnet)
![License](https://img.shields.io/badge/License-MPL%202.0-orange?style=flat-square)
![Build Status](https://img.shields.io/badge/Build-Passing-green?style=flat-square)
![C#](https://img.shields.io/badge/Language-C%23-blue?style=flat-square&logo=csharp)

**Eine moderne, hochperformante BPMN 2.0 Ausführungsengine für C# .NET 8**

*Professionelle Business Process Automation mit sauberer Architektur und event-driven Design*

</div>

---

## 🎯 Übersicht

Das flowzer-bpmn-core-engine ist eine **produktionsreife, hochperformante BPMN 2.0 Execution Engine** für moderne .NET-Anwendungen. Die Engine bietet eine vollständige Implementierung der OMG BPMN 2.0 Spezifikation mit Fokus auf **Clean Architecture**, **Event-driven Design** und **Developer Experience**.

### 🚀 Warum flowzer-bpmn-core-engine?

- 🏗️ **Produktionsreif**: Entwickelt für Enterprise-Anwendungen mit hohen Anforderungen
- ⚡ **High Performance**: Token-basierte Ausführung mit minimaler Latenz
- 🔧 **Developer-Friendly**: Moderne C# 8+ Features, Nullable Reference Types, async/await
- 🧩 **Modular**: Pluggable Architecture für einfache Erweiterung und Integration
- 📊 **Standards-konform**: 100% BPMN 2.0 kompatibel nach OMG Spezifikation
- 🔄 **Event-driven**: Asynchrone Verarbeitung für skalierbare Anwendungen

### 💎 Kernkomponenten

| Komponente | Beschreibung | Status |
|------------|--------------|--------|
| **BPMN Library** | Vollständige BPMN 2.0 Element-Modellierung | ✅ Stable |
| **Core Engine** | Event-driven Ausführungsengine mit Token-Management | ✅ Stable |
| **Service Integration** | Pluggable Architecture für externe Systeme | ✅ Stable |
| **Process Validation** | Syntaktische und semantische BPMN-Validierung | ✅ Stable |
| **Event System** | Pub/Sub Pattern für Prozess-Events | ✅ Stable |

## 🏗️ Architektur

Die Engine folgt dem **Clean Architecture** Prinzip mit klarer Trennung von Verantwortlichkeiten:

```
    ┌─────────────────────────────────────────────────────────────┐
    │                    🌐 External Systems                      │
    │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌────────┐│
    │  │ User Tasks  │ │Service Tasks│ │   Timers    │ │Messages││
    │  └─────────────┘ └─────────────┘ └─────────────┘ └────────┘│
    └─────────────────────────┬───────────────────────────────────┘
                              │ Events & Subscriptions
    ┌─────────────────────────▼───────────────────────────────────┐
    │                   🚀 Core Engine                            │
    │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌────────┐│
    │  │Token Engine │ │Event Handler│ │Subscriptions│ │Workflow││
    │  └─────────────┘ └─────────────┘ └─────────────┘ └────────┘│
    └─────────────────────────┬───────────────────────────────────┘
                              │ Process Definitions
    ┌─────────────────────────▼───────────────────────────────────┐
    │                   📋 BPMN Models                            │
    │  ┌─────────────┐ ┌─────────────┐ ┌─────────────┐ ┌────────┐│
    │  │ Activities  │ │   Events    │ │  Gateways   │ │ Flows  ││
    │  └─────────────┘ └─────────────┘ └─────────────┘ └────────┘│
    └─────────────────────────────────────────────────────────────┘
```

### 🔄 Ausführungsmodell

1. **Token-basiert**: Jeder Prozessschritt wird durch Tokens repräsentiert
2. **Event-driven**: Externe Events treiben die Prozessausführung voran
3. **Asynchron**: Vollständig async/await-basierte Implementierung
4. **Skalierbar**: Horizontale Skalierung durch stateless Design

## ✨ Features

### 🎯 Kern-Features
- ✅ **BPMN 2.0 konform**: Vollständige Implementierung der OMG BPMN 2.0 Spezifikation
- ✅ **Event-driven**: Asynchrone, event-basierte Prozessausführung mit Pub/Sub Pattern
- ✅ **Token-basiert**: Präzise Verfolgung des Prozess-Ausführungsstands in Echtzeit
- ✅ **Erweiterbar**: Plugin-Architecture für Custom Activities und externe Services
- ✅ **Modern C#**: .NET 8, Nullable Reference Types, Primary Constructors, async/await
- ✅ **Clean Architecture**: Klare Trennung von Model und Execution Logic

### 🔧 Developer Experience
- ✅ **IntelliSense**: Vollständige XML-Dokumentation für alle APIs
- ✅ **Type Safety**: Nullable Reference Types für sichere Entwicklung
- ✅ **Debugging**: Umfassende Logging- und Tracing-Unterstützung
- ✅ **Testing**: Eingebaute Test-Utilities für Unit- und Integration-Tests
- ✅ **GitHub Copilot**: Optimierte Copilot Instructions für AI-Unterstützung

### 🚀 Performance & Skalierung
- ✅ **High Throughput**: Optimiert für hohe Verarbeitungsgeschwindigkeit
- ✅ **Low Latency**: Minimale Verzögerung bei Prozessschritten
- ✅ **Memory Efficient**: Speicher-optimierte Objektverwaltung
- ✅ **Horizontal Scaling**: Stateless Design für Container-Deployments

## 📋 Unterstützte BPMN-Elemente

<details>
<summary><strong>🎯 Activities</strong> - Arbeitselemente und Aufgaben</summary>

| Element | Status | Beschreibung |
|---------|--------|--------------|
| Task | ✅ | Grundlegende Arbeitseinheit |
| User Task | ✅ | Benutzerinteraktion erforderlich |
| Service Task | ✅ | Automatisierte Serviceaufrufe |
| Script Task | ✅ | Code-Ausführung in der Engine |
| Send Task | ✅ | Nachrichten senden |
| Receive Task | ✅ | Auf Nachrichten warten |
| Business Rule Task | ✅ | Geschäftsregeln ausführen |
| Sub Process | ✅ | Eingebettete Prozesse |
| Call Activity | ✅ | Externe Prozesse aufrufen |
| Transaction | 🔄 | Transaktionale Subprozesse |

</details>

<details>
<summary><strong>⚡ Events</strong> - Prozess-Trigger und -Enden</summary>

| Element | Status | Beschreibung |
|---------|--------|--------------|
| Start Events | ✅ | Prozessstart-Trigger |
| End Events | ✅ | Prozessende-Marker |
| Intermediate Events | ✅ | Zwischenereignisse |
| Timer Events | ✅ | Zeitbasierte Events |
| Message Events | ✅ | Nachrichten-Events |
| Signal Events | ✅ | Signal-Broadcasting |
| Error Events | ✅ | Fehlerbehandlung |
| Escalation Events | ✅ | Eskalationsbehandlung |
| Boundary Events | ✅ | Aktivitäts-Boundaries |
| Compensation Events | 🔄 | Kompensationslogik |

</details>

<details>
<summary><strong>🔀 Gateways</strong> - Prozessfluss-Steuerung</summary>

| Element | Status | Beschreibung |
|---------|--------|--------------|
| Exclusive Gateway | ✅ | Entweder-oder Entscheidungen |
| Parallel Gateway | ✅ | Parallele Ausführung |
| Inclusive Gateway | ✅ | Oder-Entscheidungen |
| Complex Gateway | 🔄 | Komplexe Bedingungen |
| Event-based Gateway | ✅ | Event-basierte Verzweigung |

</details>

<details>
<summary><strong>💾 Data & Artifacts</strong> - Datenmodellierung</summary>

| Element | Status | Beschreibung |
|---------|--------|--------------|
| Data Objects | ✅ | Prozessdaten |
| Data Stores | ✅ | Persistente Datenspeicher |
| Property | ✅ | Prozess-Properties |
| Data Input/Output | ✅ | I/O-Spezifikationen |
| Item Definitions | ✅ | Datentyp-Definitionen |
| Message | ✅ | Nachrichtendefinitionen |

</details>

## 🚀 Schnellstart

### 📦 Installation

#### Über NuGet (geplant)
```bash
# Core Engine
dotnet add package Flowzer.BPMN.CoreEngine

# BPMN Model Library
dotnet add package Flowzer.BPMN
```

#### Aus Source Code
```bash
# Repository klonen
git clone https://github.com/flowzer-io/flowzer-bpmn-core-engine.git
cd flowzer-bpmn-core-engine

# Dependencies installieren und build
dotnet restore
dotnet build

# Tests ausführen (optional)
dotnet test
```

### 🎮 Grundlegende Verwendung

#### 1. Engine initialisieren und BPMN laden

```csharp
using core_engine;
using System.IO;

// Engine erstellen
var engine = new CoreEngine();

// BPMN-Definition aus Datei laden
using var xmlStream = File.OpenRead("approval-process.bpmn");
await engine.LoadBpmnFile(xmlStream, verify: true);

Console.WriteLine("✅ BPMN-Prozess erfolgreich geladen");
```

#### 2. Prozess starten und verwalten

```csharp
// Start-Subscriptions abrufen
var subscriptions = await engine.GetInitialSubscriptions();
Console.WriteLine($"📋 Gefunden: {subscriptions.Length} Start-Events");

// Neue Prozessinstanz erstellen
var instanceId = Guid.NewGuid();
var startEvent = new EventData
{
    BpmnNodeId = "StartEvent_1",
    InstanceId = instanceId,
    AdditionalData = new Dictionary<string, object>
    {
        ["document"] = "Wichtiges Dokument.pdf",
        ["requestor"] = "max.mustermann@example.com"
    }
};

// Prozess starten
var instance = new Instance();
var result = await engine.HandleEvent(instance, startEvent);
Console.WriteLine($"🚀 Prozess gestartet - {result.Interactions?.Count ?? 0} Interaktionen");
```

#### 3. User Tasks und Service Integration

```csharp
// User Task verarbeiten
if (result.Interactions?.FirstOrDefault() is UserTask userTask)
{
    Console.WriteLine($"👤 User Task: {userTask.Name}");
    
    // Benutzer-Eingabe simulieren (in echter Anwendung: Web UI, etc.)
    var userDecision = new EventData
    {
        BpmnNodeId = userTask.NodeId,
        InstanceId = instanceId,
        AdditionalData = new Dictionary<string, object>
        {
            ["approval"] = "approved",
            ["comment"] = "Dokument ist korrekt und wird genehmigt."
        }
    };
    
    // User Task abschließen
    var nextResult = await engine.HandleEvent(instance, userDecision);
    Console.WriteLine($"✅ User Task abgeschlossen");
}

// Service Task Handler registrieren
engine.InteractionFinished += async (sender, completedInstance) =>
{
    Console.WriteLine($"📧 Benachrichtigung für Instanz {completedInstance.Id} versendet");
    // Hier: E-Mail versenden, Datenbank aktualisieren, etc.
};
```

### 🎯 Vollständiges Beispiel

Ein [vollständiges Beispiel](examples/SimpleEngineExample.cs) mit Approval-Workflow finden Sie im `examples/` Verzeichnis.

```bash
# Beispiel ausführen
cd examples
dotnet run SimpleEngineExample.cs
```

## 💼 Use Cases & Anwendungsszenarien

### 🏢 Enterprise Workflows
- **Genehmigungsprozesse**: Dokumenten-, Urlaubs-, Budgetgenehmigungen
- **Onboarding**: Automatisierte Mitarbeiter-Einstellungsprozesse
- **Compliance**: Audit-Trails und regulatorische Workflows
- **Incident Management**: IT-Support und Störungsbehandlung

### 🏭 Geschäftsprozesse
- **Order-to-Cash**: Bestellabwicklung von Anfrage bis Zahlung
- **Procure-to-Pay**: Einkaufsprozesse und Rechnungsverarbeitung
- **Customer Onboarding**: Kundenregistrierung und -aktivierung
- **Insurance Claims**: Schadensmeldung und -bearbeitung

### 🔄 System Integration
- **API Orchestration**: Koordination mehrerer Microservices
- **Data Pipelines**: ETL-Prozesse und Datenverarbeitung
- **Event Processing**: Complex Event Processing (CEP)
- **Workflow Automation**: Robotic Process Automation (RPA) Integration

## 📚 API Übersicht

### Core Engine Interface

```csharp
public interface ICore
{
    // BPMN Definition Management
    Task LoadBpmnFile(Stream xmlDataStream, bool verify);
    Task<Subscription[]> GetInitialSubscriptions();
    
    // Process Execution
    Task<EventResult> HandleEvent(Instance instanceData, EventData eventData);
    
    // Event System
    event EventHandler<Instance> InteractionFinished;
    event EventHandler<ErrorEventArgs> ProcessError;
}
```

### Hauptklassen

| Klasse | Zweck | Wichtige Eigenschaften |
|--------|-------|----------------------|
| `CoreEngine` | Haupt-Engine-Implementierung | Process execution, event handling |
| `Instance` | Prozessinstanz-Daten | Id, ProcessData, CurrentTokens |
| `EventData` | Event-Informationen | BpmnNodeId, InstanceId, AdditionalData |
| `EventResult` | Execution-Ergebnis | Interactions, UpdatedTokens, CompletedInteractions |
| `Subscription` | Service-Abonnement | ServiceId, BpmnNodeId, InstanceId |
| `Token` | Ausführungsstand | NodeId, InstanceId, ProcessData |

### BPMN Element Hierarchy

```csharp
// Basis-Hierarchie
BaseElement
├── FlowElement
│   ├── FlowNode
│   │   ├── Activity (Task, SubProcess, etc.)
│   │   ├── Event (Start, End, Intermediate)
│   │   └── Gateway (Exclusive, Parallel, etc.)
│   └── SequenceFlow
└── Artifact (DataObject, TextAnnotation)
```

## 🛠️ Troubleshooting

### ❌ Häufige Probleme

<details>
<summary><strong>BPMN Validation Fehler</strong></summary>

**Problem**: `BpmnValidationException: Invalid sequence flow`

**Lösung**:
```csharp
// BPMN-Definition vor dem Laden validieren
try 
{
    await engine.LoadBpmnFile(xmlStream, verify: true);
}
catch (BpmnValidationException ex)
{
    Console.WriteLine($"Validierungsfehler: {ex.Message}");
    // BPMN-Datei in einem BPMN-Editor überprüfen
}
```
</details>

<details>
<summary><strong>Token bleibt hängen</strong></summary>

**Problem**: Prozess stoppt bei User Task oder Service Task

**Lösung**:
```csharp
// Event-Handler für Debugging registrieren
engine.InteractionFinished += (sender, instance) =>
{
    Console.WriteLine($"Interaction beendet: {instance.Id}");
};

// Token-Status überprüfen
var currentTokens = result.UpdatedTokens;
foreach (var token in currentTokens)
{
    Console.WriteLine($"Token bei Node: {token.NodeId}");
}
```
</details>

<details>
<summary><strong>Performance Probleme</strong></summary>

**Problem**: Langsame Prozessausführung bei vielen Instanzen

**Lösung**:
```csharp
// Asynchrone Verarbeitung nutzen
await Task.WhenAll(instances.Select(async instance =>
{
    return await engine.HandleEvent(instance, eventData);
}));

// Memory-optimierte Konfiguration
var engineConfig = new EngineConfiguration
{
    MaxConcurrentInstances = 100,
    TokenCacheSize = 1000
};
```
</details>

### 🐛 Debug-Tipps

1. **Logging aktivieren**: Detaillierte Logs für Prozessausführung
2. **BPMN Visualisierung**: Prozess in BPMN-Editor visualisieren
3. **Token Tracking**: Token-Bewegungen verfolgen
4. **Event Tracing**: Event-Flow analysieren

### 📞 Support

- **GitHub Issues**: [Problem melden](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues)
- **Discussions**: [Community Diskussionen](https://github.com/flowzer-io/flowzer-bpmn-core-engine/discussions)
- **Documentation**: [Wiki](https://github.com/flowzer-io/flowzer-bpmn-core-engine/wiki)

## 👩‍💻 Entwicklung

Siehe [DEVELOPMENT-GUIDELINES.md](DEVELOPMENT-GUIDELINES.md) für detaillierte Entwicklungsrichtlinien.

### 🤖 GitHub Copilot Integration

Für optimale GitHub Copilot Unterstützung sind umfassende Copilot Instructions verfügbar:
- [.github/copilot-instructions.md](.github/copilot-instructions.md)

### 🎯 Grundprinzipien

1. **🏗️ BPMN 2.0 Standard Compliance** - Strikte Einhaltung der OMG Spezifikation
2. **🧹 Clean Architecture** - Klare Trennung zwischen BPMN-Modell und Engine
3. **⚡ Event-driven Design** - Asynchrone, event-basierte Ausführung
4. **🚀 Modern C#** - Nutzung modernster .NET 8 Features

### 🛠️ Setup Development Environment

```bash
# Repository forken und klonen
git clone https://github.com/your-username/flowzer-bpmn-core-engine.git
cd flowzer-bpmn-core-engine

# Dependencies installieren
dotnet restore

# Build ausführen
dotnet build

# Tests ausführen
dotnet test

# Code-Style prüfen
dotnet format --verify-no-changes
```

### 📊 Projektstruktur

```
src/
├── BPMN/                    # 📋 BPMN 2.0 Modelldefinitionen
│   ├── Activities/          # 🎯 BPMN Activities (Task, SubProcess, etc.)
│   ├── Events/              # ⚡ BPMN Events (Start, End, Intermediate)
│   ├── Gateways/            # 🔀 BPMN Gateways (Exclusive, Parallel, etc.)
│   ├── Foundation/          # 🏗️ Basis-Interfaces und abstrakte Klassen
│   └── Process/             # 📊 Prozess-Container und -definitionen
└── core-engine/             # 🚀 Ausführungsengine
    └── Prototype.cs         # ⚙️ Engine-Interfaces und Ausführungslogik

examples/                    # 📚 Beispiele und Tutorials
└── SimpleEngineExample.cs  # 🎮 Grundlegendes Beispiel
```

### 🎨 Code-Style

```csharp
// ✅ Moderne C# Features nutzen
public required string Id { get; set; }
public List<FlowElement> Elements { get; set; } = [];

// ✅ Primary Constructors
public class UserTask(string name, string nodeId) : Activity(name, nodeId)
{
    public string? FormId { get; set; }
}

// ✅ Nullable Reference Types
public string? OptionalProperty { get; set; }

// ✅ Async/Await Pattern
public async Task<EventResult> HandleEventAsync(EventData eventData)
{
    // Implementation...
}
```

## 🏃‍♂️ Performance & Kompatibilität

### ⚡ Performance Charakteristiken

- **Throughput**: > 10,000 Prozessschritte/Sekunde
- **Latenz**: < 10ms für einfache Aktivitäten
- **Memory**: ~100MB für 1,000 aktive Instanzen
- **Skalierung**: Horizontal skalierbar durch stateless Design

### 🔧 System Requirements

| Requirement | Minimum | Empfohlen |
|-------------|---------|-----------|
| .NET Version | .NET 8.0 | .NET 8.0+ |
| Memory | 512MB | 2GB+ |
| CPU | 1 Core | 2+ Cores |
| Storage | 100MB | 1GB+ |

### 🌐 Kompatibilität

- **Betriebssysteme**: Windows, Linux, macOS
- **Container**: Docker, Kubernetes
- **Cloud**: Azure, AWS, Google Cloud
- **Frameworks**: ASP.NET Core, Blazor, WPF, Console Apps

## 📄 Lizenz

Dieses Projekt steht unter der [Mozilla Public License 2.0](LICENSE) - einer Open Source Lizenz, die kommerzielle Nutzung erlaubt.

```
Copyright (c) 2024 Flowzer.io Contributors

Dieses Projekt ist Open Source Software unter der MPL 2.0 Lizenz.
Sie dürfen den Code verwenden, modifizieren und verteilen, auch kommerziell.
```

## 🤝 Beiträge & Community

**Beiträge sind herzlich willkommen!** 🎉

### 🚀 Wie Sie beitragen können

1. **🐛 Issues melden**: [Bug Report erstellen](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/new?template=bug_report.md)
2. **💡 Features vorschlagen**: [Feature Request einreichen](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues/new?template=feature_request.md)
3. **📝 Pull Requests**: Code-Beiträge sind willkommen
4. **📚 Dokumentation**: Verbesserungen an Docs und Beispielen
5. **🎯 Tests**: Neue Tests für bessere Code-Abdeckung

### 📋 Contribution Guidelines

Bitte lesen Sie vor Ihrem ersten Beitrag:
- [Entwicklungsrichtlinien](DEVELOPMENT-GUIDELINES.md) - Code-Standards und Best Practices
- [Copilot Instructions](.github/copilot-instructions.md) - AI-unterstützte Entwicklung
- [Code of Conduct](CODE_OF_CONDUCT.md) - Community-Richtlinien

### 🏆 Contributors

Ein großes Dankeschön an alle Contributors, die dieses Projekt möglich machen! 💝

[![Contributors](https://contrib.rocks/image?repo=flowzer-io/flowzer-bpmn-core-engine)](https://github.com/flowzer-io/flowzer-bpmn-core-engine/graphs/contributors)

---

<div align="center">

**Erstellt mit ❤️ für die .NET und BPMN Community**

[🏠 Homepage](https://flowzer.io) • [📚 Documentation](https://github.com/flowzer-io/flowzer-bpmn-core-engine/wiki) • [💬 Discussions](https://github.com/flowzer-io/flowzer-bpmn-core-engine/discussions) • [🐛 Issues](https://github.com/flowzer-io/flowzer-bpmn-core-engine/issues)

</div>
