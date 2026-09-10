namespace core_engine;

/// <summary>Maschinenlesbarer, versionierter Vertrag der durch Flowzer ausführbaren BPMN-Teilmenge.</summary>
public sealed record BpmnCapabilityContract(
    string ContractVersion,
    IReadOnlyList<BpmnElementCapability> Elements);

/// <summary>Fähigkeit einer BPMN-Elementart in der angegebenen Vertragsversion.</summary>
public sealed record BpmnElementCapability(
    string ElementType,
    bool Modelable,
    bool Parsable,
    bool Executable);
