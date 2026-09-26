using FluentAssertions;
using Microsoft.Extensions.Logging;
using Npgsql;
using PostgreSqlStorageSystem;
using WebApiEngine.Persistence;

namespace WebApiEngine.Tests;

/// <summary>
/// Scheiternde Migration (#366): Der Migrator benennt die Migration, an der der Lauf abbricht,
/// ohne die Transaktionssemantik zu aendern, und `--migrate` endet dann mit Exit-Code 1 und einer
/// Fehlerzeile statt mit einer unbehandelten Ausnahme. Dieselbe Kollision wie Fall 3 des
/// Upgrade-/Restore-Rigs: eine vorab angelegte `inbound_triggers` ohne Spalte `trigger_key`
/// laesst 019 am Unique-Index scheitern, nachdem 017 und 018 im selben Lauf schon gelaufen sind.
/// </summary>
public partial class PostgreSqlStorageIntegrationTest
{
    private const int MigrationFailureBaselineExclusiveMaxVersion = 17;

    // Testzweck: Scheitert eine Migration, wirft der Migrator eine SchemaMigrationFailedException
    // mit Version, Namen und SQLSTATE der gescheiterten Migration (019, 42703) und der
    // PostgresException als Ursache. Der ganze Lauf ist zurueckgerollt: Historie 001 bis 016
    // unveraendert, keines der Objekte aus 017 bis 019 im Schema.
    [Test]
    public async Task Migrator_ShouldNameTheFailedMigrationAndRollBackTheWholeRun()
    {
        var schema = $"flowzer_migrate_fail_{Guid.NewGuid():N}";

        try
        {
            await PrepareMigrationCollisionAsync(schema);
            var before = await PostgreSqlMigrator.GetStatusAsync(_connectionString, schema);

            var act = () => PostgreSqlMigrator.ApplyAsync(_connectionString, schema);

            var failure = (await act.Should().ThrowExactlyAsync<SchemaMigrationFailedException>()).Which;
            failure.Version.Should().Be(19);
            failure.Name.Should().Be("019_inbound_triggers");
            failure.SqlState.Should().Be(PostgresErrorCodes.UndefinedColumn);
            failure.InnerException.Should().BeOfType<PostgresException>()
                .Which.MessageText.Should().Contain("trigger_key");
            failure.Message.Should().Contain("019_inbound_triggers").And.Contain("42703");

            var after = await PostgreSqlMigrator.GetStatusAsync(_connectionString, schema);
            after.Applied.Should().Equal(before.Applied);
            after.Applied.Should().Equal(Enumerable.Range(1, MigrationFailureBaselineExclusiveMaxVersion - 1));
            (await CountMigrationObjectsFrom017To019Async(schema)).Should().Be(0);
        }
        finally
        {
            await DropSchemaAsync(schema);
        }
    }

    // Testzweck: `--migrate` (RunMigrationsAsync) faengt die gescheiterte Migration, liefert
    // Exit-Code 1 und protokolliert genau eine Fehlerzeile mit Migration, Version, Ausnahmetyp
    // und SQLSTATE. Es meldet keine angewendete Migration, laesst das Formularbindungs-Upgrade
    // aus und die Historie unveraendert.
    [Test]
    public async Task RunMigrations_ShouldReturnExitCodeOneAndLogTheCauseWhenAMigrationFails()
    {
        var schema = $"flowzer_migrate_exit_{Guid.NewGuid():N}";

        try
        {
            await PrepareMigrationCollisionAsync(schema);
            var before = await PostgreSqlMigrator.GetStatusAsync(_connectionString, schema);
            var logger = new CapturingLogger<PostgreSqlStorageIntegrationTest>();

            var exitCode = await FlowzerStorageExtensions.RunMigrationsAsync(MigrationConfiguration(schema), logger);

            exitCode.Should().Be(FlowzerStorageExtensions.MigrationFailedExitCode).And.Be(1);
            var error = logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Error).Which;
            error.Message.Should()
                .Contain("019_inbound_triggers").And
                .Contain("version 19").And
                .Contain("Npgsql.PostgresException").And
                .Contain("SqlState 42703").And
                .Contain("column \"trigger_key\" does not exist");
            logger.Entries.Should().NotContain(entry => entry.Message.Contains("Applied"));
            logger.Entries.Should().NotContain(entry => entry.Message.Contains("Formularbindungen"));

            (await PostgreSqlMigrator.GetStatusAsync(_connectionString, schema)).Applied.Should().Equal(before.Applied);
            (await CountMigrationObjectsFrom017To019Async(schema)).Should().Be(0);
        }
        finally
        {
            await DropSchemaAsync(schema);
        }
    }

    // Stand 016 wie im Rig-Fixture, dazu die Kollisionstabelle mit abweichender Struktur.
    private async Task PrepareMigrationCollisionAsync(string schema)
    {
        await ApplyMigrationsBelow(schema, MigrationFailureBaselineExclusiveMaxVersion);
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var collision = new NpgsqlCommand(
            $"CREATE TABLE {Quote(schema)}.inbound_triggers (id uuid PRIMARY KEY, body text NOT NULL)", connection);
        await collision.ExecuteNonQueryAsync();
    }

    // Indizes aus 017 und 018 sowie der Unique-Index aus 019: Nach dem Rollback darf keiner da sein.
    private async Task<long> CountMigrationObjectsFrom017To019Async(string schema)
    {
        await using var connection = await _dataSource!.OpenConnectionAsync();
        await using var count = new NpgsqlCommand("""
            SELECT count(*)
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = @schema
              AND c.relname IN ('ai_runs_instance_idx', 'idempotency_records_instance_idx',
                                'runtime_node_events_definition_time_idx', 'inbound_triggers_key_unique_idx')
            """, connection);
        count.Parameters.AddWithValue("schema", schema);
        return (long)(await count.ExecuteScalarAsync())!;
    }
}
