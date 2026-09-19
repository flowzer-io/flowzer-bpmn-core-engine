using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Npgsql;
using PostgreSqlStorageSystem;
using WebApiEngine.Ai;
using WebApiEngine.Auth;
using WebApiEngine.Jobs;
using WebApiEngine.Limits;
using WebApiEngine.Persistence;

namespace WebApiEngine.Diagnostics;

/// <summary>Bewertung eines geprueften Bereichs.</summary>
public enum ConfigurationCheckState
{
    Ok,
    Warning,
    Error
}

/// <summary>Eine Zeile der Pruefausgabe: Bereich, Zustand und ein lesbarer Hinweis.</summary>
public sealed record ConfigurationCheckRow(string Area, ConfigurationCheckState State, string Hint);

/// <summary>
/// Startargument <c>--check-config</c>: prueft die Konfiguration einer Installation, ohne die
/// API zu starten. Der Host wird wie beim normalen Start gebaut (Optionsbindung und
/// <c>ValidateOnStart</c>); zusaetzlich werden Ablage, Migrationsstand, Authority und die
/// Freigabelisten geprueft. Das Ergebnis ist eine Tabelle und ein Exit-Code:
/// 0 alles in Ordnung, 1 mindestens ein Fehler, 2 nur Warnungen.
/// Es werden ausschliesslich nicht geheime Werte ausgegeben - keine Verbindungszeichenfolgen,
/// keine Client-Secrets, keine Tokens.
/// </summary>
public static class ConfigurationCheck
{
    public const string Argument = "--check-config";

    public const int ExitCodeOk = 0;
    public const int ExitCodeError = 1;
    public const int ExitCodeWarning = 2;

    /// <summary>Zeitfenster fuer Netzwerkproben; eine Pruefung darf nicht haengen bleiben.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private const string AreaConfiguration = "Konfiguration";
    private const string AreaStorage = "Ablage";
    private const string AreaMigrations = "Migrationen";
    private const string AreaAuthentication = "Authentifizierung";
    private const string AreaWebhooks = "Webhook-Ziele";
    private const string AreaAi = "KI-Datenfluss";
    private const string AreaLimits = "Grenzen";
    private const string AreaForwardedHeaders = "Proxy-Vertrauen";
    private const string AreaObservability = "Beobachtbarkeit";

