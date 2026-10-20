using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PostgreSqlStorageSystem;
using WebApiEngine.Persistence;

namespace WebApiEngine.Tests;

/// <summary>Reale Npgsql-Parserfehler vor jedem DB-Zugriff dürfen keine Secretfragmente ausgeben (#375).</summary>
public class PostgreSqlSecretFailureTest
{
    private const string Marker = "synthetic-password-fragment-r375";
    private static string Malformed => $"Host=localhost;Username=demo;Password=prefix;{Marker}=suffix";

    // Testzweck: Der echte Treiberparser übernimmt ein falsch angehängtes Passwortfragment in
    // seine Fehlermeldung; die Ablagevalidierung darf weder Text noch InnerException übernehmen.
    [TestCase(false)]
    [TestCase(true)]
    public void Options_ShouldRejectMalformedRuntimeOrMigrationWithoutRetainingDriverException(bool migration)
    {
        var options = new PostgreSqlStorageOptions
        {
            ConnectionString = migration ? "Host=localhost;Database=demo" : Malformed,
            MigrationConnectionString = migration ? Malformed : null
        };
        Action validate = options.Validate;
        var error = validate.Should().ThrowExactly<InvalidOperationException>().Which;
        error.Message.Should().Contain(migration ? "MigrationConnectionString" : "ConnectionString");
        error.ToString().Should().NotContain(Marker).And.NotContain("prefix").And.NotContain("suffix");
        error.InnerException.Should().BeNull();
    }

    // Testzweck: Der API-DI-Pfad muss dieselbe sichere Validierung vor dem DataSource-Build
    // erreichen, ohne Webhost/Listener, Datenbank oder ein künstliches Parser-Testdoppel.
    [Test]
    public void ApiRegistration_ShouldRejectMalformedInputWithoutSecretInExceptionChain()
    {
        var configuration = Config(Malformed);
        Action register = () => new ServiceCollection().AddFlowzerStorage(configuration);
        var error = register.Should().ThrowExactly<InvalidOperationException>().Which;
        error.ToString().Should().NotContain(Marker);
        error.InnerException.Should().BeNull();
    }

    // Testzweck: Auch beim echten Migrationskommando darf kein Parserfragment im Logtext oder
    // Exceptionobjekt landen. Ungültige Syntax scheitert vor Verbindung/SQL, Exitcode bleibt1.
    [Test]
    public async Task MigrationCommand_ShouldRejectMalformedInputWithSafeLogAndExitOne()
    {
        var logger = new CapturingLogger<PostgreSqlSecretFailureTest>();
        var exit = await FlowzerStorageExtensions.RunMigrationsAsync(Config(Malformed), logger);
        exit.Should().Be(1);
        var entry = logger.Entries.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogLevel.Error);
        entry.Message.Should().Contain("Migration step failed");
        entry.Message.Should().NotContain(Marker).And.NotContain("prefix").And.NotContain("suffix");
        entry.Exception.Should().BeNull();
    }

    // Testzweck: Sichere Verbindungserstellung erfolgt über den Builder; Trennzeichen und
    // Anführungszeichen im Passwort bleiben gültig statt durch eine Zeichenfilterung zerstört.
    [Test]
    public void Options_ShouldAcceptBuilderQuotedPasswordsWithoutChangingTheirValue()
    {
        var builder = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = "localhost", Database = "demo", Username = "demo", Password = "x;\"y=z"
        };
        var options = new PostgreSqlStorageOptions { ConnectionString = builder.ConnectionString };
        options.Validate();
        new Npgsql.NpgsqlConnectionStringBuilder(options.ConnectionString).Password.Should().Be(builder.Password);
    }

    // Testzweck: Downstream- und Abbruchfehler können selbst Secret-/SQL-Fragmente tragen.
    // Das Log erhält nur die endliche Kategorie, niemals Message/Stacktrace/InnerException.
    [Test]
    public void MigrationFailure_ShouldNotForwardDriverOrCustomExceptionPayloads()
    {
        var logger = new CapturingLogger<PostgreSqlSecretFailureTest>();
        FlowzerStorageExtensions.LogMigrationFailure(logger,
            new ArgumentException(Marker, new InvalidOperationException(Marker)));
        FlowzerStorageExtensions.LogMigrationFailure(logger,
            new SchemaMigrationFailedException(19, Marker,
                new Npgsql.PostgresException(Marker, Marker, Marker, Npgsql.PostgresErrorCodes.UndefinedColumn)));
        logger.Entries.Should().HaveCount(2);
        logger.Entries.Should().OnlyContain(entry => entry.Exception == null && !entry.Message.Contains(Marker));
        logger.Entries[1].Message.Should().Contain("version 19").And.Contain("SqlState 42703");
    }

    // Testzweck: Auch der --check-config-Parser darf bei direkter Diagnose einer noch nicht
    // gebundenen Konfiguration keinen unbekannten Passwortschlüssel im Hinweis wiederholen.
    [Test]
    public void ConfigurationProbe_ShouldNotCopyMalformedParserMessage()
    {
        var accepted = WebApiEngine.Diagnostics.ConfigurationCheck.TryDescribeConnection(
            Malformed, out _, out var probe, out var error);
        accepted.Should().BeFalse();
        probe.Should().BeEmpty();
        error.Should().NotContain(Marker).And.NotContain("prefix").And.NotContain("suffix");
    }

    private static IConfiguration Config(string connection) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "PostgreSql",
            ["Storage:PostgreSql:ConnectionString"] = connection
        }).Build();
}
