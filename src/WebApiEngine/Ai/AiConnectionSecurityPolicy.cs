using System.Net;
using Model;

namespace WebApiEngine.Ai;

/// <summary>
/// Gemeinsame Zielgrenze fuer Administration und Ausfuehrung. Persistierte Daten werden beim
/// Lauf erneut geprueft, damit Legacy- oder manipulierte Eintraege keine SSRF-Grenze umgehen.
/// </summary>
internal static class AiConnectionSecurityPolicy
{
    public static string? ValidateAndNormalizeTarget(
        AiProviderKind provider,
        AiProcessingLocation location,
        string? baseAddress,
        FlowzerAiOptions options)
    {
        if (!Enum.IsDefined(provider) || !Enum.IsDefined(location))
            throw new ArgumentException("The AI connection kind is not supported.");
        if (location == AiProcessingLocation.Cloud && !options.AllowCloudProviders)
            throw new ArgumentException("Cloud AI providers are not enabled for this installation.", nameof(location));
        if (location == AiProcessingLocation.Local && !options.AllowLocalEndpoints)
            throw new ArgumentException("Local AI endpoints are not enabled for this installation.", nameof(location));
        if (provider is AiProviderKind.OpenAi or AiProviderKind.Anthropic
            && location != AiProcessingLocation.Cloud)
            throw new ArgumentException("This provider is available only as an explicit cloud connection.", nameof(location));

        if (provider is AiProviderKind.OpenAi or AiProviderKind.Anthropic)
        {
            if (!string.IsNullOrWhiteSpace(baseAddress))
                throw new ArgumentException("BaseAddress is fixed for this provider.", nameof(baseAddress));
            return null;
        }

        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException(
                "BaseAddress must be an absolute HTTP(S) URL without credentials, query or fragment.",
                nameof(baseAddress));

        if (location == AiProcessingLocation.Cloud
            && (uri.Scheme != Uri.UriSchemeHttps || IsLocalHost(uri.Host)))
            throw new ArgumentException("Cloud BaseAddress must use HTTPS and a non-local host.", nameof(baseAddress));

        return uri.AbsoluteUri.TrimEnd('/');
    }

    private static bool IsLocalHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!IPAddress.TryParse(host, out var address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.Equals(IPAddress.Any)
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.None)
            || address.Equals(IPAddress.IPv6None)
            || address.IsIPv6Multicast)
            return true;
        var bytes = address.GetAddressBytes();
        return address.AddressFamily switch
        {
            System.Net.Sockets.AddressFamily.InterNetwork =>
                bytes[0] == 10
                || bytes[0] == 127
                || bytes[0] == 169 && bytes[1] == 254
                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                || bytes[0] == 192 && bytes[1] == 168,
            System.Net.Sockets.AddressFamily.InterNetworkV6 =>
                address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                // RFC 4193 Unique Local Addresses (fc00::/7).
                || (bytes[0] & 0xfe) == 0xfc,
            _ => true
        };
    }
}
