# Copilot Instructions für flowzer-bpmn-core-engine

## Projektübersicht

Das flowzer-bpmn-core-engine Projekt ist eine C# .NET 8 Implementierung einer BPMN 2.0 (Business Process Model and Notation) Ausführungsengine. Das Projekt verfolgt eine klare Trennung zwischen der BPMN-Modelldefinition und der Ausführungslogik.

## Architektur und Kernprinzipien

### 1. BPMN 2.0 Standard Compliance
- **Immer** die offizielle BPMN 2.0 Spezifikation befolgen
- Alle BPMN-Elemente korrekt implementieren (Activities, Events, Gateways, Flows, etc.)
- Standard-konforme Namensgebung und Strukturierung verwenden
- BPMN XML Schema-Kompatibilität sicherstellen

### 2. Saubere Architektur
- **Klare Trennung** zwischen `BPMN` (Modelldefinitionen) und `core-engine` (Ausführungslogik)
- Interface-basiertes Design verwenden (`ICore`, `IBaseElement`, etc.)
- Dependency Injection Pattern befolgen
- Abstrakte Basisklassen für gemeinsame Funktionalität nutzen

### 3. Event-Driven Architecture
- Token-basierte Prozessausführung implementieren
- Event-Handler für externe Systemintegration
- Subscription-basierte Service-Integration
- Asynchrone Ausführung mit `Task`-Pattern

## Code-Standards und Best Practices

### C# Entwicklungsrichtlinien

```csharp
// ✅ Korrekt: Nullable Reference Types verwenden
public required string Id { get; set; }
public string? OptionalProperty { get; set; }

// ✅ Korrekt: Primary Constructor für einfache Klassen
public class UserTask(string name, string nodeId, string formId) : Interaction(name, nodeId)
{
    public string FormId { get; set; } = formId;
}

// ✅ Korrekt: Interface-basierte Abstraktion
public interface ICore
{
    Task<EventResult> HandleEvent(Instance instanceData, EventData eventData);
}
```

### Naming Conventions
- **Deutsch**: Kommentare und XML-Dokumentation in deutscher Sprache
- **Englisch**: Code, Variablen, Methoden und Interfaces in englischer Sprache
- PascalCase für öffentliche Member
- camelCase für private/lokale Variablen
- Interface-Namen mit "I" präfixieren

### XML-Dokumentation
```csharp
/// <summary>
/// Lädt die BPMN Definition (xml) aus dem angegebenen Stream und prüft diese ggf. auf Richtigkeit
/// </summary>
/// <param name="xmlDataStream">Die XML Daten des BPMN</param>
/// <param name="verify">Gibt an, ob die Daten auf syntaktische und logische Richtigkeit geprüft werden soll</param>
public Task LoadBpmnFile(Stream xmlDataStream, bool verify);
```

## BPMN-Spezifische Richtlinien

### 1. Element-Modellierung
- Jedes BPMN-Element erbt von `BaseElement`
- Alle erforderlichen Eigenschaften als `required` markieren
- Collections als `List<T>` mit Standard-Initialisierung `= []`
- Optionale Referenzen als nullable types

### 2. Prozess-Execution
- Token repräsentieren den aktuellen Ausführungsstand
- Interactions für externe System-Integrationen
- EventData für Datenaustausch zwischen Prozessschritten
- Subscriptions für Service-Abonnements

### 3. Error Handling
- BPMN Error Events korrekt implementieren
- Exception Handling für Engine-Fehler
- Validierung von BPMN-Definitionen vor Ausführung

## Entwicklungsworkflow

### 1. Neue Features
1. **BPMN Standard prüfen**: Neue Funktionalität gegen BPMN 2.0 Spezifikation validieren
2. **Interface Design**: Zuerst Interfaces und Abstraktionen definieren
3. **Modell Implementation**: BPMN-Elemente im `BPMN` Namespace implementieren
4. **Engine Integration**: Ausführungslogik im `core-engine` integrieren
5. **Tests**: Unit Tests für neue Funktionalität schreiben

