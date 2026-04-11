namespace FlowzerFrontend;

/// <summary>
/// Zentrale Frontend-Konfiguration für den Zugriff auf die Flowzer-Web-API.
/// </summary>
public sealed class FlowzerApiOptions
{
    public const string SectionName = "FlowzerApi";

    /// <summary>
    /// Optionale Zieladresse der Web-API. Leere Werte fallen auf die Host-Basisadresse
    /// des Frontends zurück und unterstützen damit Reverse-Proxy- oder Same-Origin-Setups.
    /// </summary>
    public string? BaseUrl { get; set; }

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

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var absoluteUri = uri.AbsoluteUri;
        return absoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri($"{absoluteUri}/", UriKind.Absolute);
    }
}
