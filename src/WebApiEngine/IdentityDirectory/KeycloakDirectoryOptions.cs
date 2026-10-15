namespace WebApiEngine.IdentityDirectory;

/// <summary>
/// Konfiguration für den ausschließlich lesenden Keycloak-Verzeichnisabgleich.
/// Das Client-Secret wird zur Laufzeit über die Konfiguration injiziert und darf nie geloggt
/// oder an einen Browser weitergegeben werden.
/// </summary>
public sealed class KeycloakDirectoryOptions
{
    public const string SectionName = "IdentityDirectory";

    /// <summary>Der Abgleich ist opt-in, damit ein API-Start ohne Keycloak unverändert bleibt.</summary>
    public bool Enabled { get; set; }

    /// <summary>HTTPS-Basisadresse des Keycloak-Servers ohne Realm-Pfad.</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Exakter OIDC-Issuer aus dem <c>iss</c>-Claim. Er ist bewusst getrennt von der Admin-
    /// Adresse, weil Keycloak hinter einem Proxy intern unter einer anderen URL erreichbar sein kann.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Realm, aus dem Personen und Gruppen gelesen werden.</summary>
    public string Realm { get; set; } = string.Empty;

    /// <summary>Client-ID eines Service-Accounts mit ausschließlich lesenden Realm-Rechten.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Vertrauliches Client-Secret; ausschließlich zur Laufzeit aus einem Secret-Store.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Optionale stabile Keycloak-Gruppen-ID als Installationsgrenze. Mit Wert
    /// werden nur diese Gruppe, ihre Nachfahren und deren Mitglieder gelesen;
    /// Namen, Pfadpräfixe und Token-Gruppen sind kein Identitätsersatz.
    /// Leer erhält den bestehenden ausdrücklich realmweiten Abgleich.
    /// </summary>
    public string RootGroupId { get; set; } = string.Empty;

    /// <summary>Größe jeder Keycloak-Seite; der Wert wird zusätzlich defensiv begrenzt.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Schützt vor Endlosschleifen bei einem fehlerhaften oder nicht fortschreitenden Server.</summary>
    public int MaxPages { get; set; } = 10_000;

    /// <summary>Zusätzliche Versuche für temporäre Netzwerk- und Serverfehler.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Wartezeit zwischen Wiederholungen; sie wird auf einen sicheren Bereich begrenzt.</summary>
    public int RetryDelayMilliseconds { get; set; } = 250;

    /// <summary>Grenze je HTTP-Aufruf einschließlich des vollständigen Antwortkörpers.</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>Maximale Gesamtdauer eines vollständigen Imports.</summary>
    public int SynchronizationTimeoutSeconds { get; set; } = 300;

    /// <summary>Puffer nach dem lokalen Timeout, bevor eine andere API-Instanz die Lease übernimmt.</summary>
    public int LeaseGraceSeconds { get; set; } = 30;

    /// <summary>Antwortobergrenze je Keycloak-Seite inklusive Tokenantwort.</summary>
    public int MaxResponseBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>Ein Zugangstoken wird vor Ablauf rechtzeitig erneuert.</summary>
    public int TokenRefreshSkewSeconds { get; set; } = 15;

    /// <summary>Intervall des periodischen Abgleichs nach dem ersten sofortigen Durchlauf.</summary>
    public int SyncIntervalSeconds { get; set; } = 300;

    /// <summary>Prueft nur Struktur und Grenzen; geheime Werte werden nie in die Meldung aufgenommen.</summary>
    public bool IsValid()
    {
        if (!Enabled) return string.IsNullOrEmpty(RootGroupId);
        return IsSafeHttpsUri(ServerUrl)
               && IsSafeHttpsUri(Issuer)
               && !string.IsNullOrWhiteSpace(Realm)
               && !string.IsNullOrWhiteSpace(ClientId)
               && !string.IsNullOrWhiteSpace(ClientSecret)
               && (string.IsNullOrEmpty(RootGroupId) || IsSafeRootGroupId(RootGroupId))
               && PageSize is >= 1 and <= 1_000
               && MaxPages is >= 1 and <= 100_000
               && MaxRetries is >= 0 and <= 5
               && RetryDelayMilliseconds is >= 0 and <= 30_000
               && RequestTimeoutSeconds is >= 1 and <= 300
               && SynchronizationTimeoutSeconds is >= 10 and <= 3_600
               && LeaseGraceSeconds is >= 1 and <= 600
               && MaxResponseBytes is >= 1_024 and <= 32 * 1024 * 1024
               && TokenRefreshSkewSeconds is >= 0 and <= 300
               && SyncIntervalSeconds is >= 10 and <= 86_400;
    }

    // Stabile Provider-IDs werden als genau ein URL-Segment verwendet, nie als
    // Anzeigename oder Pfad. Kein Trimmen, Dekodieren oder toleranter Fallback.
    private static bool IsSafeRootGroupId(string value) => value.Length is >= 1 and <= 256
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    /// <summary>
    /// Opaque User-Storage-IDs dürfen etwa f:provider:externalId, Unicode oder
    /// längere externe Kennungen enthalten. Nur mehrdeutige URL-Segmente schließen;
    /// keine UUID-Form erzwingen, kein Trimmen und keine Unicode-Normalisierung.
    /// HTTP-Body-, Seiten- und Importlimits bleiben unabhängig davon wirksam.
    /// </summary>
    internal static bool IsSafeProviderId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or "..") return false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsWhiteSpace(character) || char.IsControl(character) || character is '/' or '\\' or '%' or '?' or '#')
                return false;
            // EscapeDataString ersetzt ungepaarte Surrogates; dann wäre der
            // angefragte Provider-Schlüssel nicht mehr der gelesene Schlüssel.
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1])) return false;
                index++;
            }
            else if (char.IsLowSurrogate(character)) return false;
        }
        return true;
    }

    private static bool IsSafeHttpsUri(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);
}
