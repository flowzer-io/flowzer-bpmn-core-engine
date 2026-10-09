using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using WebApiEngine.IdentityDirectory;

namespace WebApiEngine.Tests;

/// <summary>Nur synthetische HTTP-Quellen; reale Keycloak-/Demo-Gruppen werden nicht angelegt oder zugeordnet.</summary>
public sealed class KeycloakDirectoryScopeTest
{
    internal const string RootId = "demo-root", ChildId = "demo-personal";
    internal const string RootPath = "/Organisation/Demo";

    // Testzweck: Gruppenmitglieder statt globalem User-/Rootkatalog, verschachtelte Mitglieder,
    // deduplizierte Personen und keine fremden Gruppen/Profile im publizierbaren Ergebnis.
    [Test]
    public async Task ScopedSnapshot_ShouldReadOnlyRootTreeAndMemberProfiles()
    {
        using var handler = new Handler("valid"); using var http = new HttpClient(handler);
        var snapshot = await Client(http).GetSnapshotAsync(CancellationToken.None);
        snapshot.Groups.Select(group => group.Id).Should().BeEquivalentTo(RootId,ChildId);
        snapshot.Groups.Single(group => group.Id == RootId).ParentId.Should().BeNull();
        snapshot.Groups.Single(group => group.Id == ChildId).ParentId.Should().Be(RootId);
        snapshot.Users.Select(user => user.Subject).Should().BeEquivalentTo("user-a","user-b");
        snapshot.Users.Single(user => user.Subject == "user-a").Groups.Should().BeEquivalentTo(RootId,ChildId);
        snapshot.Users.Single(user => user.Subject == "user-b").Enabled.Should().BeFalse();
        handler.Requests.Should().NotContain(path => path.StartsWith("/admin/realms/flowzer/users?",StringComparison.Ordinal)
            || path.StartsWith("/admin/realms/flowzer/groups?",StringComparison.Ordinal));
        handler.Requests.Count(path => path == "/admin/realms/flowzer/users/user-a").Should().Be(1);
        JsonSerializer.Serialize(snapshot).Should().NotContain("outside-secret");
    }

