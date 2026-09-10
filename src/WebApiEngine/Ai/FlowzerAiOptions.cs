namespace WebApiEngine.Ai;

/// <summary>Installationsweite Sicherheitsgrenzen fuer KI-Verarbeitung und Secret-Aufloesung.</summary>
public sealed class FlowzerAiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Cloud-Verarbeitung ist opt-in und wird nie durch einen Workflow eingeschaltet.</summary>
    public bool AllowCloudProviders { get; set; }

    /// <summary>Lokale/private Endpunkte sind wegen des internen Netzzugriffs ein eigenes Opt-in.</summary>
    public bool AllowLocalEndpoints { get; set; }

    /// <summary>Erlaubter Namensraum fuer Umgebungsvariablen-Referenzen.</summary>
    public string SecretEnvironmentVariablePrefix { get; set; } = "FLOWZER_AI_";

    public bool IsValid() =>
        SecretEnvironmentVariablePrefix is { Length: >= 3 and <= 64 }
        && SecretEnvironmentVariablePrefix.All(character =>
            character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')
        && SecretEnvironmentVariablePrefix[0] is >= 'A' and <= 'Z';
}
