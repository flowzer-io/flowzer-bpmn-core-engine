namespace FlowzerFrontend.Exceptions;

/// <summary>
/// Signalisiert, dass der Frontend-Client eine unerwartete oder unvollständige HTTP-/JSON-Antwort
/// erhalten hat und der erwartete API-Vertrag damit verletzt wurde.
/// </summary>
public class ApiContractException(string message) : ApiException(message);