### 2. Code Reviews
- BPMN-Konformität überprüfen
- Interface-Konsistenz sicherstellen
- Nullable Reference Types korrekt verwendet
- Deutsche Dokumentation, englischer Code
- Performance-Auswirkungen bei Engine-Änderungen

### 3. Testing Strategy
```csharp
// Tests sollten BPMN-Szenarien abdecken
[Test]
public async Task ProcessExecution_WithUserTask_ShouldCreateCorrectSubscription()
{
    // Arrange: BPMN-Definition laden
    // Act: Prozess starten
    // Assert: Erwartete Subscriptions und Tokens
}
```

## Performance und Skalierung

### 1. Engine Performance
- Minimale Token-Operationen
- Effiziente Event-Verarbeitung
- Memory-optimierte BPMN-Element-Instanzierung
- Async/await für IO-Operationen

### 2. BPMN-Model Optimierung
- Lazy Loading für große Prozessdefinitionen
- Caching für wiederverwendbare Elemente
- Streaming für große XML-Dateien

## Fehlerbehandlung und Logging

### 1. BPMN-Fehler
```csharp
// BPMN Error Events
public class BpmnError : Exception
{
    public string ErrorCode { get; }
    public string NodeId { get; }
}
```

### 2. Engine-Fehler
- Strukturiertes Logging für Debug-Informationen
- Context-Information in Exceptions
- Graceful Degradation bei nicht-kritischen Fehlern

## Integration und Extensibility

### 1. Service Integration
- Pluggable Service-Provider
- Standard-Interfaces für externe Services
- Configuration-basierte Service-Bindung

### 2. Custom Elements
- Extension-Points für projektspezifische BPMN-Elemente
- Custom Activity-Implementierungen
- Zusätzliche Event-Types

## Dokumentation

### 1. Code-Dokumentation
- Alle öffentlichen Interfaces vollständig dokumentiert
- BPMN-Konzepte in Kommentaren erklären
- Beispiele für komplexe Verwendung

### 2. Architektur-Dokumentation
- Klassendiagramme für BPMN-Hierarchie
- Sequenzdiagramme für Prozessausführung
- Integration-Patterns dokumentieren

## Migration und Versioning

### 1. BPMN-Kompatibilität
- Rückwärtskompatibilität für BPMN-Definitionen
- Graceful Handling von unbekannten Elementen
- Version-Marker in XML-Definitionen

### 2. API-Stabilität
- Semantic Versioning befolgen
- Breaking Changes klar kommunizieren
- Deprecation-Warnings vor Entfernung

---

## Wichtige Erinnerungen für Copilot

1. **BPMN 2.0 Standard ist führend** - Bei Unsicherheiten immer die offizielle Spezifikation konsultieren
2. **Deutsche Kommentare, englischer Code** - Konsistente Sprachwahl beibehalten
3. **Interface-first Design** - Abstraktionen vor Implementierungen definieren
4. **Token-basierte Ausführung** - Prozessschritte über Token-Manipulation steuern
5. **Event-driven Pattern** - Externe Integration über Events und Subscriptions
6. **Nullable Reference Types** - Moderne C# Features konsequent nutzen
7. **Asynchrone Patterns** - Task-basierte APIs für alle IO-Operationen
8. **Clean Architecture** - Klare Grenzen zwischen BPMN-Model und Engine
9. **Moderne Dokumentation** - README.md und Entwicklungsrichtlinien immer aktuell halten
10. **Community Focus** - Code sollte für externe Entwickler verständlich und erweiterbar sein

---

**Siehe auch:**
- [README.md](../README.md) - Projektübersicht und Schnellstart
- [DEVELOPMENT-GUIDELINES.md](../DEVELOPMENT-GUIDELINES.md) - Detaillierte Entwicklungsrichtlinien

**Letzte Aktualisierung**: $(date)
**Version**: 1.1