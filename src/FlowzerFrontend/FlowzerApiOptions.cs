namespace FlowzerFrontend;

/// <summary>
/// Zentrale Frontend-Konfiguration für den Zugriff auf die Flowzer-Web-API.
/// </summary>
public sealed class FlowzerApiOptions
{
    public const string SectionName = "FlowzerApi";
    public const string DevelopmentUserIdHeaderName = "X-Flowzer-UserId";

    /// <summary>
    /// Optionale Zieladresse der Web-API. Leere Werte fallen auf die Host-Basisadresse
    /// des Frontends zurück und unterstützen damit Reverse-Proxy- oder Same-Origin-Setups.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optionaler technischer Benutzer für lokale Development-Umgebungen. Wird nur dann als
    /// Request-Header gesetzt, wenn das Frontend selbst im Development-Modus läuft.
    /// </summary>
    public string? DevelopmentUserId { get; set; }

    /// <summary>
    /// Ermittelt die effektive API-Basisadresse aus Host-URL und optionaler Konfiguration.
    /// </summary>
    public Uri ResolveBaseAddress(string hostBaseAddress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostBaseAddress);

        var normalizedHostBaseAddress = EnsureTrailingSlash(new Uri(hostBaseAddress, UriKind.Absolute));
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            return normalizedHostBaseAddress;
        }

        if (Uri.TryCreate(BaseUrl, UriKind.Absolute, out var absoluteBaseAddress) &&
            (absoluteBaseAddress.Scheme == Uri.UriSchemeHttp ||
             absoluteBaseAddress.Scheme == Uri.UriSchemeHttps))
        {
            return EnsureTrailingSlash(absoluteBaseAddress);
        }

        var normalizedRelativeBaseAddress = BaseUrl.TrimStart('/');
        return string.IsNullOrWhiteSpace(normalizedRelativeBaseAddress)
            ? normalizedHostBaseAddress
            : EnsureTrailingSlash(
                new Uri(
                    normalizedHostBaseAddress,
                    new Uri(normalizedRelativeBaseAddress, UriKind.Relative)));
    }

    /// <summary>
    /// Liefert den konfigurierten technischen Development-Benutzer nur dann zurück, wenn die
    /// aktuelle Frontend-Instanz tatsächlich im Development-Modus läuft.
    /// </summary>
    public Guid? ResolveDevelopmentUserId(bool isDevelopment)
    {
        if (!isDevelopment || !Guid.TryParse(DevelopmentUserId, out var developmentUserId))
        {
            return null;
        }

        return developmentUserId;
    }

    /// <summary>
    /// Ergänzt den technischen Development-Header auf dem HttpClient, falls er für die
    /// aktuelle Umgebung konfiguriert und erlaubt ist.
    /// </summary>
    public void ApplyDefaultHeaders(HttpClient httpClient, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        var developmentUserId = ResolveDevelopmentUserId(isDevelopment);
        if (developmentUserId is null)
        {
            return;
        }

        httpClient.DefaultRequestHeaders.Remove(DevelopmentUserIdHeaderName);
        httpClient.DefaultRequestHeaders.Add(
            DevelopmentUserIdHeaderName,
            developmentUserId.Value.ToString());
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var absoluteUri = uri.AbsoluteUri;
        return absoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri($"{absoluteUri}/", UriKind.Absolute);
    }
}
