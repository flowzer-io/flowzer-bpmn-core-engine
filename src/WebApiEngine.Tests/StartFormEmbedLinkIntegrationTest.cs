using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Model;
using WebApiEngine.BusinessLogic;
using StorageSystem;
using WebApiEngine.IdentityDirectory;

namespace WebApiEngine.Tests;

/// <summary>Ein persönlicher Startformularsnapshot erzeugt weder Aufgabe noch Instanz.</summary>
[NonParallelizable]
public sealed class StartFormEmbedLinkIntegrationTest
{
    private static readonly Dictionary<string, string> Settings = new()
    {
        ["FormEmbedding:Enabled"] = "true",
        ["FormEmbedding:PublicOrigin"] = "https://flowzer.test",
        ["FormEmbedding:AllowedHostOrigins:0"] = "https://host.test"
    };

    // Testzweck: Der persönliche Startlink liefert einmalig nur das versionsgebundene
    // Formular. Er ist kein Login und legt insbesondere keine künstliche Aufgabe an.
    [Test]
    public async Task StartLink_ShouldRedeemOnceWithoutAnInstanceOrMutationAuthority()
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        await Publish(context);
        var definition = await context.DeployAsync("assignee=\"bert\"", startFormKey: "Approval");
        using var actor = context.CreateClient();
        using var frame = context.CreateAnonymousClient();
        var secret = await Issue(actor, definition);
        using var response = await Redeem(frame, secret);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        result.GetProperty("definitionId").GetGuid().Should().Be(definition.Id);
        result.GetProperty("relatedDefinitionId").GetString().Should().Be(definition.DefinitionId);
        result.GetProperty("hostOrigin").GetString().Should().Be("https://host.test");
        result.GetProperty("form").GetProperty("formData").GetString().Should().Contain("components");
        result.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "definitionId", "relatedDefinitionId", "hostOrigin", "form");
        (await Redeem(frame, secret)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await frame.PostAsJsonAsync("/form-embed/redeem", new { secret })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        frame.DefaultRequestHeaders.Add("X-Flowzer-Form-Link", secret);
        (await frame.PostAsJsonAsync($"/definition/meta/{definition.DefinitionId}/instance",
            new { expectedDefinitionId = definition.Id, variables = new { } })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().BeEmpty();
        (await context.Storage.SubscriptionStorage.GetAllUserTasksExtended(Guid.Empty)).Should().BeEmpty();
    }

    // Testzweck: Ein Start ohne Formular bleibt ausdrücklich ohne Link; es entsteht
    // weder ein Ersatzformular noch eine Grant-/Instanz-/Aufgabenattrappe.
    [Test]
    public async Task NoStartForm_ShouldReturnAnExplicitNullLink()
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        await Publish(context);
        var definition = await context.DeployAsync("assignee=\"bert\"");
        using var actor = context.CreateClient();
        using var response = await actor.PostAsJsonAsync(IssuePath(definition), new { hostOrigin = "https://host.test" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        result.GetProperty("definitionId").GetGuid().Should().Be(definition.Id);
        result.GetProperty("formLink").ValueKind.Should().Be(JsonValueKind.Null);
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().BeEmpty();
    }

    // Testzweck: Anonyme, unbekannte oder nach Linkausgabe entzogene Identitäten dürfen
    // keine Startformularanzeige erhalten; alte JWT-Gruppenclaims reichen nicht aus.
    [Test]
    public async Task StartLink_ShouldRecheckCurrentIdentityAtRedemption()
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        await Publish(context);
        var definition = await context.DeployAsync("assignee=\"bert\"", startFormKey: "Approval");
        using var actor = context.CreateClient();
        using var stranger = context.CreateClient(userId: Guid.NewGuid());
        using var frame = context.CreateAnonymousClient();
        (await frame.PostAsJsonAsync(IssuePath(definition), new { hostOrigin = "https://host.test" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await stranger.PostAsJsonAsync(IssuePath(definition), new { hostOrigin = "https://host.test" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var secret = await Issue(actor, definition);
        await Publish(context, active: false);
        (await Redeem(frame, secret)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Nur erlaubte HTTPS-Hosts erhalten persönliche Links; die Installation
    // bleibt ohne ausdrückliches Opt-in geschlossen.
    [TestCase(false, "https://host.test")]
    [TestCase(true, "https://evil.test")]
    public async Task StartLink_ShouldFailClosed(bool enabled, string host)
    {
        var settings = new Dictionary<string, string>(Settings) { ["FormEmbedding:Enabled"] = enabled.ToString() };
        using var context = new AuthenticatedWorkflowTestContext(settings);
        await Publish(context);
        var definition = await context.DeployAsync("assignee=\"bert\"", startFormKey: "Approval");
        using var actor = context.CreateClient();
        (await actor.PostAsJsonAsync(IssuePath(definition), new { hostOrigin = host })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Ein fehlender/leerer Versionsparameter wird nicht auf die aktuelle
    // Fassung ersetzt. Ein korrekt geformter, fremder Versionswert liefert den Konfliktvertrag.
    [TestCase(null, HttpStatusCode.BadRequest)]
    [TestCase("", HttpStatusCode.BadRequest)]
    [TestCase("00000000-0000-0000-0000-000000000000", HttpStatusCode.BadRequest)]
    [TestCase("11111111-1111-1111-1111-111111111111", HttpStatusCode.Conflict)]
    public async Task StartLink_ShouldRequireTheDisplayedVersion(string? version, HttpStatusCode expected)
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        await Publish(context);
        var definition = await context.DeployAsync("assignee=\"bert\"", startFormKey: "Approval");
        using var actor = context.CreateClient();
        var path = $"/definition/meta/{definition.DefinitionId}/start-form-link" + (version is null ? "" : "?expectedDefinitionId=" + version);
        (await actor.PostAsJsonAsync(path, new { hostOrigin = "https://host.test" })).StatusCode.Should().Be(expected);
    }

    // Testzweck: Die Einlösung endet exakt nach fünf Minuten; ein bereits geöffnetes
    // Startformular bleibt davon unabhängig mit einem erneuerten Benutzerrequest startbar.
    [Test]
    public async Task ExpiredEntry_ShouldNotInvalidateAnOpenedStartForm()
    {
        var clock = new FakeTimeProvider();
        using var context = new AuthenticatedWorkflowTestContext(Settings, clock);
        await Publish(context);
        var definition = await context.DeployAsync("assignee=\"bert\"", startFormKey: "Approval");
        using var actor = context.CreateClient();
        using var frame = context.CreateAnonymousClient();
        var opened = await Issue(actor, definition);
        var unused = await Issue(actor, definition);
        (await Redeem(frame, opened)).StatusCode.Should().Be(HttpStatusCode.OK);
        clock.Advance(TimeSpan.FromMinutes(5));
        (await Redeem(frame, unused)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        clock.Advance(TimeSpan.FromMinutes(41));
        using var renewed = context.CreateClient();
        renewed.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        (await renewed.PostAsJsonAsync($"/definition/meta/{definition.DefinitionId}/instance",
            new { expectedDefinitionId = definition.Id, variables = new { } })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().ContainSingle();
    }

    // Testzweck: Zwei opaque Frames erhalten genau einen Snapshot. Origin-null-CORS
    // bleibt ohne Credentials am neuen Read-only-Pfad und erlaubt keine API-Mutation.
    [Test]
    public async Task ConcurrentRedemption_ShouldHaveOneWinnerAndReadOnlyCors()
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        await Publish(context);
        var definition = await context.DeployAsync("assignee=\"bert\"", startFormKey: "Approval");
        using var actor = context.CreateClient();
        using var frame = context.CreateAnonymousClient();
        frame.DefaultRequestHeaders.Add("Origin", "null");
        var secret = await Issue(actor, definition);
        var responses = await Task.WhenAll(Redeem(frame, secret), Redeem(frame, secret));
        responses.Should().ContainSingle(r => r.StatusCode == HttpStatusCode.OK);
        responses.Should().ContainSingle(r => r.StatusCode == HttpStatusCode.NotFound);
        foreach (var response in responses)
        {
            response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("null");
            response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
            response.Dispose();
        }
    }

    // Testzweck: Die persönliche Versionsobergrenze gilt auch bei gleichzeitiger Ausgabe;
    // ältere Einstieg-Links werden verdrängt, nicht aber Formulare, die schon offen sind.
    [Test]
    public async Task StartLinks_ShouldBoundOutstandingEntriesPerPersonAndVersion()
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        await Publish(context);
        var definition = await context.DeployAsync("assignee=\"bert\"", startFormKey: "Approval");
        using var actor = context.CreateClient();
        using var frame = context.CreateAnonymousClient();
        var secrets = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Issue(actor, definition)));
        var responses = await Task.WhenAll(secrets.Select(secret => Redeem(frame, secret)));
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(4);
        foreach (var response in responses) response.Dispose();
    }

    // Testzweck: Der anonyme Einstieg darf weder ein neu gesetztes Wurzelgruppen-
    // Limit noch eine nach Ausgabe entfernte Hostfreigabe umgehen.
    [TestCase(true)]
    [TestCase(false)]
    public async Task Redemption_ShouldRecheckCurrentInstallationScope(bool groupScope)
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        await Publish(context);
        var definition = await context.DeployAsync("assignee=\"bert\"", startFormKey: "Approval");
        using var actor = context.CreateClient(); using var frame = context.CreateAnonymousClient();
        var secret = await Issue(actor, definition);
        if (groupScope)
        {
            var directory = context.Services.GetRequiredService<IOptions<KeycloakDirectoryOptions>>().Value;
            directory.Issuer = AuthenticatedWorkflowTestContext.Issuer;
            directory.RootGroupId = KeycloakDirectoryScopeTest.RootId;
        }
        else context.Services.GetRequiredService<IOptions<WebApiEngine.FormEmbedding.FormEmbeddingOptions>>().Value.AllowedHostOrigins = [];
        (await Redeem(frame, secret)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Ein echtes Redeployment entwertet den noch ungeöffneten alten Link.
    // Ein bereits geöffneter Start bleibt an V1 gebunden und erzeugt unter V2 nichts.
    [Test]
    public async Task Redeployment_ShouldInvalidateOldEntryAndRejectOldStartVersion()
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        await Publish(context);
        var old = await context.DeployAsync("assignee=\"bert\"", startFormKey: "Approval");
        using var actor = context.CreateClient(); using var frame = context.CreateAnonymousClient();
        var opened = await Issue(actor, old); var unused = await Issue(actor, old);
        (await Redeem(frame, opened)).StatusCode.Should().Be(HttpStatusCode.OK);
        var next = new BpmnDefinition { Id = Guid.NewGuid(), DefinitionId = old.DefinitionId, PreviousGuid = old.Id,
            Hash = "synthetic-v2", SavedByUser = AuthenticatedWorkflowTestContext.UserId,
            SavedOn = DateTime.UtcNow, Version = new Model.Version(2, 0), IsActive = false };
        await context.Storage.DefinitionStorage.StoreDefinition(next);
        await context.Storage.DefinitionStorage.StoreBinary(next.Id, await context.Storage.DefinitionStorage.GetBinary(old.Id));
        await context.Services.GetRequiredService<BpmnBusinessLogic>().DeployDefinition(next);
        (await Redeem(frame, unused)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await actor.PostAsJsonAsync(IssuePath(old), new { hostOrigin = "https://host.test" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        actor.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        (await actor.PostAsJsonAsync($"/definition/meta/{old.DefinitionId}/instance",
            new { expectedDefinitionId = old.Id, variables = new { } })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().BeEmpty();
        (await Redeem(frame, await Issue(actor, next))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Der opaque application/json-Preflight gilt nur am lesenden
    // Start-Einstieg, ohne Credentials und ohne freigegebene fremde Origin.
    [TestCase("null", true)]
    [TestCase("https://evil.test", false)]
    public async Task StartRedemption_ShouldHaveIsolatedJsonPreflight(string origin, bool allowed)
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        using var frame = context.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/form-embed/start/redeem");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        using var response = await frame.SendAsync(request);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().Be(allowed);
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
        if (allowed) response.Headers.GetValues("Access-Control-Allow-Methods").Should().Equal("POST");
    }

    // Testzweck: Der neue Einstieg darf bestehende anonyme Read-only-Kontingente
    // nicht durch einen weiteren Pfad umgehen, auch wenn der allgemeine Limiter aus ist.
    [TestCase("/form-embed/start/redeem")]
    [TestCase("/form-embed/start/redeem/")]
    public async Task StartRedemption_ShouldShareTheBoundedReadOnlyBudget(string path)
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        using var frame = context.CreateAnonymousClient();
        for (var i = 0; i < 60; i++)
            (await frame.PostAsJsonAsync(path, new { secret = new string('A', 43) })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await frame.PostAsJsonAsync("/form-embed/redeem", new { secret = new string('A', 43) })).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        using var actor = context.CreateClient();
        (await actor.GetAsync("/usertask")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string IssuePath(BpmnDefinition definition) =>
        $"/definition/meta/{definition.DefinitionId}/start-form-link?expectedDefinitionId={definition.Id}";

    private static async Task<string> Issue(HttpClient actor, BpmnDefinition definition)
    {
        using var response = await actor.PostAsJsonAsync(IssuePath(definition), new { hostOrigin = "https://host.test" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        result.GetProperty("definitionId").GetGuid().Should().Be(definition.Id);
        var link = result.GetProperty("formLink");
        var url = link.GetProperty("url").GetString()!;
        url.Should().StartWith("https://flowzer.test/embed.html#start.");
        return url.Split("#start.")[1];
    }

    private static Task<HttpResponseMessage> Redeem(HttpClient frame, string secret) =>
        frame.PostAsJsonAsync("/form-embed/start/redeem", new { secret });

    private static async Task Publish(AuthenticatedWorkflowTestContext context, bool active = true)
    {
        var now = DateTime.UtcNow;
        var snapshot = new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(), Issuer = AuthenticatedWorkflowTestContext.Issuer, CompletedAtUtc = now,
            Users = active ? [new DirectoryUser { Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak,
                Issuer = AuthenticatedWorkflowTestContext.Issuer, Subject = AuthenticatedWorkflowTestContext.UserId.ToString(),
                DisplayName = "Bert", IsActive = true }] : []
        };
        (await context.Storage.IdentityDirectoryStorage.TryStartSync(snapshot.Issuer, snapshot.GenerationId, now.AddSeconds(-1), now.AddMinutes(5))).Should().BeTrue();
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(snapshot);
    }
}
