namespace FlowzerFrontend.Exceptions;

/// <summary>
/// Repräsentiert einen fachlichen API-Fehler, der bereits als kontrollierter Fehlervertrag
/// vom Server oder von einer API-nahen Frontendprüfung zurückgegeben wurde.
/// </summary>
public class ApiException(string? errorMessage) : Exception(errorMessage);
