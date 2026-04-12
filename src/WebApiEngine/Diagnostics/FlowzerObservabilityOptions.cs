using OpenTelemetry.Exporter;

namespace WebApiEngine.Diagnostics;

/// <summary>
/// Steuerbare Observability-Optionen für lokale und produktionsnahe Umgebungen.
/// Das Paket hält die Defaults bewusst defensiv, damit bestehende Dev- und CI-Pfade
/// ohne zusätzliche Exporter unverändert weiterlaufen.
/// </summary>
public sealed class FlowzerObservabilityOptions
{
    public const string SectionName = "Observability";

    public bool Enabled { get; set; }
    public bool UseConsoleExporter { get; set; }
    public string? OtlpEndpoint { get; set; }
    public string? OtlpHeaders { get; set; }
    public string OtlpProtocol { get; set; } = "grpc";
    public string ServiceName { get; set; } = FlowzerDiagnostics.MeterName;
    public string? ServiceVersion { get; set; }

    public bool HasOtlpExporter => !string.IsNullOrWhiteSpace(OtlpEndpoint);

    public OtlpExportProtocol ResolveOtlpProtocol()
    {
        return OtlpProtocol.Trim().ToLowerInvariant() switch
        {
            "grpc" => OtlpExportProtocol.Grpc,
            "http/protobuf" or "httpprotobuf" or "http-protobuf" => OtlpExportProtocol.HttpProtobuf,
            _ => throw new ArgumentException(
                $"Unsupported Observability:OtlpProtocol value '{OtlpProtocol}'. Use 'grpc' or 'http/protobuf'.",
                nameof(OtlpProtocol))
        };
    }

    public string ResolveServiceVersion()
    {
        if (!string.IsNullOrWhiteSpace(ServiceVersion))
        {
            return ServiceVersion;
        }

        return typeof(FlowzerObservabilityOptions).Assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
