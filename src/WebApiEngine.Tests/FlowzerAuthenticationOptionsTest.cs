using FluentAssertions;
using WebApiEngine.Auth;

namespace WebApiEngine.Tests;

/// <summary>
/// Startverhalten bei leeren privilegierten Rollennamen: Ohne ausdrueckliche Legacy-Wahl darf
/// die API mit aktiver Authentifizierung nicht starten, weil ein leerer Name die jeweilige
/// Faehigkeit fuer jede angemeldete Person oeffnet.
/// </summary>
public class FlowzerAuthenticationOptionsTest
{
    // Testzweck: Fehlt ein privilegierter Rollenname und ist der Legacy-Schalter nicht gesetzt,
    // bricht die Validierung ab und nennt den Konfigurationsschluessel samt Schalter.
    [TestCase("JwtBearer")]
    [TestCase("Bff")]
    public void Validate_ShouldThrowAndNameTheKey_WhenAPrivilegedRoleIsMissing(string scheme)
    {
        var options = CreateOptions(scheme);
        options.JwtBearer.Roles.Modeler = string.Empty;

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Authentication:JwtBearer:Roles:Modeler*")
            .WithMessage("*Authentication:JwtBearer:LegacyPermissiveRoles=true*");
    }

    // Testzweck: Die Liste der fehlenden Schluessel umfasst genau Zugangs-, Modeler-, Operator-
    // und Worker-Rolle; die KI-Rollen sind bei leerem Wert bereits fail-closed und fehlen darin.
    [Test]
    public void MissingPrivilegedRoleKeys_ShouldListOnlyThePrivilegedRoles()
    {
        var settings = new FlowzerAuthenticationOptions.JwtBearerSettings();

        settings.MissingPrivilegedRoleKeys().Should().Equal(
            "Authentication:JwtBearer:RequiredRole",
            "Authentication:JwtBearer:Roles:Modeler",
            "Authentication:JwtBearer:Roles:Operator",
            "Authentication:JwtBearer:Roles:Worker");
    }

    // Testzweck: Mit ausdruecklich gewaehlter Legacy-Kompatibilitaet startet eine
    // Bestandsinstallation auch ohne Rollennamen.
    [Test]
    public void Validate_ShouldNotThrow_WhenRolesAreMissingButLegacyIsChosen()
    {
        var options = CreateOptions(FlowzerAuthenticationOptions.SchemeJwtBearer);
        options.JwtBearer.RequiredRole = string.Empty;
        options.JwtBearer.Roles = new FlowzerAuthenticationOptions.ApplicationRoles();
        options.JwtBearer.LegacyPermissiveRoles = true;

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    // Testzweck: Ohne Authentifizierung sind Rollennamen wirkungslos; Schema None scheitert
    // deshalb nie an fehlenden Rollen.
    [Test]
    public void Validate_ShouldIgnoreRoles_WhenSchemeIsNone()
    {
        var options = new FlowzerAuthenticationOptions { Scheme = FlowzerAuthenticationOptions.SchemeNone };

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    // Testzweck: Sind alle privilegierten Rollennamen gesetzt, besteht die Validierung auch ohne
    // Legacy-Schalter und ohne KI-Rollen.
    [TestCase("JwtBearer")]
    [TestCase("Bff")]
    public void Validate_ShouldNotThrow_WhenAllPrivilegedRolesAreSet(string scheme)
    {
        var options = CreateOptions(scheme);

        var act = () => options.Validate();

        act.Should().NotThrow();
        options.JwtBearer.MissingPrivilegedRoleKeys().Should().BeEmpty();
    }

    // Testzweck: Der Ruecksprungpfad nach dem Provider-Logout wird an den Origin gehaengt und
    // muss deshalb wie returnTo ein lokaler absoluter Pfad sein; alles andere verhindert den Start.
    [TestCase("")]
    [TestCase("abgemeldet")]
    [TestCase("https://evil.example/")]
    [TestCase("//evil.example")]
    [TestCase("/\\evil.example")]
    [TestCase("/ok\r\nSet-Cookie: x=y")]
    public void Validate_ShouldRejectNonLocalPostLogoutPath(string postLogoutPath)
    {
        var options = CreateOptions(FlowzerAuthenticationOptions.SchemeBff);
        options.Bff.PostLogoutPath = postLogoutPath;

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>().WithMessage("*Authentication:Bff:PostLogoutPath*");
    }

    // Testzweck: Standardwert und lokale Pfade mit Query sind als Ruecksprungziel zulaessig;
    // der Provider-Logout selbst bleibt ohne ausdrueckliche Wahl abgeschaltet.
    [TestCase("/")]
    [TestCase("/abgemeldet?grund=logout")]
    public void Validate_ShouldAcceptLocalPostLogoutPath(string postLogoutPath)
    {
        var options = CreateOptions(FlowzerAuthenticationOptions.SchemeBff);
        options.Bff.PostLogoutPath = postLogoutPath;

        var act = () => options.Validate();

        act.Should().NotThrow();
        new FlowzerAuthenticationOptions.BffSettings().ProviderLogout.Should().BeFalse();
        new FlowzerAuthenticationOptions.BffSettings().PostLogoutPath.Should().Be("/");
    }

    // Testzweck: Mit Provider-Logout gelangt das ID-Token in den Browser. Stimmt die API-Audience
    // mit der BFF-Client-ID ueberein (auch als api://<ClientId>), waere es ein gueltiger Bearer;
    // der Start muss dann scheitern.
    [TestCase("flowzer-console")]
    [TestCase(" FLOWZER-CONSOLE ")]
    [TestCase("api://flowzer-console")]
    [TestCase("API://Flowzer-Console")]
    public void Validate_ShouldRejectProviderLogout_WhenAudienceIsTheBffClient(string audience)
    {
        var options = CreateOptions(FlowzerAuthenticationOptions.SchemeBff);
        options.Bff.ProviderLogout = true;
        options.JwtBearer.Audience = audience;

        var act = () => options.Validate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Authentication:Bff:ProviderLogout*")
            .WithMessage("*Authentication:JwtBearer:Audience*");
    }

    // Testzweck: Eine eigene API-Audience erlaubt den Provider-Logout; ohne den Schalter bleibt
    // auch eine gemeinsame Audience wie bisher zulaessig.
    [TestCase(true, "flowzer-api")]
    [TestCase(true, "api://flowzer-api")]
    [TestCase(false, "flowzer-console")]
    public void Validate_ShouldAllowProviderLogout_WhenAudienceDiffersOrSwitchIsOff(bool providerLogout, string audience)
    {
        var options = CreateOptions(FlowzerAuthenticationOptions.SchemeBff);
        options.Bff.ProviderLogout = providerLogout;
        options.JwtBearer.Audience = audience;

        var act = () => options.Validate();

        act.Should().NotThrow();
    }

    private static FlowzerAuthenticationOptions CreateOptions(string scheme) => new()
    {
        Scheme = scheme,
        JwtBearer = new FlowzerAuthenticationOptions.JwtBearerSettings
        {
            Authority = "https://issuer.test/realms/flowzer",
            Audience = "flowzer-api",
            RequiredRole = "flowzer-access",
            Roles = new FlowzerAuthenticationOptions.ApplicationRoles
            {
                Modeler = "flowzer-modeler",
                Operator = "flowzer-operator",
                Worker = "flowzer-worker"
            }
        },
        Bff = new FlowzerAuthenticationOptions.BffSettings
        {
            ClientId = "flowzer-console",
            ClientSecret = "test-only",
            DataProtectionKeysPath = "/tmp/flowzer-keys-not-touched"
        }
    };
}