    // Testzweck: Fehlende/vertauschte Wurzel oder während des Imports neu auftauchende
    // Untergruppe darf niemals still in einen breiteren oder partiellen Snapshot fallen.
    [TestCase("missing-root",KeycloakAdminClientFailureKind.NotFound)]
    [TestCase("wrong-root",KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("new-child",KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("wrong-user",KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("duplicate-member",KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("moved-child",KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("moved-membership",KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("unsafe-member",KeycloakAdminClientFailureKind.InvalidResponse)]
    [TestCase("unsafe-child",KeycloakAdminClientFailureKind.InvalidResponse)]
    public async Task ScopedSnapshot_ShouldFailClosedOnSourceDrift(string variant,KeycloakAdminClientFailureKind kind)
    {
        using var handler = new Handler(variant); using var http = new HttpClient(handler);
        var error = await FluentActions.Awaiting(()=>Client(http).GetSnapshotAsync(CancellationToken.None)).Should().ThrowAsync<KeycloakAdminClientException>();
        error.Which.Kind.Should().Be(kind); error.Which.ToString().Should().NotContain("outside-secret");
    }

    // Testzweck: Unsichere/mehrdeutige IDs und Scope bei deaktiviertem Abgleich stoppen vor HTTP.
    [TestCase("../other")][TestCase("demo/root")][TestCase("demo root")][TestCase("demo%2Froot")][TestCase("disabled")]
    public async Task ScopedOptions_ShouldFailBeforeAnyRequest(string variant)
    {
        var options = Options(); options.RootGroupId = variant == "disabled" ? RootId : variant;
        options.Enabled = variant != "disabled";
        options.IsValid().Should().BeFalse();
        using var handler = new Handler("valid"); using var http = new HttpClient(handler);
        var error = await FluentActions.Awaiting(()=>new KeycloakAdminClient(http,Microsoft.Extensions.Options.Options.Create(options))
            .GetSnapshotAsync(CancellationToken.None)).Should().ThrowAsync<KeycloakAdminClientException>();
        error.Which.Kind.Should().Be(KeycloakAdminClientFailureKind.Configuration); handler.Requests.Should().BeEmpty();
    }

    internal static KeycloakDirectoryOptions Options() => new()
    {
        Enabled=true,ServerUrl="https://keycloak.example.invalid",Issuer="https://issuer.test/realms/flowzer",
        Realm="flowzer",ClientId="directory-reader",ClientSecret="synthetic-only",RootGroupId=RootId,
        PageSize=2,MaxRetries=0,RetryDelayMilliseconds=0,
    };
    private static KeycloakAdminClient Client(HttpClient http)=>new(http,Microsoft.Extensions.Options.Options.Create(Options()));

    // Testzweck: Sichere opaque User-Storage-IDs mit Doppelpunkten, Unicode und
    // längeren externen IDs bleiben unverändert; keine UUID-/ASCII-Normalisierung.
    [TestCase("f:provider:m1.demo@example.test")]
    [TestCase("f:provider:mäx")]
    [TestCase("f:provider:ユーザー")]
    [TestCase("f:provider:🙂")]
    [TestCase("long")]
    public async Task ScopedSnapshot_ShouldPreserveSafeOpaqueStorageIds(string subject)
    {
        if (subject == "long") subject = "f:provider:" + new string('a', 300);
        using var handler = new Handler("valid", subject); using var http = new HttpClient(handler);
        var snapshot = await Client(http).GetSnapshotAsync(CancellationToken.None);
        snapshot.Users.Should().ContainSingle(user => user.Subject == subject);
        handler.Requests.Should().Contain("/admin/realms/flowzer/users/" + Uri.EscapeDataString(subject));
        handler.Requests.Should().NotContain(path => path.StartsWith("/admin/realms/flowzer/users?", StringComparison.Ordinal));
    }

    // Testzweck: Kein Separator, mehrstufiges Encoding, Punktsegment oder fehlerhaftes
    // Unicode darf vor einem Folgeabruf zu einem anderen Resource-Pfad werden.
    [TestCase(".")][TestCase("..")][TestCase("other/user")][TestCase("other\\user")]
    // Testzweck: Kein Encoding-/Whitespace-/Query-/Fragment-Segment als Provider-ID.
    [TestCase("%2e")][TestCase("user space")][TestCase("user?query")][TestCase("user#fragment")]
    // Testzweck: Fehlerhaftes UTF-16 muss vor einem normalisierenden Folgeabruf schließen.
    [TestCase("invalid-utf16")]
    public async Task ScopedSnapshot_ShouldRejectUnsafeMemberSegmentsBeforeProfileGet(string subject)
    {
        using var handler = new Handler(subject == "invalid-utf16" ? subject : "valid", subject);
        using var http = new HttpClient(handler);
        var error = await FluentActions.Awaiting(() => Client(http).GetSnapshotAsync(CancellationToken.None))
            .Should().ThrowAsync<KeycloakAdminClientException>();
        error.Which.Kind.Should().Be(KeycloakAdminClientFailureKind.InvalidResponse);
        handler.Requests.Should().NotContain(path => path.StartsWith("/admin/realms/flowzer/users/", StringComparison.Ordinal));
    }

    // Testzweck: Ein Slash im legitimen Gruppennamen ist keine weitere Hierarchieebene;
    // Keycloak liefert je Installationsmodus den Roh- oder ~/ escaped Pfad.
    [TestCase(false)][TestCase(true)]
    public async Task ScopedSnapshot_ShouldAcceptDirectChildrenWithSlashesInName(bool escaped)
    {
        var childPath = RootPath + "/" + (escaped ? "HR~/Finance" : "HR/Finance");
        using var handler = new Handler("valid", groupName: "HR/Finance", groupPath: childPath);
        using var http = new HttpClient(handler);
        var snapshot = await Client(http).GetSnapshotAsync(CancellationToken.None);
        snapshot.Groups.Single(group => group.Id == ChildId).Should().Be(
            new KeycloakDirectoryGroup(ChildId, "HR/Finance", childPath, RootId));
    }

    private sealed class Handler(string variant, string userSubject = "user-a", string groupName = "Personal", string? groupPath = null):HttpMessageHandler
    {
        private string ChildPath => groupPath ?? RootPath + "/" + groupName;
        internal List<string> Requests {get;}=[];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
        {
            var path=request.RequestUri!.PathAndQuery; Requests.Add(path);
            if(request.Method==HttpMethod.Post)
                return Task.FromResult(Json(new{access_token="synthetic-token",expires_in=300}));
            request.Headers.Authorization!.Scheme.Should().Be("Bearer");
            const string prefix="/admin/realms/flowzer/";
            var suffix=path[prefix.Length..].Replace(Uri.EscapeDataString(userSubject), "user-a", StringComparison.Ordinal);
            return Task.FromResult(suffix switch
            {
                "groups/demo-root" when variant=="missing-root" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "groups/demo-root"=>Json(new{id=variant=="wrong-root"?"another-root":RootId,name="Demo",path=RootPath}),
                "groups/demo-root/children?first=0&max=2&briefRepresentation=true"=>Json(new[]{new{id=variant=="unsafe-child"?".":ChildId,name=groupName,path=variant=="moved-child"?"/Private/Personal":ChildPath}}),
                "groups/demo-personal/children?first=0&max=2&briefRepresentation=true"=>Json(Array.Empty<object>()),
                "groups/demo-root/members?first=0&max=2&briefRepresentation=true" when variant=="invalid-utf16"=>new HttpResponseMessage(HttpStatusCode.OK)
                    {Content=new StringContent("""[{"id":"\uD800"}]""",System.Text.Encoding.UTF8,"application/json")},
                "groups/demo-root/members?first=0&max=2&briefRepresentation=true"=>Json(variant=="duplicate-member"?new[]{new{id=userSubject},new{id=userSubject}}:new[]{new{id=variant=="unsafe-member"?".":userSubject},new{id="user-b"}}),
                "groups/demo-root/members?first=2&max=2&briefRepresentation=true"=>Json(Array.Empty<object>()),
                "groups/demo-personal/members?first=0&max=2&briefRepresentation=true"=>Json(new[]{new{id=userSubject},new{id="user-revoked"}}),
                "groups/demo-personal/members?first=2&max=2&briefRepresentation=true"=>Json(Array.Empty<object>()),
                "users/user-a"=>Json(new{id=variant=="wrong-user"?"outside-secret":userSubject,enabled=true,username="m1",firstName="Miriam",email="m1@example.test",attributes=new{secret="outside-secret"}}),
                "users/user-b"=>Json(new{id="user-b",enabled=false,username="m2"}),
                "users/user-revoked"=>Json(new{id="user-revoked",enabled=true,username="former"}),
                "users/user-a/groups?first=0&max=2&briefRepresentation=true"=>Json(new[]{new{id=RootId,path=RootPath},new{id=ChildId,path=variant=="moved-membership"?"/Private/Personal":ChildPath}}),
                "users/user-a/groups?first=2&max=2&briefRepresentation=true"=>Json(new[]{new{id=variant=="new-child"?"new-child":"outside-secret",path=variant=="new-child"?RootPath+"/NewChild":RootPath+"-other"}}),
                "users/user-b/groups?first=0&max=2&briefRepresentation=true"=>Json(new[]{new{id=RootId,path=RootPath}}),
                "users/user-revoked/groups?first=0&max=2&briefRepresentation=true"=>Json(new[]{new{id="outside-secret",path="/Private"}}),
                // Der alte realmweite Pfad bleibt testbar, würde aber diesen Scoped-Vertrag verletzen.
                "users?first=0&max=2&briefRepresentation=true"=>Json(new[]{new{id="outside-secret",enabled=true,username="outside-secret"}}),
                "groups?first=0&max=2&briefRepresentation=true"=>Json(Array.Empty<object>()),
                "users/outside-secret/groups?first=0&max=2&briefRepresentation=true"=>Json(Array.Empty<object>()),
                _=>throw new AssertionException("Unexpected synthetic Keycloak request: "+suffix),
            });
        }
        private static HttpResponseMessage Json(object body)=>new(HttpStatusCode.OK){Content=JsonContent.Create(body)};
    }
}
