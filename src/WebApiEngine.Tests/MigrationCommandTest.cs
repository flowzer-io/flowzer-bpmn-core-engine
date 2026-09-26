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
    // und nennt den Ausnahmetyp (NpgsqlException) statt abzustuerzen. Die Ausnahme haengt fuer die
    // Diagnose an der Logzeile, und weder Zeile noch Ausnahme tragen das Passwort der
    // Verbindungszeichenfolge. Port 1 auf der Loopback-Schnittstelle weist die Verbindung sofort
    // ab; es wird nichts ausser dem Verbindungsversuch ausgefuehrt.
    [Test]
    public async Task RunMigrations_ShouldReturnExitCodeOneWhenTheDatabaseIsUnreachable()
    {
        var logger = new CapturingLogger<MigrationCommandTest>();

        var exitCode = await FlowzerStorageExtensions.RunMigrationsAsync(UnreachableDatabaseConfiguration(), logger);

        exitCode.Should().Be(FlowzerStorageExtensions.MigrationFailedExitCode).And.Be(1);
        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().Contain("Migration step failed").And.Contain("Npgsql.NpgsqlException");
        entry.Exception.Should().BeAssignableTo<Npgsql.NpgsqlException>();
        entry.Message.Should().NotContain(UnreachablePassword);
        entry.Exception!.ToString().Should().NotContain(UnreachablePassword);
    }

    // Testzweck: Ein Abbruch (bereits ausgeloestes Token, wie nach SIGTERM) ist kein
    // Migrationsfehler: `--migrate` liefert 130 statt 1 und meldet den Abbruch als Warnung mit
    // verworfener Transaktion, ohne eine "failed"-Zeile.
    [Test]
    public async Task RunMigrations_ShouldReturnExitCode130WhenCancelled()
    {
        var logger = new CapturingLogger<MigrationCommandTest>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var exitCode = await FlowzerStorageExtensions.RunMigrationsAsync(
            UnreachableDatabaseConfiguration(), logger, cancellation.Token);

        exitCode.Should().Be(FlowzerStorageExtensions.MigrationCancelledExitCode).And.Be(130);
        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("cancelled").And.Contain("rolled back");
        logger.Entries.Should().NotContain(logEntry => logEntry.Message.Contains("failed"));
    }

    private const string UnreachablePassword = "r366-geheim-7f3a";

    private static IConfiguration UnreachableDatabaseConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = FlowzerStorageOptions.ProviderPostgreSql,
            ["Storage:PostgreSql:ConnectionString"] =
                $"Host=127.0.0.1;Port=1;Database=x;Username=y;Password={UnreachablePassword};Timeout=3",
            ["Storage:PostgreSql:Schema"] = "flowzer"
        })
        .Build();
}
