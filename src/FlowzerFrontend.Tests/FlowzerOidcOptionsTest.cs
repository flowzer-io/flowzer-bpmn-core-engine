using FluentAssertions;
using FlowzerFrontend.Auth;

namespace FlowzerFrontend.Tests;

public class FlowzerOidcOptionsTest
{
    // Testzweck: Ohne Authority und ClientId bleibt OIDC aus und das Frontend verhaelt sich wie bisher.
    [Test]
    public void IsEnabled_ShouldBeFalse_WhenNothingIsConfigured()
    {
        var options = new FlowzerOidcOptions();

        options.IsEnabled.Should().BeFalse();
        options.Invoking(candidate => candidate.Validate()).Should().NotThrow();
    }

    // Testzweck: Authority und ClientId zusammen aktivieren den OIDC-Login.
    [Test]
    public void IsEnabled_ShouldBeTrue_WhenAuthorityAndClientIdAreSet()
    {
        var options = new FlowzerOidcOptions
        {
            Authority = "https://login.microsoftonline.com/tenant/v2.0",
            ClientId = "spa-client"
        };

        options.IsEnabled.Should().BeTrue();
        options.Invoking(candidate => candidate.Validate()).Should().NotThrow();
    }

    // Testzweck: Eine halbe Konfiguration (nur Authority oder nur ClientId) ist ein Betriebsfehler
    // und muss beim Start sichtbar scheitern statt still ohne Login zu laufen.
    [TestCase("https://issuer.example", null)]
    [TestCase(null, "spa-client")]
    public void Validate_ShouldThrow_WhenOnlyOneValueIsConfigured(string? authority, string? clientId)
    {
        var options = new FlowzerOidcOptions { Authority = authority, ClientId = clientId };

        options.Invoking(candidate => candidate.Validate())
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Oidc:Authority*Oidc:ClientId*");
    }

    // Testzweck: openid und profile sind immer dabei, der API-Scope kommt dazu, Duplikate und
    // Leerwerte fallen weg.
    [Test]
    public void ResolveScopes_ShouldAlwaysIncludeOpenIdAndProfile_AndAppendConfiguredScopes()
    {
        var options = new FlowzerOidcOptions
        {
            Scopes = ["api://flowzer-api/access_as_user", " profile ", "", "openid"]
        };

        options.ResolveScopes().Should().Equal("openid", "profile", "api://flowzer-api/access_as_user");
    }
}
