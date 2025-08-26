# Flowzer BPMN Core Engine

![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue.svg)
![License](https://img.shields.io/badge/license-MPL--2.0-blue.svg)
![Build Status](https://img.shields.io/badge/build-passing-brightgreen.svg)

A powerful and flexible BPMN (Business Process Model and Notation) execution engine for .NET, designed to execute business processes defined in BPMN 2.0 format.

## 🚀 Features

- **BPMN 2.0 Compliance**: Full support for BPMN 2.0 standard elements
- **Token-based Execution**: Efficient process execution using token-based flow control
- **Multi-Environment Support**: Runs in various deployment scenarios:
  - Long-running applications
  - Microservices
  - Azure Functions
  - AWS Lambda
  - Unit tests
- **Event-Driven Architecture**: External event handling for webhooks, user tasks, timers, and more
- **Process Validation**: Built-in BPMN file validation for syntactic and logical correctness
- **Flexible Integration**: Easy integration with existing .NET applications

## 🏗️ Architecture

The engine consists of two main components:

### BPMN Model Library
- **Activities**: Tasks, sub-processes, and call activities
- **Events**: Start, intermediate, and end events
- **Gateways**: Exclusive, parallel, inclusive, and event-based gateways
- **Data Objects**: Data stores, data objects, and data associations
- **Artifacts**: Text annotations and groups
- **Infrastructure**: Lanes, pools, and collaboration elements

### Core Execution Engine
- **ICore Interface**: Main execution interface
- **Token Management**: Process token lifecycle management
- **Event Processing**: External event integration
- **Instance Management**: Process instance state management

## 🛠️ Getting Started

### Prerequisites
- .NET 8.0 SDK or later
- Visual Studio 2022 or compatible IDE (optional)

### Installation

Clone the repository:
```bash
git clone https://github.com/flowzer-io/flowzer-bpmn-core-engine.git
cd flowzer-bpmn-core-engine
```

Restore dependencies:
```bash
dotnet restore
```

Build the project:
```bash
dotnet build
```

### Basic Usage

```csharp
using core_engine;

// Create core engine instance
ICore engine = new CoreEngine();

// Load BPMN file
using var fileStream = File.OpenRead("process.bpmn");
await engine.LoadBpmnFile(fileStream, verify: true);

// Get initial subscriptions (start events)
var subscriptions = await engine.GetInitialSubscriptions();

// Handle events and execute process
var eventData = new EventData { /* your event data */ };
var instance = new Instance { /* your instance data */ };
var result = await engine.HandleEvent(instance, eventData);
```

## 📋 Supported BPMN Elements

### Activities
- **Service Tasks**: Automated tasks executed by the system
- **User Tasks**: Human tasks requiring user interaction
- **Script Tasks**: Executable scripts within the process
- **Sub-processes**: Embedded and reusable processes

### Events
- **Start Events**: Process initiation points
- **Intermediate Events**: Process flow control points
- **End Events**: Process termination points
- **Timer Events**: Time-based process triggers
- **Message Events**: Inter-process communication
- **Signal Events**: Broadcast communication

### Gateways
- **Exclusive Gateways**: Decision points with single path selection
- **Parallel Gateways**: Concurrent flow execution
- **Inclusive Gateways**: Multiple path selection based on conditions

## 🔧 Configuration

The engine supports various configuration options for different deployment scenarios. Event handling can be configured for:

- **Automatic Processing**: Immediate execution of the next process step
- **External Events**: Integration with webhooks, user interfaces, and external services
- **Timer Events**: Time-based process triggering
- **Message Correlation**: Inter-process and external message handling

## 📖 Documentation

For detailed documentation, please refer to:
- [API Documentation](docs/api/) (Coming soon)
- [BPMN Element Reference](docs/bpmn-elements/) (Coming soon)
- [Integration Examples](docs/examples/) (Coming soon)

## 🤝 Contributing

We welcome contributions! Please feel free to submit issues, feature requests, and pull requests.

### Development Setup
1. Fork the repository
2. Create a feature branch: `git checkout -b feature/amazing-feature`
3. Make your changes and add tests
4. Build and test: `dotnet build && dotnet test`
5. Commit your changes: `git commit -m 'Add amazing feature'`
6. Push to the branch: `git push origin feature/amazing-feature`
7. Open a pull request

## 📄 License

This project is licensed under the Mozilla Public License 2.0 - see the [LICENSE](LICENSE) file for details.

## 🏢 About Flowzer

Flowzer BPMN Core Engine is part of the Flowzer ecosystem for business process management and automation.

---

**Note**: This is a core engine library. For complete business process management solutions, consider integrating with additional Flowzer components for process modeling, monitoring, and management interfaces.
