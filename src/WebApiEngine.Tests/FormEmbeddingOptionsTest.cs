using FluentAssertions;
using WebApiEngine.FormEmbedding;

namespace WebApiEngine.Tests;

/// <summary>Verhindert Wildcards, Pfade, HTTP und Credentials in der Embed-Konfiguration.</summary>
public sealed class FormEmbeddingOptionsTest
{
    // Testzweck: Nur exakte HTTPS-Origins sind als bewusstes Installations-Opt-in zulässig.
    [TestCase("https://host.test", true)]
    [TestCase("https://host.test:8443", true)]
    [TestCase("http://host.test", false)]
    [TestCase("https://host.test/", false)]
    [TestCase("https://host.test/other", false)]
    [TestCase("https://user:password@host.test", false)]
    [TestCase("https://host.test?q=1", false)]
    [TestCase("null", false)]
    [TestCase("*", false)]
    public void Enabled_ShouldValidateExactHttpsOrigins(string origin, bool valid)
    {
        new FormEmbeddingOptions { Enabled = true, PublicOrigin = "https://flowzer.test", AllowedHostOrigins = [origin] }
            .IsValid().Should().Be(valid);
    }

    // Testzweck: Die Defaultinstallation bleibt geschlossen; die Host-Allowlist kennt keinen
    // Prefixvergleich, der eine täuschend ähnliche fremde Domain akzeptieren würde.
    [Test]
    public void Default_ShouldBeClosedAndHostMatchingExact()
    {
        new FormEmbeddingOptions().IsValid().Should().BeTrue();
        new FormEmbeddingOptions().Allows("https://host.test").Should().BeFalse();
        var options = new FormEmbeddingOptions { Enabled = true, PublicOrigin = "https://flowzer.test",
            AllowedHostOrigins = ["https://host.test"] };
        options.Allows("https://host.test.evil.test").Should().BeFalse();
        options.Allows("https://host.test").Should().BeTrue();
    }
}
