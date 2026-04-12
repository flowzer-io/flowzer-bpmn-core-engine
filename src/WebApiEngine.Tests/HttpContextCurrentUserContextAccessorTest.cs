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
