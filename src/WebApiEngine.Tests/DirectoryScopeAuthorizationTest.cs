using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StorageSystem;
using WebApiEngine.Auth;
using WebApiEngine.IdentityDirectory;

namespace WebApiEngine.Tests;

/// <summary>Scope-Gates gegen synthetische Snapshots, ohne Realm- oder Benutzeränderungen.</summary>
public sealed class DirectoryScopeAuthorizationTest
{
    private const string Issuer = "https://issuer.test/realms/flowzer";
    private static readonly Guid User = Guid.Parse("bf68c19d-6a91-4086-a201-b4ea660bd957");
    private static readonly Guid Root = Guid.Parse("0919cbb1-c084-4081-86fa-a81971f8e6a7");
    private static readonly Guid Child = Guid.Parse("43029067-b736-4e4e-a87c-d832172bfc71");
    private static readonly string[] Policies =
    [
        FlowzerPolicies.Access, FlowzerPolicies.Modeler, FlowzerPolicies.Operator, FlowzerPolicies.Worker,
        FlowzerPolicies.IdentityDirectoryOperator, FlowzerPolicies.AiConnectionUse, FlowzerPolicies.AiConnectionManage
    ];
    private static readonly string[] Variants =
    [
        "valid", "missing", "outside-user", "revoked", "disabled-user", "disabled-root", "wrong-issuer",
        "wrong-root", "unscoped", "cycle", "storage-error", "foreign-source", "historical"
    ];
    private static IEnumerable<TestCaseData> AccessCases() => Policies.SelectMany(policy =>
        Variants.Select(variant => new TestCaseData(policy, variant)));

    // Testzweck: Jede explizite Policy braucht dieselbe aktuelle stabile Demo-Mitgliedschaft;
    // Operatorrollen, Namen und Token-Gruppen dürfen den Directory-Scope nicht ersetzen.
    [TestCaseSource(nameof(AccessCases))]
    public async Task EveryApplicationPolicy_ShouldEnforceCurrentScope(string policy, string variant)
    {
        using var services = Services(new SnapshotStorage(Snapshot(variant), variant == "storage-error"));
        var result = await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(Principal(), null, policy);
        result.Succeeded.Should().Be(variant is "valid" or "historical", $"{policy} must enforce {variant}");
    }

    // Testzweck: Die Fallback-Policy darf neue unmarkierte Endpunkte nicht am Scope vorbei öffnen;
    // die reine Sitzungsverwaltung bleibt dagegen für Abmeldung erreichbar.
    [Test]
    public async Task Fallback_ShouldBeScopedButSessionShouldAllowLogout()
    {
        using var services = Services(new SnapshotStorage(Snapshot("outside-user")));
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var fallback = await services.GetRequiredService<IAuthorizationPolicyProvider>().GetFallbackPolicyAsync();
        (await authorization.AuthorizeAsync(Principal(), null, fallback!)).Succeeded.Should().BeFalse();
        (await authorization.AuthorizeAsync(Principal(), null, FlowzerPolicies.Session)).Succeeded.Should().BeTrue();
    }

    // Testzweck: Fehlende/mehrdeutige authentifizierte Identitäten sind kein stabiles Subject;
    // identische Namen und Gruppenclaims oder Claims einer zweiten Identität reichen nie.
    [TestCase("no-issuer")][TestCase("no-subject")][TestCase("duplicate-subject")][TestCase("second-identity")]
    public async Task Scope_ShouldRequireUnambiguousAuthenticatedIdentity(string variant)
    {
        var principal = Principal();
        var identity = (ClaimsIdentity)principal.Identity!;
        if (variant == "no-issuer") identity.RemoveClaim(identity.FindFirst("iss"));
        if (variant == "no-subject") identity.RemoveClaim(identity.FindFirst("sub"));
        if (variant == "duplicate-subject") identity.AddClaim(new Claim("sub", User.ToString()));
        if (variant == "second-identity") principal.AddIdentity(new ClaimsIdentity([new Claim("sub", User.ToString())], "second"));
        using var services = Services(new SnapshotStorage(Snapshot("valid")));
        (await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(principal, null, FlowzerPolicies.Access))
            .Succeeded.Should().BeFalse();
    }