    public static bool IsConfigurationCheckRun(string[] args) =>
        args.Any(argument => string.Equals(argument, Argument, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Prueft den bereits gebauten Host und schreibt die Tabelle. Rueckgabe ist der Exit-Code.
    /// </summary>
    public static async Task<int> RunAsync(IServiceProvider services, TextWriter output, CancellationToken cancellationToken = default)
    {
        var rows = await CollectAsync(services, cancellationToken);
        return Report(rows, output);
    }

    /// <summary>Schreibt eine bereits ermittelte Tabelle und liefert den passenden Exit-Code.</summary>
    public static int Report(IReadOnlyList<ConfigurationCheckRow> rows, TextWriter output)
    {
        Render(rows, output);
        return ExitCodeFor(rows);
    }

    public static int ExitCodeFor(IReadOnlyList<ConfigurationCheckRow> rows)
    {
        if (rows.Any(row => row.State == ConfigurationCheckState.Error))
        {
            return ExitCodeError;
        }

        return rows.Any(row => row.State == ConfigurationCheckState.Warning)
            ? ExitCodeWarning
            : ExitCodeOk;
    }

    /// <summary>
    /// Einige Abschnitte validieren bereits beim Registrieren der Dienste und lassen den Host
    /// scheitern, bevor die Pruefung ueberhaupt laeuft. Fuer <c>--check-config</c> wird genau
    /// dieselbe Registrierung deshalb vorab gegen eine Wegwerf-Sammlung ausgefuehrt: So bleibt
    /// die Regel an einer Stelle, und aus dem Abbruch wird eine benannte Zeile statt einer
    /// rohen Ausnahme.
    /// </summary>
    public static bool TryDescribeEagerOptionFailure(IConfiguration configuration, out ConfigurationCheckRow failure)
    {
        foreach (var (area, register) in EagerRegistrations)
        {
            try
            {
                register(new ServiceCollection(), configuration);
            }
            catch (Exception exception)
            {
                failure = new ConfigurationCheckRow(area, ConfigurationCheckState.Error, Flatten(exception));
                return true;
            }
        }

        failure = new ConfigurationCheckRow(AreaConfiguration, ConfigurationCheckState.Ok, string.Empty);
        return false;
    }

    private static readonly (string Area, Action<IServiceCollection, IConfiguration> Register)[] EagerRegistrations =
    [
        (AreaStorage, (services, configuration) => services.AddFlowzerStorage(configuration)),
        (AreaAuthentication, (services, configuration) => services.AddFlowzerAuthentication(configuration)),
        (AreaLimits, (services, configuration) => services.AddFlowzerLimits(configuration)),
        (AreaForwardedHeaders, (services, configuration) => services.AddFlowzerForwardedHeaders(configuration)),
        (AreaObservability, (services, configuration) => services.AddFlowzerObservability(configuration))
    ];

    public static async Task<IReadOnlyList<ConfigurationCheckRow>> CollectAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<ConfigurationCheckRow> { CheckOptions(services) };

        var storageOptions = services.GetRequiredService<FlowzerStorageOptions>();
        if (storageOptions.IsPostgreSql)
        {
            rows.Add(await CheckPostgreSqlStorageAsync(storageOptions, cancellationToken));
            rows.Add(await CheckMigrationsAsync(storageOptions, cancellationToken));
        }
        else
        {
            rows.Add(CheckFilesystemStorage());
        }

        rows.Add(await CheckAuthenticationAsync(services, cancellationToken));
        rows.Add(CheckWebhookAllowList(services));
        rows.Add(CheckAiBoundaries(services));
        return rows;
    }

    /// <summary>
    /// Zieht dieselbe Validierung, die der normale Start beim Hochlaufen ausloest. Damit sind
    /// Bindung und <c>ValidateOnStart</c> aller Optionsabschnitte abgedeckt.
    /// </summary>
    private static ConfigurationCheckRow CheckOptions(IServiceProvider services)
    {
        try
        {
            services.GetRequiredService<IStartupValidator>().Validate();
            return new ConfigurationCheckRow(AreaConfiguration, ConfigurationCheckState.Ok,
                "Alle Optionsabschnitte gebunden und validiert.");
        }
        catch (Exception exception)
        {
            return new ConfigurationCheckRow(AreaConfiguration, ConfigurationCheckState.Error, Flatten(exception));
        }
    }

    private static ConfigurationCheckRow CheckFilesystemStorage()
    {
        string root;
        try
        {
            root = FilesystemStorageSystem.Storage.ResolveStorageRoot();
        }
        catch (Exception exception)
        {
            return new ConfigurationCheckRow(AreaStorage, ConfigurationCheckState.Error,
                $"Wurzelverzeichnis der Dateiablage nicht bestimmbar: {exception.Message}");
        }

        try
        {
            Directory.CreateDirectory(root);
            // Nur ein tatsaechlicher Schreibversuch beweist das Schreibrecht; ein Existenztest
            // uebersieht schreibgeschuetzte Mounts und fremde Besitzverhaeltnisse.
            var probe = Path.Combine(root, $".flowzer-check-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return new ConfigurationCheckRow(AreaStorage, ConfigurationCheckState.Ok,
                $"Dateiablage beschreibbar: {root}");
        }
        catch (Exception exception)
        {
            return new ConfigurationCheckRow(AreaStorage, ConfigurationCheckState.Error,
                $"Dateiablage {root} ist nicht beschreibbar: {exception.Message}");
        }
    }

    private static async Task<ConfigurationCheckRow> CheckPostgreSqlStorageAsync(
        FlowzerStorageOptions options,
        CancellationToken cancellationToken)
    {
        if (!TryDescribeConnection(options.PostgreSql.ConnectionString, out var description, out var probeConnectionString, out var error))
        {
            return new ConfigurationCheckRow(AreaStorage, ConfigurationCheckState.Error,
                $"Laufzeitverbindung unbrauchbar: {error}");
        }

        try
        {
            await using var connection = new NpgsqlConnection(probeConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = @schema)", connection);
            command.Parameters.AddWithValue("schema", options.PostgreSql.Schema);
            var schemaExists = await command.ExecuteScalarAsync(cancellationToken) is true;

            return schemaExists
                ? new ConfigurationCheckRow(AreaStorage, ConfigurationCheckState.Ok,
                    $"PostgreSQL erreichbar ({description}), Schema {options.PostgreSql.Schema} vorhanden.")
                : new ConfigurationCheckRow(AreaStorage, ConfigurationCheckState.Warning,
                    $"PostgreSQL erreichbar ({description}), Schema {options.PostgreSql.Schema} fehlt noch - Migration ausstehend.");
        }
        catch (Exception exception)
        {
            return new ConfigurationCheckRow(AreaStorage, ConfigurationCheckState.Error,
                $"PostgreSQL nicht erreichbar ({description}): {Flatten(exception)}");
        }
    }

    private static async Task<ConfigurationCheckRow> CheckMigrationsAsync(
        FlowzerStorageOptions options,
        CancellationToken cancellationToken)
    {
        // Bewusst mit der Migrationsidentitaet: so belegt die Pruefung zugleich, dass der
        // getrennte Migrationsschritt spaeter ueberhaupt anmelden kann.
        if (!TryDescribeConnection(options.PostgreSql.ResolveMigrationConnectionString(), out _, out var probeConnectionString, out var error))
        {
            return new ConfigurationCheckRow(AreaMigrations, ConfigurationCheckState.Error,
                $"Migrationsverbindung unbrauchbar: {error}");
        }

        try
        {
            var status = await PostgreSqlMigrator.GetStatusAsync(probeConnectionString, options.PostgreSql.Schema, cancellationToken);
            if (status.IsUpToDate)
            {
                return new ConfigurationCheckRow(AreaMigrations, ConfigurationCheckState.Ok,
                    $"aktuell ({status.Applied.Count} angewendet, hoechste Version {status.Available.LastOrDefault()}).");
            }

            var pending = status.Pending;
            return new ConfigurationCheckRow(AreaMigrations, ConfigurationCheckState.Warning,
                $"{pending.Count} ausstehend ({string.Join(", ", pending)}) - `dotnet WebApiEngine.dll --migrate` vor dem Start ausfuehren.");
        }
        catch (Exception exception)
        {
            return new ConfigurationCheckRow(AreaMigrations, ConfigurationCheckState.Error,
                $"Migrationsstand nicht lesbar: {Flatten(exception)}");
        }
    }

    private static async Task<ConfigurationCheckRow> CheckAuthenticationAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        var options = services.GetRequiredService<FlowzerAuthenticationOptions>();
        if (!options.IsAuthenticationEnabled)
        {
            return new ConfigurationCheckRow(AreaAuthentication, ConfigurationCheckState.Warning,
                $"Schema {options.Scheme}: die API ist ungeschuetzt und nur fuer lokale Pruefungen geeignet.");
        }

        if (!Uri.TryCreate(options.JwtBearer.Authority, UriKind.Absolute, out var authority))
        {
            return new ConfigurationCheckRow(AreaAuthentication, ConfigurationCheckState.Error,
                $"Schema {options.Scheme}: Authority ist keine absolute Adresse.");
        }

        var discovery = new Uri(authority.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration");
        // Nur Namensaufloesung und ein HEAD auf das Discovery-Dokument. Es werden weder
        // Anmeldedaten mitgeschickt noch Antwortinhalte gelesen oder protokolliert.
        try
        {
            await System.Net.Dns.GetHostAddressesAsync(authority.Host, cancellationToken);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            return new ConfigurationCheckRow(AreaAuthentication, ConfigurationCheckState.Error,
                $"Schema {options.Scheme}: Authority-Host {authority.Host} ist nicht aufloesbar.");
        }

        try
        {
            using var client = new HttpClient { Timeout = ProbeTimeout };
            using var request = new HttpRequestMessage(HttpMethod.Head, discovery);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode
                ? new ConfigurationCheckRow(AreaAuthentication, ConfigurationCheckState.Ok,
                    $"Schema {options.Scheme}, Authority {authority.Host} antwortet auf die OIDC-Discovery.")
                : new ConfigurationCheckRow(AreaAuthentication, ConfigurationCheckState.Warning,
                    $"Schema {options.Scheme}: Authority {authority.Host} antwortet auf die OIDC-Discovery mit {(int)response.StatusCode}.");
        }
        catch (Exception exception)
        {
            return new ConfigurationCheckRow(AreaAuthentication, ConfigurationCheckState.Warning,
                $"Schema {options.Scheme}: OIDC-Discovery bei {authority.Host} nicht erreichbar ({Flatten(exception)}).");
        }
    }

    private static ConfigurationCheckRow CheckWebhookAllowList(IServiceProvider services)
    {
        var options = services.GetRequiredService<FlowzerWebhookOptions>();
        if (!options.Enabled)
        {
            return new ConfigurationCheckRow(AreaWebhooks, ConfigurationCheckState.Ok,
                "abgeschaltet; es werden keine ausgehenden Benachrichtigungen versendet.");
        }

        // Absichtlich nur die Anzahl: die Zieladressen einer Installation gehoeren nicht in
        // eine Ausgabe, die in Tickets und Logs landet.
        var allowed = options.AllowedHosts.Count(host => !string.IsNullOrWhiteSpace(host));
        if (allowed == 0)
        {
            return new ConfigurationCheckRow(AreaWebhooks, ConfigurationCheckState.Warning,
                "aktiviert, aber ohne freigegebenes Ziel - jede Webhook-Anmeldung wird abgewiesen.");
        }

        return new ConfigurationCheckRow(AreaWebhooks, ConfigurationCheckState.Ok,
            $"aktiviert mit {allowed} freigegebenen Ziel(en), HTTP {(options.AllowHttp ? "erlaubt" : "gesperrt")}.");
    }

    private static ConfigurationCheckRow CheckAiBoundaries(IServiceProvider services)
    {
        var ai = services.GetRequiredService<IOptions<FlowzerAiOptions>>().Value;
        var execution = services.GetRequiredService<IOptions<AiRunExecutionOptions>>().Value;
        var summary =
            $"Cloud {(ai.AllowCloudProviders ? "erlaubt" : "gesperrt")}, "
            + $"lokale Endpunkte {(ai.AllowLocalEndpoints ? "erlaubt" : "gesperrt")}, "
            + $"Ausfuehrung {(execution.Enabled ? "aktiv" : "aus")}.";

        if (execution.Enabled && !ai.AllowCloudProviders && !ai.AllowLocalEndpoints)
        {
            return new ConfigurationCheckRow(AreaAi, ConfigurationCheckState.Warning,
                summary + " Kein Zielbereich freigegeben; jeder Lauf endet als abgewiesen.");
        }

        return new ConfigurationCheckRow(AreaAi, ConfigurationCheckState.Ok, summary);
    }

    /// <summary>
    /// Zerlegt eine Verbindungszeichenfolge in eine nicht geheime Kurzbeschreibung und ergaenzt
    /// knappe Zeitgrenzen fuer die Probe. Das Passwort wird nie ausgegeben.
    /// </summary>
    private static bool TryDescribeConnection(
        string connectionString,
        out string description,
        out string probeConnectionString,
        out string error)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString)
            {
                Timeout = (int)ProbeTimeout.TotalSeconds,
                CommandTimeout = (int)ProbeTimeout.TotalSeconds
            };
            description = $"{builder.Host}:{builder.Port}/{builder.Database}";
            probeConnectionString = builder.ConnectionString;
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            description = "unbekannt";
            probeConnectionString = string.Empty;
            error = exception.Message;
            return false;
        }
    }

    private static void Render(IReadOnlyList<ConfigurationCheckRow> rows, TextWriter output)
    {
        const string areaHeader = "Bereich";
        const string stateHeader = "Zustand";
        var areaWidth = Math.Max(areaHeader.Length, rows.Max(row => row.Area.Length));
        var stateWidth = Math.Max(stateHeader.Length, rows.Max(row => Label(row.State).Length));

        output.WriteLine("Flowzer Konfigurationspruefung");
        output.WriteLine();
        output.WriteLine($"{areaHeader.PadRight(areaWidth)}  {stateHeader.PadRight(stateWidth)}  Hinweis");
        output.WriteLine($"{new string('-', areaWidth)}  {new string('-', stateWidth)}  {new string('-', 7)}");
        foreach (var row in rows)
        {
            output.WriteLine($"{row.Area.PadRight(areaWidth)}  {Label(row.State).PadRight(stateWidth)}  {row.Hint}");
        }

        var errors = rows.Count(row => row.State == ConfigurationCheckState.Error);
        var warnings = rows.Count(row => row.State == ConfigurationCheckState.Warning);
        output.WriteLine();
        output.WriteLine(errors > 0
            ? $"Ergebnis: {errors} Fehler, {warnings} Warnung(en) - die Installation ist nicht startbereit."
            : warnings > 0
                ? $"Ergebnis: {warnings} Warnung(en) - Start moeglich, die Hinweise gehoeren aber geklaert."
                : "Ergebnis: keine Beanstandungen.");
        output.Flush();
    }

    private static string Label(ConfigurationCheckState state) => state switch
    {
        ConfigurationCheckState.Ok => "OK",
        ConfigurationCheckState.Warning => "Warnung",
        _ => "Fehler"
    };

    /// <summary>Fasst verschachtelte Ausnahmen zu einer Zeile zusammen, ohne Stapelabbild.</summary>
    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AggregateException aggregate)
            {
                messages.AddRange(aggregate.InnerExceptions.Select(inner => inner.Message));
                continue;
            }

            messages.Add(current.Message);
        }

        return string.Join(" | ", messages.Distinct()).ReplaceLineEndings(" ");
    }
}
