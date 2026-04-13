namespace core_engine.Exceptions;

/// <summary>
/// Signalisiert, dass ein BPMN-Modell zwar syntaktisch lesbar ist,
/// aber für einen unterstützten Flowzer-Pfad fachlich unvollständig oder widersprüchlich ist.
/// </summary>
public class FlowzerModelParseException(string? message = null) : Exception(message);
