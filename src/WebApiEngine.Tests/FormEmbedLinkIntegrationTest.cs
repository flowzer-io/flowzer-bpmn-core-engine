using System.Dynamic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.DependencyInjection;
using Model;
using StorageSystem;


namespace WebApiEngine.Tests;

/// <summary>Prüft den strikt lesenden, persönlichen Einmal-Einstieg ohne Browseranmeldung.</summary>
[NonParallelizable]
public sealed class FormEmbedLinkIntegrationTest
{
    private static readonly Dictionary<string, string> Settings = new()
    {
        ["FormEmbedding:Enabled"] = "true",
        ["FormEmbedding:PublicOrigin"] = "https://flowzer.test",
        ["FormEmbedding:AllowedHostOrigins:0"] = "https://host.test"
    };

    // Testzweck: Nur der aktuelle Bearbeiter bekommt einen persönlichen Fragment-Link;
    // die anonyme Einlösung liefert ausschließlich gebundenes Formular und erlaubte Werte.
    [Test]
    public async Task Link_ShouldRedeemOnceWithoutGrantingMutationAuthority()
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        var task = await StartDirectoryTask(context);
        using var actor = context.CreateClient();
        using var stranger = context.CreateClient(userId: Guid.NewGuid());
        using var frame = context.CreateAnonymousClient();
        (await stranger.PostAsJsonAsync($"/usertask/{task.Id}/form-link", new { hostOrigin = "https://host.test" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        var secret = await Issue(actor, task.Id);
        using var response = await Redeem(frame, secret);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var payload = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        payload.GetProperty("userTaskId").GetGuid().Should().Be(task.Id);
        payload.GetProperty("form").GetProperty("formData").GetString().Should().Contain("components");
        payload.TryGetProperty("token", out _).Should().BeFalse();
        (await Redeem(frame, secret)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        frame.DefaultRequestHeaders.Add("X-Flowzer-Form-Link", secret);
        (await frame.PutAsJsonAsync($"/usertask/{task.Id}/draft", new { expectedRevision = 0, data = new { } }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Testzweck: Die fünf Minuten betreffen nur die Einlösung. Ein schon geöffnetes Formular
    // bleibt nach 46 Minuten über einen erneuerten Benutzerrequest speicher- und abschließbar.
    [Test]
    public async Task RedemptionExpiry_ShouldNotExpireAnOpenedTask()
    {
        var clock = new FakeTimeProvider();
        using var context = new AuthenticatedWorkflowTestContext(Settings, clock);
        var task = await StartDirectoryTask(context);
        using var actor = context.CreateClient();
        using var frame = context.CreateAnonymousClient();
        var opened = await Issue(actor, task.Id);
        var unused = await Issue(actor, task.Id);
        (await Redeem(frame, opened)).StatusCode.Should().Be(HttpStatusCode.OK);
        clock.Advance(TimeSpan.FromMinutes(46));
        (await Redeem(frame, unused)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var renewedActor = context.CreateClient();
        (await renewedActor.PutAsJsonAsync($"/usertask/{task.Id}/draft", new { expectedRevision = 0, data = new { } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await renewedActor.PostAsJsonAsync("/usertask", new
        {
            processInstanceId = task.ProcessInstanceId, tokenId = task.Token.Id,
            flowNodeId = task.Token.CurrentFlowNode!.Id
        })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Zwei gleichzeitig einlösende Frames dürfen den privaten Snapshot nicht
    // beide erhalten; Origin-null-CORS ist ausschließlich am Read-only-Endpunkt erlaubt.
    [Test]
    public async Task ConcurrentRedemption_ShouldHaveOneWinnerAndIsolatedCors()
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        var task = await StartDirectoryTask(context);
        using var actor = context.CreateClient();
        using var frame = context.CreateAnonymousClient();
        frame.DefaultRequestHeaders.Add("Origin", "null");
        var secret = await Issue(actor, task.Id);
        var results = await Task.WhenAll(Redeem(frame, secret), Redeem(frame, secret));
        results.Should().ContainSingle(r => r.StatusCode == HttpStatusCode.OK);
        results.Should().ContainSingle(r => r.StatusCode == HttpStatusCode.NotFound);
        results[0].Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("null");
        results[0].Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
        using var other = await frame.GetAsync("/usertask");
        other.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        foreach (var result in results) result.Dispose();
    }

    // Testzweck: Der reale application/json-Preflight eines opaque Frames darf nur
    // POST/Content-Type ohne Credentials freigeben; fremde Origins erhalten kein CORS.
    [TestCase("null", true)]
    [TestCase("https://evil.test", false)]
    public async Task Redemption_ShouldHaveAnIsolatedJsonPreflight(string origin, bool allowed)
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        using var frame = context.CreateAnonymousClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/form-embed/redeem");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        using var response = await frame.SendAsync(request);
        response.Headers.Contains("Access-Control-Allow-Origin").Should().Be(allowed);
        response.Headers.Contains("Access-Control-Allow-Credentials").Should().BeFalse();
        if (allowed)
        {
            response.Headers.GetValues("Access-Control-Allow-Methods").Should().Equal("POST");
            response.Headers.GetValues("Access-Control-Allow-Headers").Single().ToLowerInvariant().Should().Be("content-type");
        }
    }

    // Testzweck: Auch bei deaktiviertem allgemeinen Limiter wird anonymer Linkverkehr
    // API-prozessweit begrenzt, ohne das Kontingent angemeldeter Aufgabenoperationen zu teilen.
    [Test]
    public async Task Redemption_ShouldBoundAnonymousTrafficWithoutBlockingAuthenticatedWork()
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        using var frame = context.CreateAnonymousClient();
        for (var i = 0; i < 60; i++)
            (await Redeem(frame, new string('A', 43))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Redeem(frame, new string('A', 43))).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        using var actor = context.CreateClient();
        (await actor.GetAsync("/usertask")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // Testzweck: Ein korrekt formatiertes, aber unbekanntes anonymes Secret wird vor
    // der globalen Schreibsperre verworfen und wartet nicht auf echte Engine-Arbeit.
    [Test]
    public async Task UnknownSecret_ShouldNotEnterEngineMutationLock()
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        using var frame = context.CreateAnonymousClient();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutation = context.Services.GetRequiredService<WebApiEngine.BusinessLogic.BpmnBusinessLogic>()
            .ExecuteUserTaskMutationAsync(async _ => { entered.SetResult(); await released.Task; return true; });
        await entered.Task;
        try
        {
            using var response = await Redeem(frame, new string('A', 43)).WaitAsync(TimeSpan.FromSeconds(5));
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally { released.SetResult(); await mutation; }
    }

    // Testzweck: Ein fremder Operator hat keinen persönlichen Formularzugriff. Ein
    // gültiges Operatorsubject ersetzt weder die echte Directory-Zuweisung noch den Bearbeiter.
    [Test]
    public async Task Issue_ShouldNotAllowOperatorBypass()
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        var task = await StartDirectoryTask(context);
        var operatorId = Guid.NewGuid();
        var users = (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!.Users.ToList();
        users.Add(new DirectoryUser { Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak,
            Issuer = AuthenticatedWorkflowTestContext.Issuer, Subject = operatorId.ToString(), DisplayName = "Operator", IsActive = true });
        await Publish(context, users);
        using var ops = context.CreateClient(userId: operatorId, isOperator: true);
        (await ops.PostAsJsonAsync($"/usertask/{task.Id}/form-link", new { hostOrigin = "https://host.test" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Eine nach Ausgabe veränderte Definition oder Lifecycle-Revision darf
    // den alten persönlichen Bootstrap nicht wieder auf eine andere Bindung anwenden.
    [TestCase(true)]
    [TestCase(false)]
    public async Task Redemption_ShouldRejectChangedBinding(bool definitionChanged)
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        var task = await StartDirectoryTask(context);
        using var actor = context.CreateClient();
        using var frame = context.CreateAnonymousClient();
        var secret = await Issue(actor, task.Id);
        if (definitionChanged)
        {
            task.DefinitionId = Guid.NewGuid();
            await context.Storage.SubscriptionStorage.AddUserTaskSubscription(task);
        }
        else
        {
            var change = UserTaskLifecycleStorageTest.Create(task, "claim", revision: 1);
            var owner = WebApiEngine.Auth.UserTaskDraftOwnerKey.Create(new WebApiEngine.Auth.CurrentUserContext(
                AuthenticatedWorkflowTestContext.UserId, "bert", false)
            { Identity = new AuthenticatedSubject(AuthenticatedWorkflowTestContext.Issuer, AuthenticatedWorkflowTestContext.UserId.ToString()) });
            var state = new UserTaskWorkState { UserTaskId = task.Id, Revision = 1,
                AssigneeOwnerKey = owner, DirectoryAssigneeUserId = (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!.Users.Single().Id,
                AssigneeDisplayName = "Bert", UpdatedAtUtc = DateTimeOffset.UtcNow };
            (await context.Storage.UserTaskLifecycleStorage.TryWrite(state, 0, change.Event)).Status
                .Should().Be(StorageSystem.UserTaskLifecycleWriteStatus.Written);
        }
        (await Redeem(frame, secret)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        if (!definitionChanged)
            (await Redeem(frame, await Issue(actor, task.Id))).StatusCode.Should().Be(HttpStatusCode.OK,
                "the same actor is still authorized, only the old lifecycle revision is stale");
    }

    // Testzweck: Rechteentzug oder Prozessabbruch nach Linkausgabe muss die Einlösung
    // blockieren; der Snapshot darf nicht zu einer zeitversetzten Rechteausnahme werden.
    [TestCase(true)]
    [TestCase(false)]
    public async Task Redemption_ShouldRecheckActorAndLifecycle(bool revokeActor)
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        var task = await StartDirectoryTask(context);
        using var actor = context.CreateClient();
        using var frame = context.CreateAnonymousClient();
        var secret = await Issue(actor, task.Id);
        if (revokeActor) await Publish(context, []);
        else
        {
            using var ops = context.CreateClient(isOperator: true);
            (await ops.PostAsJsonAsync($"/instance/{task.ProcessInstanceId}/cancel", new { }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }
        (await Redeem(frame, secret)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Ohne ausdrückliches Installations-Opt-in sowie bei fremder Host-Origin
    // werden keine Anzeige-Freigaben erstellt, auch nicht für Operatoren.
    [Test]
    public async Task Issue_ShouldRequireOptInAndAllowedHost()
    {
        using (var context = new AuthenticatedWorkflowTestContext())
        {
            var task = await StartDirectoryTask(context);
            using var actor = context.CreateClient();
            (await actor.PostAsJsonAsync($"/usertask/{task.Id}/form-link", new { hostOrigin = "https://host.test" }))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        using var enabled = new AuthenticatedWorkflowTestContext(Settings);
        var enabledTask = await StartDirectoryTask(enabled);
        using var user = enabled.CreateClient();
        (await user.PostAsJsonAsync($"/usertask/{enabledTask.Id}/form-link", new { hostOrigin = "https://evil.test" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Ein unvollständiger privater Entwurf wird beim Wiederöffnen wiederhergestellt,
    // ohne Pflichtfeldabschluss; unbekannte Prozessvariablen gehören nicht zum Anzeigenachweis.
    [Test]
    public async Task Reopening_ShouldRestoreOnlyTheActorsIncompleteDraft()
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        await FormTestSeed.StoreAsync(context.Storage, "Approval", """
            { "components": [{ "type": "textfield", "key": "answer", "validate": { "required": true } }] }
            """);
        var task = await StartDirectoryTask(context);
        using var actor = context.CreateClient();
        using var frame = context.CreateAnonymousClient();
        (await actor.PutAsJsonAsync($"/usertask/{task.Id}/draft", new { expectedRevision = 0, data = new { answer = "" } }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        using var response = await Redeem(frame, await Issue(actor, task.Id));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var snapshot = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result");
        snapshot.GetProperty("draft").GetProperty("revision").GetInt64().Should().Be(1);
        snapshot.GetProperty("draft").GetProperty("data").GetProperty("answer").GetString().Should().BeEmpty();
        (await actor.PostAsJsonAsync("/usertask", new { processInstanceId = task.ProcessInstanceId,
            tokenId = task.Token.Id, flowNodeId = task.Token.CurrentFlowNode!.Id, variables = new { answer = "" } }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // Testzweck: Ein Secret kann nicht als anderer API-Zugang dienen; ungültige Links und
    // Secrets als Queryparameter verraten keine Aufgabe und erhalten keinen Read-Snapshot.
    [Test]
    public async Task Redemption_ShouldRejectMalformedSecretsAndBusinessGet()
    {
        using var context = new AuthenticatedWorkflowTestContext(Settings);
        using var frame = context.CreateAnonymousClient();
        foreach (var value in new[] { "", "../escape", new string('a', 42), new string('a', 44) })
            (await Redeem(frame, value)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await frame.GetAsync("/form-embed/redeem?secret=unused" )).StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    private static async Task<string> Issue(HttpClient client, Guid taskId)
    {
        using var response = await client.PostAsJsonAsync($"/usertask/{taskId}/form-link", new { hostOrigin = "https://host.test" });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var link = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result").GetProperty("url").GetString()!;
        var uri = new Uri(link);
        uri.GetLeftPart(UriPartial.Path).Should().Be("https://flowzer.test/embed.html");
        uri.Query.Should().BeEmpty();
        return uri.Fragment[1..];
    }

    private static Task<HttpResponseMessage> Redeem(HttpClient client, string secret) =>
        client.PostAsJsonAsync("/form-embed/redeem", new { secret });

    private static async Task<UserTaskSubscription> StartDirectoryTask(AuthenticatedWorkflowTestContext context)
    {
        await Publish(context, [new DirectoryUser
        {
            Id = Guid.NewGuid(), SourceKind = DirectorySourceKind.Keycloak,
            Issuer = AuthenticatedWorkflowTestContext.Issuer,
            Subject = AuthenticatedWorkflowTestContext.UserId.ToString(), DisplayName = "Bert", IsActive = true
        }]);
        var userId = (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!.Users.Single().Id;
        return await context.StartAsync("", assignmentExtensionXml: $"<flowzer:taskAssignment mode=\"directory\" assigneeId=\"{userId}\" />");
    }

    private static async Task Publish(AuthenticatedWorkflowTestContext context, List<DirectoryUser> users)
    {
        var now = DateTime.UtcNow;
        var snapshot = new DirectorySnapshot { GenerationId = Guid.NewGuid(), Issuer = AuthenticatedWorkflowTestContext.Issuer,
            CompletedAtUtc = now, Users = users };
        (await context.Storage.IdentityDirectoryStorage.TryStartSync(snapshot.Issuer, snapshot.GenerationId, now.AddSeconds(-1), now.AddMinutes(5)))
            .Should().BeTrue();
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(snapshot);
    }
}
