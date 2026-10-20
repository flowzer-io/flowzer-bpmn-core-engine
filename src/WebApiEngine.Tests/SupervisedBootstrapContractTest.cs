using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using WebApiEngine.Configuration;

namespace WebApiEngine.Tests;

/// <summary>
/// Manueller Start-/Wiederanlaufvertrag an der echten API-Bootstrapquelle. Kein Host,
/// Feeder, Container oder Secret-Store: Diese Tests sind keine Betriebsabnahme.
/// </summary>
public class SupervisedBootstrapContractTest
{
    // Testzweck: Nach erfolgreicher einmaliger Lieferung benötigt ein neuer Prozess wieder
    // ein vollständiges Paket. Leeres EOF darf keinen gespeicherten Vorgängerwert aktivieren.
    [Test]
    public async Task Restart_ShouldRequireFreshInputInsteadOfReusingPriorBootstrap()
    {
        using var firstInput = ApiInput();
        var first = await RuntimeSecretInput.LoadAsync(["--secrets-stdin"], firstInput);
        using var firstConfig = new ConfigurationManager();
        RuntimeSecretConfiguration.TryApply(first, firstConfig).Should().BeTrue();

        using var noNewDelivery = new MemoryStream();
        var restarted = await RuntimeSecretInput.LoadAsync(["--secrets-stdin"], noNewDelivery);
        restarted.Accepted.Should().BeFalse();
        using var secondConfig = new ConfigurationManager();
        RuntimeSecretConfiguration.TryApply(restarted, secondConfig).Should().BeFalse();
        secondConfig[RuntimeSecretInput.RuntimeKey].Should().BeNull();
    }

    // Testzweck: Der Migrationslauf liefert ausschließlich die DDL-Identität. Dass ein
    // vorheriger Migrator erfolgreich beliefert wurde, berechtigt keinen APIstart ohne Paket.
    [Test]
    public async Task ApiStart_ShouldRejectMigrationEnvelopeAndNeverInheritDdlIdentity()
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = 1, role = "migrate",
            values = new { migrationConnectionString = "Host=localhost;Username=demo_migration" }
        });
        using var migrationInput = new MemoryStream(payload);
        var migration = await RuntimeSecretInput.LoadAsync(["--migrate", "--secrets-stdin"], migrationInput);
        using var migrationConfig = new ConfigurationManager();
        RuntimeSecretConfiguration.TryApply(migration, migrationConfig).Should().BeTrue();
        migrationConfig[RuntimeSecretInput.BffKey].Should().BeNull();
        using var apiInput = new MemoryStream(payload);
        var api = await RuntimeSecretInput.LoadAsync(["--secrets-stdin"], apiInput);
        api.Accepted.Should().BeFalse();
    }

    // Testzweck: Ein manuelles Restartpaket darf einen verbliebenen ENV-/Datei-Provider nicht
    // überschreiben. Auch ein scheinbar leerer höherer Provider verdeckt keinen Fremdwert.
    [Test]
    public async Task Restart_ShouldRejectProtectedValueInAnyPreviousConfigurationProvider()
    {
        using var input = ApiInput();
        var bootstrap = await RuntimeSecretInput.LoadAsync(["--secrets-stdin"], input);
        using var config = new ConfigurationManager();
        config.AddInMemoryCollection(new Dictionary<string, string?> { [RuntimeSecretInput.RuntimeKey] = "synthetic-old-secret" });
        config.AddInMemoryCollection(new Dictionary<string, string?> { [RuntimeSecretInput.RuntimeKey] = "" });
        RuntimeSecretConfiguration.TryApply(bootstrap, config).Should().BeFalse();
    }

    // Testzweck: Der tatsächlich kompilierte Provider enthält den komponierten Head22,
    // nicht nur eine Dateiliste oder den Head20 des früheren isolierten x64-Piloten.
    [Test]
    public void ComposedMigrationAssembly_ShouldContainExactlyVersionsOneThroughTwentyTwo()
    {
        PostgreSqlStorageSystem.PostgreSqlMigrator.AvailableVersions.Should().Equal(Enumerable.Range(1, 22));
    }

    private static MemoryStream ApiInput() => new(JsonSerializer.SerializeToUtf8Bytes(new
    {
        version = 1, role = "api",
        values = new
        {
            runtimeConnectionString = "Host=localhost;Username=demo_runtime",
            bffClientSecret = "synthetic-bff-only", directoryClientSecret = "synthetic-directory-only"
        }
    }));
}
