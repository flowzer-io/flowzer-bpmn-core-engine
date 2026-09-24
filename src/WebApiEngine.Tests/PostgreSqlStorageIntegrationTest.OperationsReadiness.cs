using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Model;
using Npgsql;
using PostgreSqlStorageSystem;
using WebApiEngine.Controller;
using WebApiEngine.Persistence;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Betriebsnachweise rund um die Ablage: Was meldet die Bereitschaftsprobe ueber den
/// Migrationsstand, und laesst sich ein Bestand mit den vorgesehenen Werkzeugen sichern und
/// wieder einspielen?
/// </summary>
public partial class PostgreSqlStorageIntegrationTest
{
    // Testzweck: Die Bereitschaftsprobe meldet bei PostgreSQL den Migrationsstand: auf einem
    // aelteren Schema die Zahl der ausstehenden Migrationen, nach dem Update "UpToDate".
    [Test]
    public async Task HealthReadiness_ShouldReportPendingAndCurrentMigrationState()
    {
        var schema = $"flowzer_ready_{Guid.NewGuid():N}";

        try
        {
            await ApplyMigrationsBelow(schema, OldestUpgradeBaselineExclusiveMaxVersion);

            var controller = CreateHealthController(schema);
            var pending = await ReadinessDetailsAsync(controller);
            pending.StorageProvider.Should().Be($"PostgreSQL (schema {schema})");
            pending.MigrationState.Should().Be("Pending");
            pending.PendingMigrationCount.Should().Be(
                PostgreSqlMigrator.AvailableVersions.Count(version => version >= OldestUpgradeBaselineExclusiveMaxVersion));
            pending.ExpectedMigrationVersion.Should().Be(PostgreSqlMigrator.AvailableVersions.Last());

            await PostgreSqlMigrator.ApplyAsync(_connectionString, schema);

            var current = await ReadinessDetailsAsync(CreateHealthController(schema));
            current.MigrationState.Should().Be("UpToDate");
            current.PendingMigrationCount.Should().Be(0);
        }
        finally
        {
            await DropSchemaAsync(schema);
        }
    }

    // Testzweck: Der im Runbook beschriebene Weg funktioniert wirklich: `pg_dump -Fc` sichert
    // das Flowzer-Schema, `pg_restore` spielt es in eine leere Datenbank zurueck, und danach
    // stimmen Bestand und Migrationsstand wieder.
    [Test]
    public async Task BackupAndRestore_ShouldRoundTripSchemaAndMigrationState()
    {
        var schema = $"flowzer_dump_{Guid.NewGuid():N}";
        var dumpPath = $"/tmp/{schema}.dump";
        var connection = new NpgsqlConnectionStringBuilder(_connectionString);
        var definition = CreateDefinition("backup-catalog", 3, 1, isActive: true);

        try
        {
            await PostgreSqlMigrator.ApplyAsync(_connectionString, schema);
            using (var storage = new PostgreSqlStorage(_dataSource!, schema))
            {
                await storage.DefinitionStorage.StoreMetaDefinition(
                    new BpmnMetaDefinition { DefinitionId = definition.DefinitionId, Name = "Sicherung" });
                await storage.DefinitionStorage.StoreDefinition(definition);
            }

            // Genau der Befehl aus scripts/runtime/backup.sh: Archivformat, nur das Flowzer-Schema,
            // ohne Eigentuemer- und Rechtezuweisungen.
            await ExecuteInDatabaseContainerAsync(
                "pg_dump", "--username", connection.Username!, "--dbname", connection.Database!,
                "--format=custom", "--no-owner", "--no-privileges", $"--schema={schema}", "--file", dumpPath);

            await DropSchemaAsync(schema);
            (await PostgreSqlMigrator.GetStatusAsync(_connectionString, schema)).SchemaExists.Should().BeFalse();

            await ExecuteInDatabaseContainerAsync(
                "pg_restore", "--username", connection.Username!, "--dbname", connection.Database!,
                "--no-owner", "--no-privileges", "--exit-on-error", dumpPath);

            var status = await PostgreSqlMigrator.GetStatusAsync(_connectionString, schema);
            status.SchemaExists.Should().BeTrue();
            status.HistoryExists.Should().BeTrue();
            status.IsUpToDate.Should().BeTrue();

            using var restored = new PostgreSqlStorage(_dataSource!, schema);
            (await restored.DefinitionStorage.GetDeployedDefinition("backup-catalog"))!.Id.Should().Be(definition.Id);
            (await restored.DefinitionStorage.GetMetaDefinitionById("backup-catalog")).Name.Should().Be("Sicherung");
        }
        finally
        {
            await DropSchemaAsync(schema);
        }
    }

    private async Task ExecuteInDatabaseContainerAsync(params string[] command)
    {
        var result = await _container!.ExecAsync(command);
        result.ExitCode.Should().Be(0, "{0} meldete: {1} {2}", command[0], result.Stdout, result.Stderr);
    }

    private HealthController CreateHealthController(string schema) => new(
        new PostgreSqlTransactionalStorageProvider(_dataSource!, schema),
        new FlowzerStorageOptions
        {
            Provider = FlowzerStorageOptions.ProviderPostgreSql,
            PostgreSql = new PostgreSqlStorageOptions { ConnectionString = _connectionString, Schema = schema }
        },
        new ReadinessTestEnvironment(),
        NullLogger<HealthController>.Instance);

    private static async Task<HealthReadinessDetailsDto> ReadinessDetailsAsync(HealthController controller)
    {
        var response = await controller.GetReadiness(CancellationToken.None);
        var payload = (response.Result as OkObjectResult)?.Value as ApiStatusResult<HealthStatusDto>;
        payload.Should().NotBeNull();
        payload!.Result!.Status.Should().Be("Healthy");
        payload.Result.Details.Should().NotBeNull();
        return payload.Result.Details!;
    }

    private sealed class ReadinessTestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "WebApiEngine.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
