using FluentAssertions;
using Microsoft.Extensions.Options;
using WebApiEngine.Ai;

namespace WebApiEngine.Tests;

/// <summary>Grenztests fuer die erste austauschbare Secret-Store-Implementierung.</summary>
[NonParallelizable]
public sealed class EnvironmentAiSecretStoreTest
{
    // Testzweck: Nur gesetzte Variablen im explizit freigegebenen Flowzer-Namensraum gelten
    // als vorhanden; fremde Umgebungsvariablen koennen nicht ueber die API sondiert werden.
    [Test]
    public async Task ExistsAsync_ShouldStayInsideConfiguredEnvironmentNamespace()
    {
        const string allowed = "FLOWZER_AI_TEST_SECRET";
        const string foreign = "FLOWZER_OTHER_SECRET";
        var oldAllowed = Environment.GetEnvironmentVariable(allowed);
        var oldForeign = Environment.GetEnvironmentVariable(foreign);
        try
        {
            Environment.SetEnvironmentVariable(allowed, "sensitive-test-value");
            Environment.SetEnvironmentVariable(foreign, "foreign-sensitive-test-value");
            var store = new EnvironmentAiSecretStore(Options.Create(new FlowzerAiOptions
            {
                SecretEnvironmentVariablePrefix = "FLOWZER_AI_"
            }));

            (await store.ExistsAsync($"env:{allowed}")).Should().BeTrue();
            (await store.ExistsAsync($"env:{foreign}")).Should().BeFalse();
            (await store.ExistsAsync("env:PATH")).Should().BeFalse();
            (await store.ExistsAsync("file:/tmp/key")).Should().BeFalse();

            using var resolved = await store.ResolveAsync($"env:{allowed}");
            resolved.Should().NotBeNull();
            resolved!.Value.ToString().Should().Be("sensitive-test-value");
            resolved.ToString().Should().Be("[REDACTED]");
        }
        finally
        {
            Environment.SetEnvironmentVariable(allowed, oldAllowed);
            Environment.SetEnvironmentVariable(foreign, oldForeign);
        }
    }

    // Testzweck: Eine leere Variable ist nicht ausfuehrungsbereit und wird nicht mit einem
    // bloss vorhandenen Variablennamen verwechselt.
    [Test]
    public async Task ExistsAsync_ShouldTreatEmptySecretAsMissing()
    {
        const string name = "FLOWZER_AI_EMPTY_SECRET";
        var previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, string.Empty);
            var store = new EnvironmentAiSecretStore(Options.Create(new FlowzerAiOptions()));

            (await store.ExistsAsync($"env:{name}")).Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }
}
