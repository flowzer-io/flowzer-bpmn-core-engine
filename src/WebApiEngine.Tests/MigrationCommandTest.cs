using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebApiEngine.Persistence;

namespace WebApiEngine.Tests;

/// <summary>
/// Fehlerpfad von `--migrate` ohne Datenbank (#366): Auch ein Fehler vor der ersten Migration
/// (Konfiguration, Verbindung) endet mit Exit-Code 1 und einer Fehlerzeile, nicht mit einer
/// unbehandelten Ausnahme. Die Faelle mit echter Datenbank stehen in
/// <c>PostgreSqlStorageIntegrationTest.MigrationFailure.cs</c>.
/// </summary>
public class MigrationCommandTest
{
    // Testzweck: Eine ungueltige Ablagekonfiguration laesst RunMigrationsAsync nicht werfen,
    // sondern Exit-Code 1 liefern und die Ursache samt Ausnahmetyp als Fehler protokollieren.
    [Test]
    public async Task RunMigrations_ShouldReturnExitCodeOneForAnInvalidStorageConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Provider"] = "Unknown" })
            .Build();
        var logger = new CapturingLogger<MigrationCommandTest>();

        var exitCode = await FlowzerStorageExtensions.RunMigrationsAsync(configuration, logger);

        exitCode.Should().Be(1);
        logger.Entries.Should().ContainSingle().Which.Should().Match<CapturingLogger<MigrationCommandTest>.CapturedLogEntry>(entry =>
            entry.Level == LogLevel.Error
            && entry.Message.Contains("Migration step failed")
            && entry.Message.Contains("System.InvalidOperationException")
            && entry.Message.Contains("Storage:Provider"));
    }

    // Testzweck: Ist die Datenbank nicht erreichbar, endet `--migrate` ebenfalls mit Exit-Code 1
    // und nennt den Ausnahmetyp (NpgsqlException) statt abzustuerzen. Port 1 auf der Loopback-
    // Schnittstelle weist die Verbindung sofort ab; es wird nichts ausser dem Verbindungsversuch
    // ausgefuehrt.
    [Test]
    public async Task RunMigrations_ShouldReturnExitCodeOneWhenTheDatabaseIsUnreachable()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Provider"] = FlowzerStorageOptions.ProviderPostgreSql,
                ["Storage:PostgreSql:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=x;Username=y;Password=z;Timeout=3",
                ["Storage:PostgreSql:Schema"] = "flowzer"
            })
            .Build();
        var logger = new CapturingLogger<MigrationCommandTest>();

        var exitCode = await FlowzerStorageExtensions.RunMigrationsAsync(configuration, logger);

        exitCode.Should().Be(1);
        logger.Entries.Should().ContainSingle().Which.Should().Match<CapturingLogger<MigrationCommandTest>.CapturedLogEntry>(entry =>
            entry.Level == LogLevel.Error
            && entry.Message.Contains("Migration step failed")
            && entry.Message.Contains("Npgsql.NpgsqlException"));
    }
}
