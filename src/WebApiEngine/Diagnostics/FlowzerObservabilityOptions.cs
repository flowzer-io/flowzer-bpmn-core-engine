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
    public FlowzerPrometheusOptions Prometheus { get; set; } = new();

    public bool HasOtlpExporter => !string.IsNullOrWhiteSpace(OtlpEndpoint);

    /// <summary>
    /// Metriken werden gesammelt, sobald irgendein Abnehmer dafür eingeschaltet ist. Der
    /// Prometheus-Scrape-Endpunkt ist ein eigener Abnehmer: Wer nur ihn einschaltet, soll nicht
    /// zusätzlich <c>Observability:Enabled</c> setzen müssen, um Werte zu bekommen.
    /// Traces und die Push-Exporter (Console, OTLP) hängen unverändert an <see cref="Enabled"/>.
    /// </summary>
    public bool CollectsMetrics => Enabled || Prometheus.Enabled;

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

/// <summary>
/// Der Prometheus-Scrape-Endpunkt. Er antwortet ohne Anmeldung, weil Prometheus keine Sitzung
/// mitbringt, und gehört deshalb ausschliesslich ins Containernetz — niemals hinter das
/// öffentliche Gateway. Deswegen ist er auch standardmäßig aus.
/// </summary>
public sealed class FlowzerPrometheusOptions
{
    public const string DefaultPath = "/metrics";

    public bool Enabled { get; set; }
    public string Path { get; set; } = DefaultPath;

    /// <summary>
    /// Normalisiert den konfigurierten Pfad auf die Form, die die Weiterleitungsregeln des
    /// Gateways und die Scrape-Konfiguration erwarten: genau ein führender Schrägstrich, kein
    /// abschliessender. Ein leerer Wert fällt auf <see cref="DefaultPath"/> zurück, statt die
    /// Metriken still auf die Wurzel zu legen.
    /// </summary>
    public string ResolvePath()
    {
        var configuredPath = Path?.Trim();
        if (string.IsNullOrEmpty(configuredPath))
        {
            return DefaultPath;
        }

        if (configuredPath.Contains("://", StringComparison.Ordinal) || configuredPath.Contains('?', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Observability:Prometheus:Path must be a plain path like '{DefaultPath}', not a URI or query string.",
                nameof(Path));
        }

        var normalizedPath = "/" + configuredPath.Trim('/');
        return normalizedPath == "/" ? DefaultPath : normalizedPath;
    }
}

/// <summary>
/// Haelt fest, dass der Prometheus-Exporter registriert wurde und unter welchem Pfad sein
/// Endpunkt entstehen soll. Der Eintrag existiert genau dann, wenn auch der Exporter existiert;
/// Exporter und Endpunkt koennen dadurch nicht auseinanderlaufen.
/// </summary>
internal sealed record FlowzerPrometheusScrapeEndpoint(string Path);
