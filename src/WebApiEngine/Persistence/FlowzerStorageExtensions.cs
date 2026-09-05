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

        var dataSource = new NpgsqlDataSourceBuilder(options.PostgreSql.ConnectionString).Build();
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

    public static async Task<int> RunMigrationsAsync(IConfiguration configuration, ILogger logger, CancellationToken cancellationToken = default)
    {
        var options = configuration.GetSection(FlowzerStorageOptions.SectionName).Get<FlowzerStorageOptions>()
                      ?? new FlowzerStorageOptions();
        options.Validate();

        if (!options.IsPostgreSql)
        {
            logger.LogInformation("Storage provider {Provider} has no migrations to apply.", options.Provider);
            return 0;
        }

        var applied = await PostgreSqlMigrator.ApplyAsync(
            options.PostgreSql.ResolveMigrationConnectionString(),
            options.PostgreSql.Schema,
            cancellationToken);
        logger.LogInformation("Applied {Count} PostgreSQL migration(s) to schema {Schema}: {Versions}",
            applied.Count, options.PostgreSql.Schema, string.Join(", ", applied));
        return 0;
    }

    /// <summary>Optional fuer einfache Umgebungen: Migrationen beim Start der API.</summary>
    public static async Task ApplyStartupMigrationsIfConfiguredAsync(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<FlowzerStorageOptions>();
        if (options.IsPostgreSql && options.PostgreSql.ApplyMigrationsOnStartup)
        {
            await RunMigrationsAsync(app.Configuration, app.Logger);
        }
    }
}
