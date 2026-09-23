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
