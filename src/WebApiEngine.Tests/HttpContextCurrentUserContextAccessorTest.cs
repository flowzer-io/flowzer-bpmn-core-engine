using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using WebApiEngine.Auth;

namespace WebApiEngine.Tests;

[NonParallelizable]
public class HttpContextCurrentUserContextAccessorTest
{
    // Testzweck: Fuer die Zuweisungspruefung sammelt der Kontext alle Kennungen, unter denen ein
    // BPMN-Modell die Person meinen kann, und ihre Gruppen aus dem groups-Claim.
    [Test]
    public void GetCurrentUser_ShouldCollectAllKnownNamesAndGroups()
    {
        var userId = Guid.NewGuid();
        var accessor = CreateAccessor(
            environmentName: Environments.Production,
            claims:
            [
                new Claim("sub", userId.ToString()),
                new Claim("preferred_username", "anna"),
                new Claim("email", "anna@maass.it"),
                new Claim("groups", "/abteilungen/buchhaltung"),
                new Claim("groups", "/client-access/flowzer")
            ]);

        var currentUser = accessor.GetCurrentUser();

        currentUser.Names.Should().Contain(["anna", "anna@maass.it", userId.ToString()]);
        currentUser.Groups.Should().BeEquivalentTo(["/abteilungen/buchhaltung", "/client-access/flowzer"]);
    }

    // Testzweck: Ohne Anmeldung bleiben Kennungen und Gruppen leer, damit die Zuweisungspruefung
    // nichts faelschlich zuordnet.
    [Test]
    public void GetCurrentUser_ShouldReportNoNamesOrGroups_WhenNobodyIsAuthenticated()
    {
        var accessor = CreateAccessor(environmentName: Environments.Production, claims: []);

        var currentUser = accessor.GetCurrentUser();

        currentUser.Names.Should().BeEmpty();
        currentUser.Groups.Should().BeEmpty();
    }

    // Testzweck: Deckt den Fall „Get Current User Should Resolve Name Identifier Claim“ ab.
    [Test]
    public void GetCurrentUser_ShouldResolveNameIdentifierClaim()
    {
        var expectedUserId = Guid.NewGuid();
        var accessor = CreateAccessor(
            environmentName: Environments.Production,
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, expectedUserId.ToString())
            ]);

        var currentUser = accessor.GetCurrentUser();

        currentUser.UserId.Should().Be(expectedUserId);
        currentUser.Source.Should().Be("claim:nameidentifier");
        currentUser.IsFallback.Should().BeFalse();
    }

    // Testzweck: Deckt den Fall „Get Current User Should Resolve Sub Claim When Name Identifier Is Missing“ ab.
    [Test]
    public void GetCurrentUser_ShouldResolveSubClaim_WhenNameIdentifierIsMissing()
    {
        var expectedUserId = Guid.NewGuid();
        var accessor = CreateAccessor(
            environmentName: Environments.Production,
            claims:
            [
                new Claim("sub", expectedUserId.ToString())
            ]);

        var currentUser = accessor.GetCurrentUser();

        currentUser.UserId.Should().Be(expectedUserId);
        currentUser.Source.Should().Be("claim:sub");
        currentUser.IsFallback.Should().BeFalse();
    }

    // Testzweck: Deckt den Fall „Get Current User Should Use Header In Development“ ab.
    [Test]
    public void GetCurrentUser_ShouldUseHeaderInDevelopment()
    {
        var expectedUserId = Guid.NewGuid();
        var accessor = CreateAccessor(
            environmentName: Environments.Development,
            headerUserId: expectedUserId);

        var currentUser = accessor.GetCurrentUser();

        currentUser.UserId.Should().Be(expectedUserId);
        currentUser.Source.Should().Be("header:x-flowzer-userid");
        currentUser.IsFallback.Should().BeFalse();
    }

    // Testzweck: Deckt den Fall „Get Current User Should Ignore Header Outside Development“ ab.
    [Test]
    public void GetCurrentUser_ShouldIgnoreHeaderOutsideDevelopment()
    {
        var expectedUserId = Guid.NewGuid();
        var accessor = CreateAccessor(
            environmentName: Environments.Production,
            headerUserId: expectedUserId);

        var currentUser = accessor.GetCurrentUser();

        currentUser.UserId.Should().Be(HttpContextCurrentUserContextAccessor.FallbackUserId);
        currentUser.Source.Should().Be("fallback:system-user");
        currentUser.IsFallback.Should().BeTrue();
    }

    // Testzweck: Ist der Request bereits authentifiziert (z. B. JWT ohne GUID-Claim), darf der
    // technische Development-Header die Identitaet nicht ueberschreiben. Er ist nur ein Ersatz
    // fuer fehlende Authentifizierung.
    [Test]
    public void GetCurrentUser_ShouldIgnoreHeader_WhenRequestIsAuthenticatedWithoutGuidClaim()
    {
        var accessor = CreateAccessor(
            environmentName: Environments.Development,
            claims: [new Claim("sub", "opaque-subject")],
            headerUserId: Guid.NewGuid());

        var currentUser = accessor.GetCurrentUser();

        currentUser.IsFallback.Should().BeTrue();
        currentUser.Source.Should().Be("fallback:system-user");
    }

    private static HttpContextCurrentUserContextAccessor CreateAccessor(
        string environmentName,
        IEnumerable<Claim>? claims = null,
        Guid? headerUserId = null)
    {
        var httpContext = new DefaultHttpContext();

        if (claims is not null)
        {
            httpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(claims, authenticationType: "test"));
        }

        if (headerUserId.HasValue)
        {
            httpContext.Request.Headers[HttpContextCurrentUserContextAccessor.UserIdHeaderName] =
                headerUserId.Value.ToString();
        }

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = httpContext
        };

        return new HttpContextCurrentUserContextAccessor(
            httpContextAccessor,
            new TestHostEnvironment(environmentName));
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "WebApiEngine.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
