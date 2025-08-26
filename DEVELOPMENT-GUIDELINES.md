# Entwicklungsrichtlinien - flowzer-bpmn-core-engine

## Übersicht

Dieses Dokument definiert die Entwicklungsrichtlinien und Best Practices für das flowzer-bpmn-core-engine Projekt - eine BPMN 2.0 konforme Ausführungsengine in C# .NET 8.

## Projektstruktur

```
src/
├── BPMN/                    # BPMN 2.0 Modelldefinitionen
│   ├── Activities/          # BPMN Activities (Task, SubProcess, etc.)
│   ├── Events/              # BPMN Events (Start, End, Intermediate)
│   ├── Gateways/            # BPMN Gateways (Exclusive, Parallel, etc.)
│   ├── Foundation/          # Basis-Interfaces und abstrakte Klassen
│   └── Process/             # Prozess-Container und -definitionen
└── core-engine/             # Ausführungsengine
    └── Prototype.cs         # Engine-Interfaces und Ausführungslogik
```

## Grundprinzipien

### 1. BPMN 2.0 Konformität
- **Strikt** nach BPMN 2.0 OMG Standard entwickeln
- Offizielle BPMN 2.0 Spezifikation als Referenz verwenden
- Keine proprietären Erweiterungen ohne Dokumentation

### 2. Separation of Concerns
- **BPMN-Namespace**: Reine Datenmodelle ohne Ausführungslogik
- **core-engine**: Ausführungslogik ohne BPMN-spezifische Implementierungsdetails
- Klare Interface-Abgrenzungen zwischen den Schichten

### 3. Modern C# Practices
- .NET 8 Target Framework
- Nullable Reference Types aktiviert
- Primary Constructors für einfache Klassen
- `required` Properties für essentielle Daten
- Async/await für alle IO-Operationen

## Code-Standards

### Naming Conventions

```csharp
// ✅ Interfaces mit 'I' Präfix
public interface ICore { }
public interface IBaseElement { }

// ✅ Abstrakte Klassen ohne Präfix
public abstract class BaseElement { }
public abstract class FlowElement { }

// ✅ Konkrete Implementierungen beschreibend
public class UserTask { }
public class ExclusiveGateway { }

// ✅ Events mit 'Event' Suffix
public class StartEvent { }
public class EndEvent { }
```

### Property-Definitionen

```csharp
// ✅ Required Properties
public required string Id { get; set; }
public required string Name { get; set; }

// ✅ Optional Properties
public string? Description { get; set; }
public Process? ParentProcess { get; set; }

// ✅ Collections mit Initialisierung
public List<FlowElement> FlowElements { get; set; } = [];
public List<Documentation> Documentations { get; set; } = [];
```

### Dokumentation

```csharp
/// <summary>
/// Lädt eine BPMN-Definition aus einem XML-Stream und validiert sie optional
/// </summary>
/// <param name="xmlDataStream">Der Stream mit den BPMN XML-Daten</param>
/// <param name="verify">Gibt an, ob die Definition validiert werden soll</param>
/// <returns>Task für asynchrone Ausführung</returns>
public Task LoadBpmnFile(Stream xmlDataStream, bool verify);
```

## Architektur-Patterns

### 1. Event-Driven Architecture

```csharp
// Event-Handling für externe Services
public interface ICore
{
    Task<EventResult> HandleEvent(Instance instanceData, EventData eventData);
    event EventHandler<Instance> InteractionFinished;
}

// Service-Subscriptions für Integration
public class Subscription
{
    public required string ServiceId { get; set; }
    public required string BpmnNodeId { get; set; }
    public required Guid? InstanceId { get; set; }
}
```

### 2. Token-basierte Ausführung

```csharp
// Token repräsentieren Ausführungsstand
public class Token
{
    public required string NodeId { get; set; }
    public required Guid InstanceId { get; set; }
    public Dictionary<string, object> ProcessData { get; set; } = [];
}
```

### 3. Interaction Pattern

```csharp
// Abstrakte Basis für alle Interaktionen
public abstract class Interaction(string name, string nodeId)
{
    public string Name { get; set; } = name;
    public string NodeId { get; set; } = nodeId;
    public Dictionary<string, object> AdditionalData { get; set; } = [];
}

// Spezifische Implementierungen
public class UserTask(string name, string nodeId, string formId) 
    : Interaction(name, nodeId)
{
    public string FormId { get; set; } = formId;
}
```

## BPMN-Element Implementierung

### Basis-Hierarchie

```csharp
// Alle BPMN-Elemente erben von BaseElement
public abstract class BaseElement : IBaseElement
{
    public required string Id { get; set; }
    public List<Documentation> Documentations { get; set; } = [];
    public List<ExtensionDefinition> ExtensionDefinitions { get; set; } = [];
}

// Flow-Elemente haben zusätzliche Eigenschaften
public abstract class FlowElement : BaseElement
{
    public string? Name { get; set; }
    // Weitere Flow-spezifische Properties
}
```

### Element-spezifische Implementierung

```csharp
// Activities als Container für Aufgaben
public abstract class Activity : FlowElement
{
    public LoopCharacteristics? LoopCharacteristics { get; set; }
    public List<InputOutputSpecification> IoSpecification { get; set; } = [];
}

// Tasks als ausführbare Activities
public abstract class Task : Activity
{
    // Task-spezifische Implementierung
}

// Konkrete Task-Implementierungen
public class ServiceTask : Task
{
    public string? Implementation { get; set; }
    public Operation? OperationRef { get; set; }
}
```

## Testing-Richtlinien

### Test-Struktur (empfohlen)

