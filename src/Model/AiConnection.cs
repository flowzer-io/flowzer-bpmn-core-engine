namespace Model;

/// <summary>Unterstuetzte Providerfamilien; konkrete Modellfaehigkeiten werden spaeter getrennt geprueft.</summary>
public enum AiProviderKind
{
    OpenAi,
    OpenAiCompatible,
    Anthropic
}

/// <summary>Explizite Datenflussgrenze einer Verbindung, ohne stillen lokalen/Cloud-Fallback.</summary>
public enum AiProcessingLocation
{
    Cloud,
    Local
}

/// <summary>
/// Persistierte, nicht geheime Metadaten einer KI-Verbindung. <see cref="SecretReference"/>
/// verweist nur auf einen serverseitigen Secret-Store; ein geheimer Wert gehoert nie hierher.
/// Deaktivierte Eintraege bleiben fuer historische Workflowfassungen erhalten.
/// </summary>
public sealed record AiConnection(
    Guid Id,
    string Name,
    AiProviderKind Provider,
    AiProcessingLocation Location,
    string? BaseAddress,
    string DefaultModel,
    string SecretReference,
    bool Enabled,
    long Revision,
    DateTimeOffset UpdatedAtUtc,
    Guid UpdatedByUserId,
    AiToolPermission[]? AllowedTools = null);
