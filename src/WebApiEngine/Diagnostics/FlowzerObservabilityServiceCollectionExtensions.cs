using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace WebApiEngine.Diagnostics;

/// <summary>
/// Kapselt die optionale OpenTelemetry-Verdrahtung, damit Program.cs lesbar bleibt
/// und Diagnosedaten dieselben stabilen Konfigurationswerte wiedergeben können.
/// </summary>
public static class FlowzerObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddFlowzerObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FlowzerObservabilityOptions>(configuration.GetSection(FlowzerObservabilityOptions.SectionName));

        var options = configuration.GetSection(FlowzerObservabilityOptions.SectionName).Get<FlowzerObservabilityOptions>()
                      ?? new FlowzerObservabilityOptions();

        if (!options.CollectsMetrics)
        {
            return services;
        }

        // Die OTLP-Werte werden nur geprueft, wenn sie auch benutzt werden. Sonst wuerde ein
        // eingeschalteter Scrape-Endpunkt eine alte, abgeschaltete OTLP-Konfiguration nachtraeglich
        // zum Startfehler machen.
        var otlpEndpoint = options.Enabled ? ResolveOtlpEndpoint(options) : null;
        var otlpProtocol = options.Enabled && options.HasOtlpExporter
            ? options.ResolveOtlpProtocol()
            : (OpenTelemetry.Exporter.OtlpExportProtocol?)null;

        // Ein ungueltiger Pfad soll beim Start auffallen und nicht erst, wenn Prometheus das
        // erste Mal vergeblich scrapt.
        var prometheusPath = options.Prometheus.Enabled ? options.Prometheus.ResolvePath() : null;

        if (prometheusPath is not null)
        {
            // Der Pfad wird hier festgehalten und spaeter genau so gemappt. Wuerde das Mappen die
            // Konfiguration erneut lesen, koennten Exporter und Endpunkt auseinanderlaufen — der
            // Endpunkt entstuende dann ohne den Exporter, den er braucht.
            services.AddSingleton(new FlowzerPrometheusScrapeEndpoint(prometheusPath));
        }

        var openTelemetry = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: options.ServiceName,
                serviceVersion: options.ResolveServiceVersion()))
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(FlowzerDiagnostics.MeterName);
                metrics.AddAspNetCoreInstrumentation();
                ConfigureExporters(metrics, options, otlpEndpoint, otlpProtocol);

                if (prometheusPath is not null)
                {
                    // Der Pfad wird nicht hier, sondern beim Mappen des Endpunkts gesetzt:
                    // MapPrometheusScrapingEndpoint bekommt ihn ausdruecklich uebergeben.
                    metrics.AddPrometheusExporter();
                }
            });

        // Traces und die Push-Exporter bleiben an Observability:Enabled gebunden. Wer nur den
        // Scrape-Endpunkt einschaltet, bekommt Metriken und sonst nichts.
        if (options.Enabled)
        {
            openTelemetry.WithTracing(tracing =>
            {
                tracing.AddSource(FlowzerDiagnostics.ActivitySourceName);
                tracing.AddAspNetCoreInstrumentation();
                ConfigureExporters(tracing, options, otlpEndpoint, otlpProtocol);
            });
        }

        return services;
    }

    /// <summary>
    /// Mappt den Prometheus-Scrape-Endpunkt, wenn er konfiguriert ist. Er ist bewusst anonym und
    /// ohne Kontingent: Prometheus bringt keine Sitzung mit, und ein regelmaessiger Scrape darf
    /// nicht gegen das Anfragefenster eines Aufrufers laufen. Genau deshalb darf der Pfad nur im
    /// Containernetz erreichbar sein — das Konsolen-Gateway leitet ihn nicht weiter
    /// (siehe tests/ui-smoke/check-gateway-routes.sh).
    /// </summary>
    public static WebApplication MapFlowzerPrometheusScrapingEndpoint(this WebApplication app)
    {
        var scrapeEndpoint = app.Services.GetService<FlowzerPrometheusScrapeEndpoint>();

        if (scrapeEndpoint is null)
        {
            return app;
        }

        app.MapPrometheusScrapingEndpoint(scrapeEndpoint.Path)
            .AllowAnonymous()
            .DisableRateLimiting();

        return app;
    }

    private static void ConfigureExporters(
        MeterProviderBuilder metrics,
        FlowzerObservabilityOptions options,
        Uri? otlpEndpoint,
        OpenTelemetry.Exporter.OtlpExportProtocol? otlpProtocol)
    {
        if (options.Enabled && options.UseConsoleExporter)
        {
            metrics.AddConsoleExporter();
        }

        if (otlpEndpoint is not null && otlpProtocol is not null)
        {
            metrics.AddOtlpExporter(exporter =>
            {
                exporter.Endpoint = otlpEndpoint;
                exporter.Protocol = otlpProtocol.Value;
                if (!string.IsNullOrWhiteSpace(options.OtlpHeaders))
                {
                    exporter.Headers = options.OtlpHeaders;
                }
            });
        }
    }

    private static void ConfigureExporters(
        TracerProviderBuilder tracing,
        FlowzerObservabilityOptions options,
        Uri? otlpEndpoint,
        OpenTelemetry.Exporter.OtlpExportProtocol? otlpProtocol)
    {
        if (options.UseConsoleExporter)
        {
            tracing.AddConsoleExporter();
        }

        if (otlpEndpoint is not null && otlpProtocol is not null)
        {
            tracing.AddOtlpExporter(exporter =>
            {
                exporter.Endpoint = otlpEndpoint;
                exporter.Protocol = otlpProtocol.Value;
                if (!string.IsNullOrWhiteSpace(options.OtlpHeaders))
                {
                    exporter.Headers = options.OtlpHeaders;
                }
            });
        }
    }

    private static Uri? ResolveOtlpEndpoint(FlowzerObservabilityOptions options)
    {
        if (!options.HasOtlpExporter)
        {
            return null;
        }

        if (Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out var endpoint)
            && (endpoint.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                || endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return endpoint;
        }

        throw new ArgumentException(
            "Observability:OtlpEndpoint must be an absolute http(s) URI like 'http://localhost:4318' when Observability is enabled.",
            nameof(options));
    }
}