```csharp
[TestFixture]
public class CoreEngineTests
{
    [Test]
    public async Task LoadBpmnFile_ValidXml_ShouldLoadSuccessfully()
    {
        // Arrange
        var core = new CoreEngine();
        var xmlStream = CreateValidBpmnStream();
        
        // Act
        await core.LoadBpmnFile(xmlStream, verify: true);
        
        // Assert
        var subscriptions = await core.GetInitialSubscriptions();
        Assert.That(subscriptions, Is.Not.Empty);
    }
    
    [Test]
    public async Task HandleEvent_UserTaskCompletion_ShouldAdvanceToken()
    {
        // Arrange
        var core = new CoreEngine();
        await core.LoadBpmnFile(CreateProcessWithUserTask(), verify: true);
        
        // Act
        var result = await core.HandleEvent(instance, eventData);
        
        // Assert
        Assert.That(result.Tokens, Has.Count.EqualTo(1));
        Assert.That(result.Interactions, Contains.Item.InstanceOf<ServiceTask>());
    }
}
```

## Error Handling

### BPMN-Fehler

```csharp
// BPMN Error Events
public class BpmnError : Exception
{
    public required string ErrorCode { get; init; }
    public required string NodeId { get; init; }
    
    public BpmnError(string errorCode, string nodeId, string message) 
        : base(message)
    {
        ErrorCode = errorCode;
        NodeId = nodeId;
    }
}
```

### Engine-Fehler

```csharp
// Engine-spezifische Exceptions
public class ProcessExecutionException : Exception
{
    public required Guid InstanceId { get; init; }
    public required string NodeId { get; init; }
    
    public ProcessExecutionException(Guid instanceId, string nodeId, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        InstanceId = instanceId;
        NodeId = nodeId;
    }
}
```

## Performance-Richtlinien

### Memory Management

```csharp
// ✅ Lazy Loading für große Strukturen
public class Process : CallableElement
{
    private List<FlowElement>? _flowElements;
    public List<FlowElement> FlowElements 
    { 
        get => _flowElements ??= LoadFlowElements();
        set => _flowElements = value;
    }
}

// ✅ Streaming für große XML-Dateien
public async Task LoadBpmnFile(Stream xmlDataStream, bool verify)
{
    using var reader = XmlReader.Create(xmlDataStream, new XmlReaderSettings
    {
        Async = true,
        IgnoreWhitespace = true
    });
    
    // Streaming-basierte Verarbeitung
}
```

### Async Patterns

```csharp
// ✅ Konsequent async/await verwenden
public async Task<EventResult> HandleEvent(Instance instanceData, EventData eventData)
{
    var token = await FindTokenAsync(eventData.BpmnNodeId, instanceData.Id);
    var nextNodes = await GetNextNodesAsync(token.NodeId);
    return await ProcessNodesAsync(nextNodes, token);
}

// ✅ ConfigureAwait(false) für Library-Code
public async Task<Subscription[]> GetInitialSubscriptions()
{
    var startEvents = await FindStartEventsAsync().ConfigureAwait(false);
    return CreateSubscriptions(startEvents);
}
```

## Integration-Patterns

### Service Integration

```csharp
// Interface für externe Services
public interface IExternalService
{
    Task<object> ExecuteAsync(ServiceTask task, Dictionary<string, object> inputData);
    bool CanHandle(string serviceType);
}

// Service-Registry für Dependency Injection
public interface IServiceRegistry
{
    void RegisterService<T>(string serviceId, T service) where T : IExternalService;
    IExternalService? GetService(string serviceId);
}
```

### Event Integration

```csharp
// Event-Publisher für externe Systeme
public interface IEventPublisher
{
    Task PublishAsync<T>(string eventType, T eventData);
    Task<IDisposable> SubscribeAsync<T>(string eventType, Func<T, Task> handler);
}
```

## Deployment und Versioning

### Assembly-Informationen

```csharp
// AssemblyInfo.cs oder .csproj
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

// Semantic Versioning befolgen
// MAJOR.MINOR.PATCH
// - MAJOR: Breaking Changes
// - MINOR: Neue Features (rückwärtskompatibel)
// - PATCH: Bugfixes
```

### Konfiguration

```json
// appsettings.json Schema
{
  "BpmnEngine": {
    "EnableValidation": true,
    "MaxConcurrentProcesses": 100,
    "TimeoutMinutes": 30,
    "Services": {
      "UserTaskService": "MyApp.Services.UserTaskService",
      "EmailService": "MyApp.Services.EmailService"
    }
  }
}
```

## Commit-Guidelines

### Commit-Messages

```
feat(engine): Implementierung von Parallel Gateway Execution
fix(bpmn): Korrektur der SequenceFlow Validierung
docs(readme): Aktualisierung der Installation-Anweisungen
test(core): Hinzufügung von Token-Lifecycle Tests
refactor(activities): Vereinfachung der Task-Hierarchie
```

### Branch-Strategy

- `main`: Produktions-bereiter Code
- `develop`: Integration-Branch für Features
- `feature/`: Feature-spezifische Branches
- `hotfix/`: Kritische Bugfixes für Production
- `release/`: Release-Vorbereitung

## Code Review Checklist

- [ ] BPMN 2.0 Konformität überprüft
- [ ] Nullable Reference Types korrekt verwendet
- [ ] Deutsche Dokumentation, englischer Code
- [ ] Async Patterns korrekt implementiert
- [ ] Error Handling implementiert
- [ ] Performance-Impact bewertet
- [ ] Tests hinzugefügt/aktualisiert
- [ ] Breaking Changes dokumentiert

---

**Letzte Aktualisierung**: $(date)
**Version**: 1.0