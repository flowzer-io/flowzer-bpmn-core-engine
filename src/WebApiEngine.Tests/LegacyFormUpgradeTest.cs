using System.Dynamic;
using System.Text.Json;
using FluentAssertions;
using WebApiEngine.Forms;

namespace WebApiEngine.Tests;

public sealed class LegacyFormUpgradeTest
{
    // Testzweck: Das vor der Vertragsumstellung ausgelieferte Formular bleibt ohne
    // Benutzer-Migration anzeigbar und validierbar; die Zeitraumregel bleibt wirksam.
    [Test]
    public void ShippedLegacyForm_ShouldUpgradeWithoutLosingItsRules()
    {
        var schema = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "legacy-leave-form.json"));
        var normalized = LegacyFormSchemaUpgrade.Normalize(schema);
        normalized.Should().NotContain("calculateValue").And.NotContain("valid =");
        LegacyFormSchemaUpgrade.Normalize(normalized).Should().Be(normalized);
        var contract = FormContractCompiler.Compile(schema);
        dynamic input = new ExpandoObject();
        input.mitarbeiter = "Testperson"; input.art = "erholung"; input.von = "2026-09-17";
        input.bis = "2026-09-18"; input.arbeitstage = 2; input.vertretung = "Vertretung";
        var output = FormSubmissionValidator.Validate(contract, (ExpandoObject)input);
        ((IDictionary<string, object?>)output)["vorgang"].Should().Be(
            "Testperson · Erholungsurlaub · 17.09.2026 bis 18.09.2026 · 2 Arbeitstage · Vertretung: Vertretung");
        input.bis = "2026-09-16";
        Action invalid = () => FormSubmissionValidator.Validate(contract, (ExpandoObject)input);
        invalid.Should().Throw<FormSubmissionException>();
    }

    // Testzweck: Nur exakt bekannte historische Scripts dürfen in deklarative Regeln
    // überführt werden; veränderte oder fremde Scripts bleiben unverändert und verboten.
    [Test]
    public void UnknownScript_ShouldNotBeSilentlyRemovedOrExecuted()
    {
        const string schema = """{"components":[{"type":"hidden","key":"value","calculateValue":"value = fetch('https://example.test');"}]}""";
        LegacyFormSchemaUpgrade.Normalize(schema).Should().Be(schema);
        Action compile = () => FormContractCompiler.Compile(schema);
        compile.Should().Throw<FormContractException>();
    }
}
