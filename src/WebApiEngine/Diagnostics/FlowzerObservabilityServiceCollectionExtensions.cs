using Microsoft.Extensions.Options;
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

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: options.ServiceName,
                serviceVersion: options.ResolveServiceVersion()))
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(FlowzerDiagnostics.MeterName);
                metrics.AddAspNetCoreInstrumentation();
                ConfigureExporters(metrics, options);
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(FlowzerDiagnostics.ActivitySourceName);
                tracing.AddAspNetCoreInstrumentation();
                ConfigureExporters(tracing, options);
            });

        return services;
    }

    private static void ConfigureExporters(MeterProviderBuilder metrics, FlowzerObservabilityOptions options)
    {
        if (options.UseConsoleExporter)
        {
            metrics.AddConsoleExporter();
        }

        if (options.HasOtlpExporter)
        {
            metrics.AddOtlpExporter(exporter =>
            {
                exporter.Endpoint = new Uri(options.OtlpEndpoint!, UriKind.Absolute);
                exporter.Protocol = options.ResolveOtlpProtocol();
                if (!string.IsNullOrWhiteSpace(options.OtlpHeaders))
                {
                    exporter.Headers = options.OtlpHeaders;
                }
            });
        }
    }

    private static void ConfigureExporters(TracerProviderBuilder tracing, FlowzerObservabilityOptions options)
    {
        if (options.UseConsoleExporter)
        {
            tracing.AddConsoleExporter();
        }

        if (options.HasOtlpExporter)
        {
            tracing.AddOtlpExporter(exporter =>
            {
                exporter.Endpoint = new Uri(options.OtlpEndpoint!, UriKind.Absolute);
                exporter.Protocol = options.ResolveOtlpProtocol();
                if (!string.IsNullOrWhiteSpace(options.OtlpHeaders))
                {
                    exporter.Headers = options.OtlpHeaders;
                }
            });
        }
    }
}
