using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace WebApiEngine.Limits;

/// <summary>
/// Auswertung der Weiterleitungsheader. Hinter einem Reverse Proxy ist
/// <c>RemoteIpAddress</c> die Adresse des Proxys; erst mit dieser Auswertung steht die
/// Adresse des Aufrufers im Kontext. Das entscheidet darueber, ob das Anfragekontingent je
/// Aufrufer oder fuer alle gemeinsam gilt.
/// </summary>
public sealed class FlowzerForwardedHeadersOptions
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// Netze, aus denen Weiterleitungsheader geglaubt werden, in CIDR-Schreibweise.
    /// Absichtlich ohne Standardwert: Wer den Header von beliebigen Absendern annimmt, laesst
    /// jeden seine Adresse frei waehlen und damit das Kontingent umgehen.
    /// </summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>Einzelne Proxy-Adressen, wenn kein ganzes Netz gemeint ist.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>
    /// Maximale Zahl explizit vertrauter Proxy-Stufen. Der Standard bleibt fuer
    /// direkte Installationen bei einer Stufe; der mitgelieferte Containerpfad setzt
    /// diesen Wert passend zu TLS-Proxy, Gateway und Konsolen-nginx auf drei.
    /// </summary>
    public int ForwardLimit { get; set; } = 1;

    public bool IsEnabled => KnownNetworks.Length > 0 || KnownProxies.Length > 0;
}

public static class FlowzerForwardedHeadersExtensions
{
    public static IServiceCollection AddFlowzerForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(FlowzerForwardedHeadersOptions.SectionName).Get<FlowzerForwardedHeadersOptions>()
                      ?? new FlowzerForwardedHeadersOptions();
        services.AddSingleton(options);

        if (!options.IsEnabled)
        {
            return services;
        }

        if (options.ForwardLimit is < 1 or > 10)
        {
            throw new InvalidOperationException(
                $"{FlowzerForwardedHeadersOptions.SectionName}:ForwardLimit must be between 1 and 10.");
        }

        services.Configure<ForwardedHeadersOptions>(forwarded =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            forwarded.ForwardLimit = options.ForwardLimit;

            // Die Standardliste erlaubt nur die Loopback-Adresse; der Proxy laeuft im
            // Containernetz. Beide Listen werden deshalb bewusst geleert und neu gefuellt.
            forwarded.KnownNetworks.Clear();
            forwarded.KnownProxies.Clear();

            foreach (var network in options.KnownNetworks)
            {
                var parts = network.Split('/', StringSplitOptions.TrimEntries);
                if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var prefix) || !int.TryParse(parts[1], out var length))
                {
                    throw new InvalidOperationException(
                        $"{FlowzerForwardedHeadersOptions.SectionName}:KnownNetworks entry '{network}' is not a valid CIDR range.");
                }

                forwarded.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(prefix, length));
            }

            foreach (var proxy in options.KnownProxies)
            {
                if (!IPAddress.TryParse(proxy, out var address))
                {
                    throw new InvalidOperationException(
                        $"{FlowzerForwardedHeadersOptions.SectionName}:KnownProxies entry '{proxy}' is not a valid IP address.");
                }

                forwarded.KnownProxies.Add(address);
            }
        });

        return services;
    }

    public static IApplicationBuilder UseFlowzerForwardedHeaders(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<FlowzerForwardedHeadersOptions>();
        if (options.IsEnabled)
        {
            app.UseForwardedHeaders();
        }

        return app;
    }
}
