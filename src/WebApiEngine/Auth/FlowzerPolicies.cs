namespace WebApiEngine.Auth;

/// <summary>Namen der Autorisierungsrichtlinien, auf die die Controller sich beziehen.</summary>
public static class FlowzerPolicies
{
    /// <summary>
    /// Die Grundanforderung: angemeldet und, falls konfiguriert, mit der Zugangsrolle.
    /// Wird nicht an Endpunkten verwendet, sondern um eine Ablehnung einzuordnen.
    /// </summary>
    public const string Access = "flowzer:access";

    public const string Modeler = "flowzer:modeler";
    public const string Operator = "flowzer:operator";

    /// <summary>
    /// Antwortheader, der eine 403 einordnet: <c>application</c> heisst, dass dieses Konto
    /// Flowzer gar nicht benutzen darf, <c>capability</c> heisst, dass nur diese eine Handlung
    /// fehlt. Ohne die Unterscheidung muesste die Oberflaeche jede Ablehnung als kompletten
    /// Zugangsverlust anzeigen.
    /// </summary>
    public const string AccessDeniedHeader = "X-Flowzer-Access-Denied";

    public const string DeniedApplication = "application";
    public const string DeniedCapability = "capability";
}
