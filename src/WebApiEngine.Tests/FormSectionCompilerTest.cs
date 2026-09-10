using FluentAssertions;
using WebApiEngine.Forms;

namespace WebApiEngine.Tests;

public sealed class FormSectionCompilerTest
{
    // Testzweck: Wiederverwendbare Abschnitte duerfen nur die bereits serverseitig
    // pruefbaren Felder, Layouts, Bedingungen und Plaintext-Hilfen verwenden.
    [Test]
    public void Compile_ShouldAcceptSupportedDeclarativeFieldsAndLayouts()
    {
        var contract = FormSectionCompiler.Compile("""
            {
              "flowzer":{"contractVersion":3},
              "components":[
                {"type":"panel","key":"request","components":[
                  {"type":"textfield","key":"reason","description":"Bitte kurz begruenden"},
                  {"type":"number","key":"days","conditional":{"when":"reason","eq":"holiday","show":true}}
                ]}
              ]
            }
            """);

        contract.ValidationProfile.Should().Be(FormContract.ProfileV3);
        contract.Fields.Select(field => field.Key).Should().Equal("reason", "days");
    }

    // Testzweck: Abschnittsreferenzen duerfen im ersten Profil nicht rekursiv
    // verschachtelt werden, damit Aufloesung und Bindung endlich bleiben.
    [Test]
    public void Compile_ShouldRejectNestedSectionReferencesWithStableCode()
    {
        Action compile = () => FormSectionCompiler.Compile("""
            {"components":[{"type":"flowzerSection","key":"address","sectionId":"00000000-0000-0000-0000-000000000001","version":"1.0"}]}
            """);

        compile.Should().Throw<FormContractException>().Which.Code.Should().Be("section.nested");
    }

    // Testzweck: Wiederholgruppen in Bibliotheksabschnitten bleiben gesperrt, bis
    // ihre Namespace-, Validierungs- und Vorschausemantik explizit spezifiziert ist.
    [Test]
    public void Compile_ShouldRejectRepeatGroupsWithStableCode()
    {
        Action compile = () => FormSectionCompiler.Compile("""
            {"flowzer":{"contractVersion":3},"components":[{"type":"datagrid","key":"rows","components":[]}]}
            """);

        compile.Should().Throw<FormContractException>().Which.Code
            .Should().Be("section.repeat_group_unsupported");
    }

    // Testzweck: Aktionen gehoeren zum Formularvertrag und duerfen beim Expandieren
    // eines Abschnitts nicht unbemerkt Root-Aktionen des Zielformulars veraendern.
    [Test]
    public void Compile_ShouldRejectRootActionsWithStableCode()
    {
        Action compile = () => FormSectionCompiler.Compile("""
            {"flowzer":{"contractVersion":4,"actions":[{"id":"approve"}]},"components":[]}
            """);

        compile.Should().Throw<FormContractException>().Which.Code
            .Should().Be("section.root_feature_unsupported");
    }

    // Testzweck: Nicht eingabebezogene Form.io-Komponenten koennen beim Einbetten
    // keine unklare Darstellung oder aktive Schaltflaechen einschleusen.
    [Test]
    public void Compile_ShouldRejectNonFieldComponentsWithStableCode()
    {
        Action compile = () => FormSectionCompiler.Compile("""
            {"components":[{"type":"content","key":"notice","html":"<b>unsafe</b>"}]}
            """);

        compile.Should().Throw<FormContractException>().Which.Code
            .Should().Be("section.non_field_component");
    }

    // Testzweck: Abschnittspublikation verwendet dieselbe Script-Sperre wie
    // vollstaendige Formulare und darf keine zweite Sicherheitsgrenze erfinden.
    [Test]
    public void Compile_ShouldReuseFormScriptProtection()
    {
        Action compile = () => FormSectionCompiler.Compile("""
            {"components":[{"type":"textfield","key":"name","calculateValue":"value = data.other"}]}
            """);

        compile.Should().Throw<FormContractException>().Which.Code.Should().Be("schema.script");
    }
}
