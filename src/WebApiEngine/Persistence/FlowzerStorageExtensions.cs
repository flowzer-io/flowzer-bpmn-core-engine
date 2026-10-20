using Npgsql;
using PostgreSqlStorageSystem;

namespace WebApiEngine.Persistence;

/// <summary>
/// Verdrahtet die konfigurierte Ablage. Die Dateiablage bleibt der Default fuer Entwicklung
/// und Tests; PostgreSQL ist der Betriebspfad mit echten Transaktionen.
/// </summary>
public static class FlowzerStorageExtensions
{
    public const string MigrateArgument = "--migrate";

    public static IServiceCollection AddFlowzerStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(FlowzerStorageOptions.SectionName).Get<FlowzerStorageOptions>()
                      ?? new FlowzerStorageOptions();
        options.Validate();
        services.AddSingleton(options);

        if (!options.IsPostgreSql)
        {
            services.AddSingleton<ITransactionalStorageProvider, FilesystemStorageSystem.FileSystemTransactionalStorageProvider>();
            services.AddSingleton<IStorageSystem, FilesystemStorageSystem.Storage>();
            return services;
        }

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(options.PostgreSql.ConnectionString);
        // Gemeinsame Cluster begrenzen Verbindungen je Rolle; der Pool bleibt darunter, damit
        // Lastspitzen in einer Warteschlange landen statt in "too many connections for role".
        if (dataSourceBuilder.ConnectionStringBuilder.MaxPoolSize > 20)
        {
            dataSourceBuilder.ConnectionStringBuilder.MaxPoolSize = 20;
        }

