using FluentAssertions;
using StorageSystem;

namespace WebApiEngine.Tests;

/// <summary>
/// Die Regel fuer Katalog-Kennungen. Sie ist bewusst kein enges Muster, sondern eine Liste
/// verbotener Bestandteile: Die vorhandenen Kataloge sollen weiter lesbar bleiben.
/// </summary>
public class DefinitionIdRulesTest
{
    // Testzweck: Prüft, dass die heute üblichen Kennungen gültig bleiben — BPMN-Ids, die Beispiele,
    // die von der Konsole erzeugten Kennungen und Namen mit Leer- und Sonderzeichen.
    [TestCase("Process_1")]
    [TestCase("Definitions_0abc")]
    [TestCase("flowzer-urlaubsantrag")]
    [TestCase("definition_3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    [TestCase("Urlaubsantrag 2026")]
    [TestCase("prüfung.v2")]
    [TestCase("...")]
    [TestCase("a..b")]
    public void IsValid_ShouldAcceptEstablishedDefinitionIds(string definitionId)
    {
        DefinitionIdRules.IsValid(definitionId).Should().BeTrue();
    }

    // Testzweck: Prüft, dass eine Kennung ohne Inhalt abgelehnt wird — sie ergäbe einen Dateinamen
    // aus nichts als der Endung.
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t")]
    public void IsValid_ShouldRejectBlankDefinitionIds(string? definitionId)
    {
        DefinitionIdRules.IsValid(definitionId).Should().BeFalse();
    }

    // Testzweck: Prüft, dass Pfadbestandteile abgelehnt werden — genau sie führen aus dem
    // Ablageordner heraus (CodeQL cs/path-injection).
    [TestCase("a/b")]
    [TestCase("a\\b")]
    [TestCase("/etc/passwd")]
    [TestCase("../x")]
    [TestCase("..")]
    [TestCase(".")]
    public void IsValid_ShouldRejectPathSegments(string definitionId)
    {
        DefinitionIdRules.IsValid(definitionId).Should().BeFalse();
    }

    // Testzweck: Prüft, dass Steuerzeichen abgelehnt werden — das Nullzeichen schneidet einen
    // Dateinamen auf Betriebssystemebene ab, Zeilenumbrüche machen Protokolle unlesbar.
    [TestCase("a\0b")]
    [TestCase("a\nb")]
    [TestCase("a\rb")]
    public void IsValid_ShouldRejectControlCharacters(string definitionId)
    {
        DefinitionIdRules.IsValid(definitionId).Should().BeFalse();
    }

    // Testzweck: Prüft die Längengrenze — genau ausgeschöpft gültig, ein Zeichen darüber nicht.
    [Test]
    public void IsValid_ShouldRejectOverlongDefinitionIds()
    {
        DefinitionIdRules.IsValid(new string('a', DefinitionIdRules.MaxLength)).Should().BeTrue();
        DefinitionIdRules.IsValid(new string('a', DefinitionIdRules.MaxLength + 1)).Should().BeFalse();
    }

    // Testzweck: Prüft, dass die Meldung die abgelehnte Kennung nennt — ohne sie ist in der
    // Oberfläche nicht erkennbar, welcher Wert gemeint ist.
    [Test]
    public void BuildErrorMessage_ShouldNameTheRejectedDefinitionId()
    {
        var message = DefinitionIdRules.BuildErrorMessage("../x");

        message.Should().Contain("../x");
        message.Should().Contain("is not a valid definition id");
    }

    // Testzweck: Prüft, dass die Prüfung als Wächter eine ArgumentException wirft — die API
    // beantwortet sie mit 400 statt mit einem Serverfehler.
    [Test]
    public void EnsureValid_ShouldThrowArgumentException_ForInvalidDefinitionId()
    {
        var act = () => DefinitionIdRules.EnsureValid("../x");

        act.Should().Throw<ArgumentException>().WithMessage("*is not a valid definition id*");
    }

    // Testzweck: Prüft, dass eine gültige Kennung den Wächter unverändert passiert.
    [Test]
    public void EnsureValid_ShouldPass_ForValidDefinitionId()
    {
        var act = () => DefinitionIdRules.EnsureValid("flowzer-urlaubsantrag");

        act.Should().NotThrow();
    }
}
