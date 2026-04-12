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

        if (!options.Enabled)
        {
            return services;
        }

        var otlpEndpoint = ResolveOtlpEndpoint(options);
        var otlpProtocol = options.HasOtlpExporter
            ? options.ResolveOtlpProtocol()
            : (OpenTelemetry.Exporter.OtlpExportProtocol?)null;

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: options.ServiceName,
                serviceVersion: options.ResolveServiceVersion()))
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(FlowzerDiagnostics.MeterName);
                metrics.AddAspNetCoreInstrumentation();
                ConfigureExporters(metrics, options, otlpEndpoint, otlpProtocol);
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(FlowzerDiagnostics.ActivitySourceName);
                tracing.AddAspNetCoreInstrumentation();
                ConfigureExporters(tracing, options, otlpEndpoint, otlpProtocol);
            });

        return services;
    }

    private static void ConfigureExporters(
        MeterProviderBuilder metrics,
        FlowzerObservabilityOptions options,
        Uri? otlpEndpoint,
        OpenTelemetry.Exporter.OtlpExportProtocol? otlpProtocol)
    {
        if (options.UseConsoleExporter)
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