    // Testzweck: Opt-in verändert vorhandene realmweite Installationen nicht und benötigt
    // ohne Scope auch keinen Directory-Adapter, keine neue Rollenfreigabe und kein Netzwerk.
    [Test]
    public async Task EmptyRoot_ShouldPreserveExistingRolePolicies()
    {
        using var services = Services(null, scoped: false);
        (await services.GetRequiredService<IAuthorizationService>().AuthorizeAsync(Principal(), null, FlowzerPolicies.Operator))
            .Succeeded.Should().BeTrue();
    }

    // Testzweck: Installations-Scope ohne authentifizierten Betrieb ist keine gültige Konfiguration.
    [Test]
    public void ScopedAuthentication_ShouldRejectUnauthenticatedMode()
    {
        var settings = Settings(); settings["Authentication:Scheme"] = "None";
        FluentActions.Invoking(() => new ServiceCollection().AddFlowzerAuthentication(
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build())).Should().Throw<InvalidOperationException>();
    }

    // Testzweck: Auch ein anderer Directory-Client darf außerhalb der Wurzel liegende oder
    // unvollständige Daten nicht publizieren; der zuvor aktive Stand bleibt bei Fehler erhalten.
    [TestCase("valid")][TestCase("wrong-root")][TestCase("unscoped")][TestCase("cycle")][TestCase("revoked")]
    public async Task Synchronizer_ShouldPublishOnlyCompleteScopedSnapshots(string variant)
    {
        var original = Snapshot("valid")!;
        var storage = new SnapshotStorage(original);
        var source = Snapshot(variant)!;
        var client = new SourceClient(new KeycloakDirectorySnapshot(
            source.Users.Select(user => new KeycloakDirectoryUser(user.Subject, user.IsActive, "m1", null, null,
                source.Memberships.Where(member => member.UserId == user.Id)
                    .Select(member => source.Groups.Single(group => group.Id == member.GroupId).ExternalId).ToArray())).ToArray(),
            source.Groups.Select(group => new KeycloakDirectoryGroup(group.ExternalId, group.Name, group.Path,
                group.ParentId is null ? null : source.Groups.Single(parent => parent.Id == group.ParentId).ExternalId)).ToArray()));
        var synchronizer = new IdentityDirectorySynchronizer(client, storage, Options.Create(KeycloakDirectoryScopeTest.Options()),
            TimeProvider.System, NullLogger<IdentityDirectorySynchronizer>.Instance);
        var outcome = await synchronizer.SynchronizeAsync(CancellationToken.None);
        outcome.Should().Be(variant == "valid" ? IdentityDirectorySynchronizer.SynchronizationOutcome.Succeeded
            : IdentityDirectorySynchronizer.SynchronizationOutcome.Failed);
        if (variant == "valid") storage.Active.Should().NotBeSameAs(original);
        else { storage.Active.Should().BeSameAs(original); storage.FailureCode.Should().Be("invalid_response"); }
    }