        var dataSource = dataSourceBuilder.Build();
        services.AddSingleton(dataSource);
        services.AddSingleton<IStorageSystem>(new PostgreSqlStorage(dataSource, options.PostgreSql.Schema));
        services.AddSingleton<ITransactionalStorageProvider>(new PostgreSqlTransactionalStorageProvider(dataSource, options.PostgreSql.Schema));
        return services;
    }

    /// <summary>
    /// Eigener Migrationsschritt fuer Deployments: `WebApiEngine --migrate` wendet die
    /// PostgreSQL-Migrationen mit der Migrationsverbindung an und beendet den Prozess.
    /// </summary>
    public static bool IsMigrationRun(string[] args) =>
        args.Any(argument => string.Equals(argument, MigrateArgument, StringComparison.OrdinalIgnoreCase));

    /// <summary>Exit-Code von `--migrate`, wenn der Migrationsschritt gescheitert ist.</summary>
    public const int MigrationFailedExitCode = 1;

    /// <summary>
    /// Exit-Code von `--migrate`, wenn der Lauf abgebrochen wurde (SIGTERM/SIGINT bzw. ein
    /// ausgeloestes Token). 130 folgt der Konvention fuer einen Abbruch per Signal.
    /// </summary>
    public const int MigrationCancelledExitCode = 130;

    /// <summary>
    /// Der Migrationsschritt `--migrate`: SQL-Migrationen, danach das Formularbindungs-Upgrade.
    /// Liefert 0 bei Erfolg. Jeder Fehler wird hier gefangen, als eine sichere Fehlerzeile
    /// (endliche Fehlerkategorie/SQLSTATE, bei einer Migration deren Version) protokolliert und als
    /// <see cref="MigrationFailedExitCode"/> zurueckgegeben; ein Abbruch ueber
    /// <paramref name="cancellationToken"/> ist kein Fehler, sondern eine Warnung und
    /// <see cref="MigrationCancelledExitCode"/>. Eine unbehandelte Ausnahme endet in
    /// <c>abort()</c> der .NET-Laufzeit, und das beendet einen Prozess, der im Container PID 1
    /// ist, nicht sauber (amd64: Exit 139, arm64: Endlosschleife; siehe #366).
    /// </summary>
    public static async Task<int> RunMigrationsAsync(IConfiguration configuration, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            await ApplyMigrationsAsync(configuration, logger, cancellationToken);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Das Token erreicht nur den Migrator; dessen Transaktion ist beim Abbruch verworfen.
            // Das Formularbindungs-Upgrade danach beachtet das Token nicht und laeuft zu Ende.
            logger.LogWarning("Migration run cancelled; the transaction was rolled back.");
            return MigrationCancelledExitCode;
        }
        catch (Exception exception)
        {
            LogMigrationFailure(logger, exception);
            return MigrationFailedExitCode;
        }
    }

    /// <summary>Optional fuer einfache Umgebungen: Migrationen beim Start der API.</summary>
    public static async Task ApplyStartupMigrationsIfConfiguredAsync(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<FlowzerStorageOptions>();
        if (options.IsPostgreSql && options.PostgreSql.ApplyMigrationsOnStartup)
        {
            // Beim Start soll ein Fehlschlag den Host weiterhin mit der Ausnahme abbrechen.
            await ApplyMigrationsAsync(app.Configuration, app.Logger, CancellationToken.None);
        }
    }

    private static async Task ApplyMigrationsAsync(IConfiguration configuration, ILogger logger, CancellationToken cancellationToken)
    {
        // Ein schon abgebrochener Lauf beginnt gar nicht erst.
        cancellationToken.ThrowIfCancellationRequested();
        var options = configuration.GetSection(FlowzerStorageOptions.SectionName).Get<FlowzerStorageOptions>()
                      ?? new FlowzerStorageOptions();
        options.Validate();

        if (!options.IsPostgreSql)
        {
            logger.LogInformation("Storage provider {Provider} has no migrations to apply.", options.Provider);
            return;
        }

        var applied = await PostgreSqlMigrator.ApplyAsync(
            options.PostgreSql.ResolveMigrationConnectionString(),
            options.PostgreSql.Schema,
            cancellationToken);
        logger.LogInformation("Applied {Count} PostgreSQL migration(s) to schema {Schema}: {Versions}",
            applied.Count, options.PostgreSql.Schema, string.Join(", ", applied));
        await using var dataSource = NpgsqlDataSource.Create(options.PostgreSql.ResolveMigrationConnectionString());
        using var storage = new PostgreSqlTransactionalStorage(dataSource, options.PostgreSql.Schema);
        await storage.LockForFormCompatibilityUpgradeAsync();
        var upgraded = await LegacyFormBindingUpgrade.ApplyAsync(storage);
        storage.CommitChanges();
        logger.LogInformation("Automatisch ergänzte historische Formularbindungen: {Count}", upgraded);
    }

    /// <summary>
    /// Endliche Fehlerprojektion: keine Message, InnerException, Typnamen unbekannter
    /// Erweiterungen oder SQL-Texte an Logger weitergeben. Auch strukturierte Logprovider
    /// dürfen die originale Ausnahme nicht nachträglich serialisieren (#375).
    /// </summary>
    internal static void LogMigrationFailure(ILogger logger, Exception exception)
    {
        var failed = exception as SchemaMigrationFailedException;
        var cause = failed?.InnerException ?? exception;
        var category = cause switch
        {
            PostgresException => "Npgsql.PostgresException",
            NpgsqlException => "Npgsql.NpgsqlException",
            ArgumentException => "System.ArgumentException",
            InvalidOperationException => "System.InvalidOperationException",
            _ => "OTHER"
        };
        // SQLSTATEs sind Protokollcodes, aber auch hier nur konkrete bekannte Kategorien
        // übernehmen. Freie DB-Nachrichten und Migrationsnamen sind keine sichere Projektion.
        var sqlState = (cause as PostgresException)?.SqlState switch
        {
            PostgresErrorCodes.UndefinedColumn => PostgresErrorCodes.UndefinedColumn,
            PostgresErrorCodes.InsufficientPrivilege => PostgresErrorCodes.InsufficientPrivilege,
            PostgresErrorCodes.InvalidPassword => PostgresErrorCodes.InvalidPassword,
            PostgresErrorCodes.DuplicateTable => PostgresErrorCodes.DuplicateTable,
            PostgresErrorCodes.TooManyConnections => PostgresErrorCodes.TooManyConnections,
            PostgresErrorCodes.QueryCanceled => PostgresErrorCodes.QueryCanceled,
            _ => "OTHER"
        };
        if (failed is not null)
        {
            logger.LogError(
                "PostgreSQL migration (version {Version}) failed; the migration run was rolled back and no migration was applied: {ExceptionType} (SqlState {SqlState}).",
                failed.Version, category, sqlState);
            return;
        }

        // Das Formularbindungs-Upgrade kann nach bereits bestätigten Migrationen scheitern:
        // keine pauschale Rollbackbehauptung für den gesamten Deployment-Schritt.
        logger.LogError("Migration step failed: {ExceptionType} (SqlState {SqlState}).", category, sqlState);
    }
}
