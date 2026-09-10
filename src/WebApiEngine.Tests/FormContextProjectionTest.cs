using System.Dynamic;
using System.Text.Json;
using FluentAssertions;
using WebApiEngine.Forms;

namespace WebApiEngine.Tests;

/// <summary>Eine deklarierte Feldadresse ist keine Freigabe für beliebige verschachtelte Objekte.</summary>
public class FormContextProjectionTest
{
    // Testzweck: Skalarfelder in Layouts und Containern bleiben nutzbar, nicht deklarierte
    // Nachbarwerte und geschützte Objektteile werden unabhängig vom Variablenscope entfernt.
    [Test]
    public void Projection_ShouldRetainOnlyDeclaredNestedLeaves()
    {
        var source = Data("""{"public":"ok","secret":"private","person":{"name":"Anna","salary":100000}}""");
        var result = FormContextProjection.Project("""
            {"components":[{"type":"columns","columns":[{"components":[
              {"type":"textfield","key":"public"}]}]},
              {"type":"container","key":"person","components":[{"type":"textfield","key":"name"}]}]}
            """, source);

        JsonSerializer.Serialize(result).Should().Be("""{"public":"ok","person":{"name":"Anna"}}""");
        JsonSerializer.Serialize(source).Should().Contain("salary");
    }

    // Testzweck: Ein textuelles Feld oder eine Auswahl darf keine beliebigen Objekte mitsamt
    // privaten Eigenschaften freigeben; skalare Mehrfachauswahl bleibt erhalten.
    [Test]
    public void Projection_ShouldRejectObjectValuesAndMixedArrays()
    {
        var result = FormContextProjection.Project("""
            {"components":[{"type":"select","key":"person"},
              {"type":"select","key":"groups","multiple":true},
              {"type":"select","key":"mixed","multiple":true}]}
            """, Data("""{"person":{"secret":"private"},"groups":["one","two"],"mixed":["ok",{"secret":"private"}]}"""));

        JsonSerializer.Serialize(result).Should().Be("""{"groups":["one","two"]}""");
    }

    // Testzweck: Unverständliche Schemata und unbekannte Komponenten dürfen nie zum
    // ungefilterten Fallback auf den gesamten Prozessvariablenscope führen.
    [TestCase(null)]
    [TestCase("{broken")]
    [TestCase("[]")]
    // Testzweck: Unbekannte Typen und Nicht-Eingabefelder geben keine Variablen frei.
    [TestCase("{\"components\":[{\"type\":\"unknown\",\"key\":\"secret\"}]}")]
    // Testzweck: Explizite Nicht-Eingabefelder werden nicht in den Formularkontext kopiert.
    [TestCase("{\"components\":[{\"type\":\"textfield\",\"key\":\"secret\",\"input\":false}]}")]
    public void Projection_ShouldFailClosed(string? schema)
    {
        JsonSerializer.Serialize(FormContextProjection.Project(schema, Data("""{"secret":"private"}""")))
            .Should().Be("{}");
    }

    // Testzweck: Nur erlaubte Pfadblätter werden freigegeben; gefährliche JavaScript-
    // Propertynamen und Formularberechnungen verleihen keine zusätzlichen Datenrechte.
    [Test]
    public void Projection_ShouldIgnorePrototypePathsAndNeverExecuteScripts()
    {
        var result = FormContextProjection.Project("""
            {"components":[{"type":"textfield","key":"person.name"},
              {"type":"hidden","key":"__proto__.secret"},
              {"type":"hidden","key":"constructor.secret"},
              {"type":"hidden","key":"calculated","calculateValue":"throw new Error('not allowed')"}]}
            """, Data("""{"person":{"name":"Anna","secret":"private"},"__proto__":{"secret":"private"},"constructor":{"secret":"private"}}"""));

        JsonSerializer.Serialize(result).Should().Be("""{"person":{"name":"Anna"}}""");
    }

    private static ExpandoObject Data(string json) => JsonSerializer.Deserialize<ExpandoObject>(json)!;
}
