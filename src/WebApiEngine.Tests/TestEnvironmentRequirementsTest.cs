using FluentAssertions;

namespace WebApiEngine.Tests;

/// <summary>
/// Prueft die Entscheidungslogik hinter <c>FLOWZER_TESTS_REQUIRE_POSTGRESQL</c>, ohne
/// Umgebungsvariablen zu veraendern oder einen Container zu brauchen.
/// </summary>
public class TestEnvironmentRequirementsTest
{
    // Testzweck: Nur "1" und "true" (auch mit Leerraum und in anderer Schreibung) schalten
    // die Pflicht ein; leer, fehlend, "0" und "false" lassen das Ueberspringen zu.
    [TestCase("1", true)]
    [TestCase("true", true)]
    [TestCase(" TRUE ", true)]
    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("0", false)]
    [TestCase("false", false)]
    [TestCase("yes", false)]
    public void IsRequired_ShouldOnlyAcceptOneOrTrue(string? value, bool expected)
    {
        TestEnvironmentRequirements.IsRequired(value).Should().Be(expected);
    }

    // Testzweck: Die Begruendung nennt Ausnahmetyp und -text, damit im CI-Protokoll sofort
    // erkennbar ist, warum der Container nicht startete; ohne Ursache bleibt der Grundsatz.
    [Test]
    public void DescribeUnavailable_ShouldNameTheCause()
    {
        var withCause = TestEnvironmentRequirements.DescribeUnavailable(
            new InvalidOperationException("Docker API responded with status code='NotFound'."));
        withCause.Should().Be(
            "PostgreSQL-Container nicht verfuegbar (Docker fehlt?): InvalidOperationException: "
            + "Docker API responded with status code='NotFound'");

        TestEnvironmentRequirements.DescribeUnavailable(null)
            .Should().Be("PostgreSQL-Container nicht verfuegbar (Docker fehlt?)");
    }

    // Testzweck: Der Hinweis im Fehlerfall nennt die Variable beim Namen, damit der Leser des
    // roten Laufs weiss, welcher Schalter das Ueberspringen verbietet.
    [Test]
    public void RequiredHint_ShouldNameTheVariable()
    {
        TestEnvironmentRequirements.RequiredHint.Should().Contain("FLOWZER_TESTS_REQUIRE_POSTGRESQL");
    }
}
