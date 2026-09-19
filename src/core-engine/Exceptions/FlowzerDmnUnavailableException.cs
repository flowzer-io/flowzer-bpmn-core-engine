namespace core_engine.Exceptions;

/// <summary>
/// Es wurde eine Entscheidung ausgewertet, obwohl die Engine ohne FEEL-faehigen
/// Ausdrucks-Handler laeuft.
/// </summary>
/// <remarks>
/// Faellt die native V8-Bibliothek aus, springt <see cref="FlowzerConfig"/> auf den einfachen
/// Handler zurueck, damit die Engine ueberhaupt startet. Eine DMN-Tabelle laesst sich damit
/// aber nicht rechnen — sie braucht echtes FEEL. Der Fehler faellt deshalb erst beim
/// Benutzen an und nennt den Handler, der tatsaechlich gesetzt ist.
/// </remarks>
public class FlowzerDmnUnavailableException(string? message = null) : FlowzerRuntimeException(message);
