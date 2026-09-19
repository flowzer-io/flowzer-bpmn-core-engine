using System.Text;
using FluentAssertions;
using WebApiEngine.InboundTriggers;

namespace WebApiEngine.Tests;

/// <summary>
/// Das Geheimnis eines Auslösers: erzeugen, versiegeln, wieder öffnen, Signatur bilden.
/// </summary>
public sealed class InboundTriggerSecretTest
{
    private static readonly byte[] Key = new InboundTriggerOptions { SecretKey = Passphrase }.ResolveKey();
    private const string Passphrase = "flowzer-trigger-test-installation-key-0123456789";

    // Testzweck: Schluessel und Geheimnis sind URL-sicher, ausreichend lang und bei jedem Aufruf
    // verschieden. Ein vorhersagbarer Schluessel waere zu erraten, ein kurzer zu durchsuchen.
    [Test]
    public void NewKeyAndSecret_ShouldBeRandomAndUrlSafe()
    {
        var keys = Enumerable.Range(0, 50).Select(_ => InboundTriggerSecret.NewKey()).ToArray();
        var secrets = Enumerable.Range(0, 50).Select(_ => InboundTriggerSecret.NewSecret()).ToArray();

        keys.Should().OnlyHaveUniqueItems();
        secrets.Should().OnlyHaveUniqueItems();
        keys.Should().AllSatisfy(key =>
        {
            key.Length.Should().BeGreaterThanOrEqualTo(24);
            key.Should().MatchRegex("^[A-Za-z0-9_-]+$");
        });
        secrets.Should().AllSatisfy(secret => secret.Should().MatchRegex("^[A-Za-z0-9_-]+$"));
    }

    // Testzweck: Das versiegelte Geheimnis enthaelt den Klartext nicht und laesst sich nur mit
    // demselben installationsweiten Schluessel wieder oeffnen.
    [Test]
    public void Protect_ShouldHideTheSecretAndOnlyOpenWithTheSameKey()
    {
        var id = Guid.NewGuid();
        var secret = InboundTriggerSecret.NewSecret();

        var sealedSecret = InboundTriggerSecret.Protect(secret, id, Key);

        sealedSecret.Should().StartWith("aesgcm-v1$");
        sealedSecret.Should().NotContain(secret);
        InboundTriggerSecret.TryUnprotect(sealedSecret, id, Key, out var opened).Should().BeTrue();
        opened.Should().Be(secret);

        var otherKey = new InboundTriggerOptions { SecretKey = Passphrase + "x" }.ResolveKey();
        InboundTriggerSecret.TryUnprotect(sealedSecret, id, otherKey, out _).Should().BeFalse();
    }

    // Testzweck: Das Siegel haengt an der Kennung des Ausloesers. Ein umkopierter Eintrag laesst
    // sich dadurch nicht unter einer fremden Kennung wieder oeffnen.
    [Test]
    public void Protect_ShouldBindTheSealToItsTrigger()
    {
        var id = Guid.NewGuid();
        var sealedSecret = InboundTriggerSecret.Protect(InboundTriggerSecret.NewSecret(), id, Key);

        InboundTriggerSecret.TryUnprotect(sealedSecret, Guid.NewGuid(), Key, out _).Should().BeFalse();
    }

    // Testzweck: Ein unlesbarer oder veraenderter Eintrag ist kein Nachweis und fuehrt nicht zu
    // einer Ausnahme, sondern zu einer Ablehnung.
    [TestCase("")]
    [TestCase("nicht-versiegelt")]
    [TestCase("pbkdf2-sha256$100000$AAAA$BBBB")]
    [TestCase("aesgcm-v1$kein-base64$x$y")]
    [TestCase("aesgcm-v1$AAAA$BBBB$CCCC")]
    [Test]
    public void TryUnprotect_ShouldRejectMalformedEntries(string stored)
    {
        InboundTriggerSecret.TryUnprotect(stored, Guid.NewGuid(), Key, out var opened).Should().BeFalse();
        opened.Should().BeEmpty();
    }

    // Testzweck: Ohne konfigurierten installationsweiten Schluessel gibt es kein
    // Schluesselmaterial; damit laesst sich nichts oeffnen und nichts pruefen.
    [Test]
    public void ResolveKey_ShouldBeEmptyWithoutConfiguration()
    {
        new InboundTriggerOptions { SecretKey = null }.ResolveKey().Should().BeEmpty();
        new InboundTriggerOptions { SecretKey = "   " }.ResolveKey().Should().BeEmpty();
        new InboundTriggerOptions { SecretKey = Passphrase }.ResolveKey().Should().HaveCount(32);
    }

    // Testzweck: Die Signatur gilt genau fuer diesen Zeitstempel und diesen Koerper. Aenderte
    // sich eines von beiden, waere ein einmal mitgelesener Aufruf beliebig wiederholbar oder
    // mit anderem Inhalt verwendbar.
    [Test]
    public void ComputeSignature_ShouldCoverTimestampAndBody()
    {
        const string secret = "geheim";
        const string body = """{"orderId":"4711"}""";

        var signature = InboundTriggerSecret.ComputeSignature(secret, 1_800_000_000, body);

        signature.Should().MatchRegex("^sha256=[0-9a-f]{64}$");
        signature.Should().NotBe(InboundTriggerSecret.ComputeSignature(secret, 1_800_000_001, body));
        signature.Should().NotBe(InboundTriggerSecret.ComputeSignature(secret, 1_800_000_000, body + " "));
        signature.Should().NotBe(InboundTriggerSecret.ComputeSignature(secret + "x", 1_800_000_000, body));
    }

    // Testzweck: Die Signatur entspricht dem dokumentierten Verfahren — HMAC-SHA256 ueber
    // "{timestamp}.{rawBody}", hexadezimal in Kleinbuchstaben. Ein fremdes System bildet sie
    // nach der Doku; weicht die Engine davon ab, kaeme keine Anbindung zustande.
    [Test]
    public void ComputeSignature_ShouldFollowTheDocumentedScheme()
    {
        const string secret = "geheim";
        const long timestamp = 1_800_000_000;
        const string body = """{"a":1}""";
        var expected = "sha256=" + Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();

        InboundTriggerSecret.ComputeSignature(secret, timestamp, body).Should().Be(expected);
    }

    // Testzweck: Eine fehlende oder abweichende Signatur wird abgelehnt; der Vergleich arbeitet
    // auf ganzer Laenge und bricht nicht beim ersten abweichenden Zeichen ab.
    [Test]
    public void SignatureMatches_ShouldRejectAnythingButTheExactValue()
    {
        var expected = InboundTriggerSecret.ComputeSignature("geheim", 1_800_000_000, "{}");

        InboundTriggerSecret.SignatureMatches(expected, expected).Should().BeTrue();
        InboundTriggerSecret.SignatureMatches(expected, null).Should().BeFalse();
        InboundTriggerSecret.SignatureMatches(expected, "").Should().BeFalse();
        InboundTriggerSecret.SignatureMatches(expected, expected.ToUpperInvariant()).Should().BeFalse();
        InboundTriggerSecret.SignatureMatches(expected, expected[..^1]).Should().BeFalse();
    }
}