    private static ServiceProvider Services(SnapshotStorage? storage, bool scoped = true)
    {
        var services = new ServiceCollection(); services.AddLogging();
        if (storage is not null) services.AddSingleton<IIdentityDirectoryStorage>(storage);
        var settings = Settings(); if (!scoped) settings["IdentityDirectory:RootGroupId"] = "";
        services.AddFlowzerAuthentication(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> Settings() => new()
    {
        ["Authentication:Scheme"] = "JwtBearer", ["Authentication:JwtBearer:Authority"] = Issuer,
        ["Authentication:JwtBearer:Audience"] = "flowzer-api", ["Authentication:JwtBearer:RequiredRole"] = "access",
        ["Authentication:JwtBearer:Roles:Modeler"] = "modeler", ["Authentication:JwtBearer:Roles:Operator"] = "operator",
        ["Authentication:JwtBearer:Roles:Worker"] = "worker", ["Authentication:JwtBearer:Roles:AiConnectionUser"] = "ai-use",
        ["Authentication:JwtBearer:Roles:AiConnectionManager"] = "ai-manage",
        ["IdentityDirectory:Enabled"] = "true", ["IdentityDirectory:Issuer"] = Issuer,
        ["IdentityDirectory:RootGroupId"] = KeycloakDirectoryScopeTest.RootId,
        ["IdentityDirectory:ServerUrl"] = "https://keycloak.example.invalid", ["IdentityDirectory:Realm"] = "flowzer",
        ["IdentityDirectory:ClientId"] = "directory-reader", ["IdentityDirectory:ClientSecret"] = "synthetic-only"
    };

    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity(
    [
        new Claim("iss", Issuer), new Claim("sub", User.ToString()), new Claim("preferred_username", "m1"),
        new Claim("groups", KeycloakDirectoryScopeTest.RootPath), new Claim("roles", "access"), new Claim("roles", "modeler"),
        new Claim("roles", "operator"), new Claim("roles", "worker"), new Claim("roles", "ai-use"), new Claim("roles", "ai-manage")
    ], "synthetic-auth"));

    private static DirectorySnapshot? Snapshot(string variant)
    {
        if (variant == "missing") return null;
        var result = new DirectorySnapshot
        {
            Issuer = Issuer, GenerationId = Guid.NewGuid(), CompletedAtUtc = DateTime.UtcNow,
            Users = [new DirectoryUser { Id = User, SourceKind = DirectorySourceKind.Keycloak, Issuer = Issuer,
                Subject = User.ToString(), DisplayName = "m1", IsActive = true }],
            Groups = [new DirectoryGroup { Id = Root, SourceKind = DirectorySourceKind.Keycloak, Issuer = Issuer,
                ExternalId = KeycloakDirectoryScopeTest.RootId, Name = "Demo", Path = KeycloakDirectoryScopeTest.RootPath, IsActive = true },
                new DirectoryGroup { Id = Child, SourceKind = DirectorySourceKind.Keycloak, Issuer = Issuer,
                ExternalId = KeycloakDirectoryScopeTest.ChildId, Name = "Personal", Path = KeycloakDirectoryScopeTest.RootPath + "/Personal",
                ParentId = Root, IsActive = true }],
            Memberships = [new DirectoryMembership { UserId = User, GroupId = Child }]
        };
        switch (variant)
        {
            case "outside-user": result.Users[0].Subject = Guid.NewGuid().ToString(); break;
            case "revoked": result.Memberships.Clear(); break;
            case "disabled-user": result.Users[0].IsActive = false; break;
            case "disabled-root": result.Groups[0].IsActive = false; break;
            case "wrong-issuer": result.Issuer += "/"; break;
            case "wrong-root": result.Groups[0].ExternalId = "different-root"; break;
            case "unscoped": result.Groups.Add(new DirectoryGroup { Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak,
                Issuer = Issuer, ExternalId = "outside-group", Name = "Other", Path = "/Other", IsActive = true }); break;
            case "cycle": result.Groups[0].ParentId = Child; break;
            case "foreign-source": result.Groups[0].SourceKind = (DirectorySourceKind)99; break;
            case "historical":
                result.Groups.Add(new DirectoryGroup { Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak,
                    Issuer = Issuer, ExternalId = "old-outside", Name = "Historisch", Path = "/Other", IsActive = false });
                result.Users.Add(new DirectoryUser { Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak,
                    Issuer = Issuer, Subject = "old-outside-user", DisplayName = "Historisch", IsActive = false });
                break;
        }
        return result;
    }

    private sealed class SourceClient(KeycloakDirectorySnapshot source) : IKeycloakAdminClient
    {
        public Task<KeycloakDirectorySnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(source);
    }

    private sealed class SnapshotStorage(DirectorySnapshot? initial, bool failRead = false) : IIdentityDirectoryStorage
    {
        internal DirectorySnapshot? Active { get; private set; } = initial;
        internal string? FailureCode { get; private set; }
        public Task<DirectorySnapshot?> GetActiveSnapshot() => failRead ? throw new IOException("synthetic") : Task.FromResult(Active);
        public Task<DirectorySyncStatus?> GetSyncStatus() => Task.FromResult<DirectorySyncStatus?>(null);
        public Task<bool> TryStartSync(string issuer, Guid generationId, DateTime startedAtUtc, DateTime leaseExpiresAtUtc) => Task.FromResult(true);
        public Task<bool> FailSync(string issuer, Guid generationId, string errorCode, string errorMessage, DateTime failedAtUtc)
        { FailureCode = errorCode; return Task.FromResult(true); }
        public Task PublishSnapshot(DirectorySnapshot snapshot) { Active = snapshot; return Task.CompletedTask; }
    }
}
