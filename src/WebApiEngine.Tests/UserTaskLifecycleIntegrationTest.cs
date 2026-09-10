using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Model;
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Öffentlicher Vertrag für Übernahme, Freigabe, Zuweisung und Delegation.</summary>
[NonParallelizable]
public sealed class UserTaskLifecycleIntegrationTest
{
    // Testzweck: Zwei Kandidaten dürfen eine freie Aufgabe sehen; nach der Übernahme darf
    // ausschließlich der tatsächliche Bearbeiter Formular, Entwurf und Abschluss nutzen.
    [Test]
    public async Task Claim_ShouldMakeTheActualWorkerExclusiveAcrossAllTaskRoutes()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("candidateGroups=\"review\"");
        using var bert = context.CreateClient(username: "bert");
        using var anna = context.CreateClient(userId: Guid.NewGuid(), username: "anna");

        (await ListTasksAsync(anna)).Should().Contain(task.Id);
        using var claimed = await bert.PostAsJsonAsync($"/usertask/{task.Id}/claim", new
        {
            expectedRevision = 0,
            actorUserId = Guid.NewGuid()
        });

        claimed.StatusCode.Should().Be(HttpStatusCode.OK);
        var state = Result(await claimed.Content.ReadFromJsonAsync<JsonElement>());
        state.GetProperty("revision").GetInt64().Should().Be(1);
        state.GetProperty("isAssignedToCurrentUser").GetBoolean().Should().BeTrue();

        using var staleDraft = await bert.PutAsJsonAsync($"/usertask/{task.Id}/draft", new
        {
            expectedRevision = 0,
            expectedTaskRevision = 0,
            data = new { }
        });
        staleDraft.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var staleCompletion = Completion(task);
        staleCompletion.ExpectedTaskRevision = 0;
        (await bert.PostAsJsonAsync("/usertask", staleCompletion)).StatusCode
            .Should().Be(HttpStatusCode.Conflict);

        (await ListTasksAsync(anna)).Should().NotContain(task.Id);
        (await anna.GetAsync($"/usertask/{task.Id}/form")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anna.GetAsync($"/usertask/{task.Id}/draft")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anna.GetAsync($"/instance/{task.ProcessInstanceId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anna.PostAsJsonAsync("/usertask", Completion(task))).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
        (await bert.GetAsync($"/usertask/{task.Id}/form")).StatusCode.Should().Be(HttpStatusCode.OK);
        var audit = await context.Storage.UserTaskLifecycleStorage.GetEvents(task.Id);
        audit.Should().ContainSingle().Which.ActorUserId.Should().Be(AuthenticatedWorkflowTestContext.UserId);
    }

    // Testzweck: Negative Lebenszyklusrevisionen bleiben die bisherige fachliche
    // Validierungsantwort und werden nicht als generischer 400-Fehler abgeflacht.
    [Test]
    public async Task Claim_ShouldRejectNegativeRevisionWithValidationProblem()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("");
        using var client = context.CreateClient(username: "bert");

        using var response = await client.PostAsJsonAsync($"/usertask/{task.Id}/claim", new
        {
            expectedRevision = -1
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").GetProperty("expectedRevision")
            .EnumerateArray().Should().ContainSingle().Which.GetString().Should().Be("revision.invalid");
        (await context.Storage.UserTaskLifecycleStorage.Get(task.Id)).Should().BeNull();
    }

    // Testzweck: Zwei gleichzeitige Claims mit derselben Revision haben genau einen Gewinner;
    // der Verlierer erhält einen strukturierten Konflikt und verändert den Stand nicht.
    [Test]
    public async Task ConcurrentClaims_ShouldHaveExactlyOneWinner()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("");
        using var bert = context.CreateClient(username: "bert");
        using var anna = context.CreateClient(userId: Guid.NewGuid(), username: "anna");

        var responses = await Task.WhenAll(
            bert.PostAsJsonAsync($"/usertask/{task.Id}/claim", new { expectedRevision = 0 }),
            anna.PostAsJsonAsync($"/usertask/{task.Id}/claim", new { expectedRevision = 0 }));

        responses.Should().ContainSingle(response => response.StatusCode == HttpStatusCode.OK);
        responses.Should().ContainSingle(response => response.StatusCode == HttpStatusCode.Conflict);
        var conflict = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
        var problem = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("user_task.revision_conflict");
        problem.GetProperty("currentRevision").GetInt64().Should().Be(1);
        responses.ToList().ForEach(response => response.Dispose());
    }

    // Testzweck: Freigabe erhöht die Revision statt sie zurückzusetzen; ein anderer Kandidat
    // kann anschließend nur mit genau diesem aktuellen Stand übernehmen.
    [Test]
    public async Task Release_ShouldPreserveMonotonicRevisionAndReopenForCandidates()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("candidateGroups=\"review\"");
        using var bert = context.CreateClient(username: "bert");
        using var anna = context.CreateClient(userId: Guid.NewGuid(), username: "anna");
        (await bert.PostAsJsonAsync($"/usertask/{task.Id}/claim", new { expectedRevision = 0 }))
            .EnsureSuccessStatusCode();

        using var released = await bert.PostAsJsonAsync($"/usertask/{task.Id}/release", new
        {
            expectedRevision = 1,
            reason = "Übergabe an das Team"
        });
        released.StatusCode.Should().Be(HttpStatusCode.OK);
        Result(await released.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revision").GetInt64().Should().Be(2);
        (await ListTasksAsync(anna)).Should().Contain(task.Id);

        using var stale = await anna.PostAsJsonAsync(
            $"/usertask/{task.Id}/claim", new { expectedRevision = 0 });
        using var current = await anna.PostAsJsonAsync(
            $"/usertask/{task.Id}/claim", new { expectedRevision = 2 });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        current.StatusCode.Should().Be(HttpStatusCode.OK);
        Result(await current.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("revision").GetInt64().Should().Be(3);
    }

    // Testzweck: Der tatsächliche Bearbeiter darf eine Directory-Kandidatenaufgabe nur an
    // einen serverseitig belegten aktiven Kandidaten delegieren; danach wechselt der Zugriff.
    [Test]
    public async Task Delegate_ShouldTransferWorkToAnActiveDirectoryCandidate()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var annaSubject = Guid.NewGuid();
        var directory = await PublishDirectoryAsync(context, annaSubject, annaInCandidateGroup: true);
        var assignment = $"<flowzer:taskAssignment mode=\"directory\" candidateGroupIds=\"{directory.GroupId}\" />";
        var task = await context.StartAsync("", assignmentExtensionXml: assignment);
        using var bert = context.CreateClient(username: "bert");
        using var anna = context.CreateClient(userId: annaSubject, username: "anna");
        (await bert.PostAsJsonAsync($"/usertask/{task.Id}/claim", new { expectedRevision = 0 }))
            .EnsureSuccessStatusCode();

        using var candidates = await bert.GetAsync(
            $"/identity-directory/user-tasks/{task.Id}/assignees?action=delegate&query=Anna");
        candidates.StatusCode.Should().Be(HttpStatusCode.OK);
        Result(await candidates.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")
            .EnumerateArray().Should().ContainSingle(item =>
                item.GetProperty("subject").GetProperty("id").GetGuid() == directory.AnnaId);

        using var delegated = await bert.PostAsJsonAsync($"/usertask/{task.Id}/delegate", new
        {
            expectedRevision = 1,
            assignee = new { kind = "user", id = directory.AnnaId },
            reason = "Anna übernimmt die Vertretung"
        });

        delegated.StatusCode.Should().Be(HttpStatusCode.OK);
        var state = Result(await delegated.Content.ReadFromJsonAsync<JsonElement>());
        state.GetProperty("revision").GetInt64().Should().Be(2);
        state.GetProperty("actualAssignee").GetProperty("id").GetGuid().Should().Be(directory.AnnaId);
        (await ListTasksAsync(bert)).Should().NotContain(task.Id);
        (await ListTasksAsync(anna)).Should().Contain(task.Id);
        (await bert.GetAsync($"/usertask/{task.Id}/draft")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anna.PostAsJsonAsync("/usertask", Completion(task))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await context.Storage.UserTaskLifecycleStorage.Get(task.Id)).Should().BeNull();
        (await context.Storage.UserTaskLifecycleStorage.GetEvents(task.Id))
            .Select(item => item.Action).Should().Equal("claim", "delegate", "complete");
    }

    // Testzweck: Nur der Operator darf außerhalb der modellierten Kandidaten zuweisen; ein
    // Gruppenwert, inaktiver Benutzer oder fehlender Delegationsgrund verändert nichts.
    [Test]
    public async Task Reassignment_ShouldValidateCapabilityTargetAndReason()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var annaSubject = Guid.NewGuid();
        var directory = await PublishDirectoryAsync(context, annaSubject, annaInCandidateGroup: false);
        var assignment = $"<flowzer:taskAssignment mode=\"directory\" candidateUserIds=\"{directory.BertId}\" />";
        var task = await context.StartAsync("", assignmentExtensionXml: assignment);
        using var bert = context.CreateClient(username: "bert");
        using var anna = context.CreateClient(userId: annaSubject, username: "anna");
        using var operation = context.CreateClient(isOperator: true, username: "operator");
        (await bert.PostAsJsonAsync($"/usertask/{task.Id}/claim", new { expectedRevision = 0 }))
            .EnsureSuccessStatusCode();

        using var forbiddenCandidate = await bert.GetAsync(
            $"/identity-directory/user-tasks/{task.Id}/assignees?action=delegate&query=Anna");
        Result(await forbiddenCandidate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")
            .GetArrayLength().Should().Be(0);
        using var operatorCandidate = await operation.GetAsync(
            $"/identity-directory/user-tasks/{task.Id}/assignees?action=assign&query=Anna");
        Result(await operatorCandidate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items")
            .GetArrayLength().Should().Be(1);

        using var notCandidate = await bert.PostAsJsonAsync($"/usertask/{task.Id}/delegate", new
        {
            expectedRevision = 1,
            assignee = new { kind = "user", id = directory.AnnaId },
            reason = "Vertretung"
        });
        using var groupTarget = await operation.PostAsJsonAsync($"/usertask/{task.Id}/assign", new
        {
            expectedRevision = 1,
            assignee = new { kind = "group", id = directory.GroupId },
            reason = "Falscher Typ"
        });
        using var missingReason = await operation.PostAsJsonAsync($"/usertask/{task.Id}/assign", new
        {
            expectedRevision = 1,
            assignee = new { kind = "user", id = directory.AnnaId },
            reason = ""
        });
        using var assigned = await operation.PostAsJsonAsync($"/usertask/{task.Id}/assign", new
        {
            expectedRevision = 1,
            assignee = new { kind = "user", id = directory.AnnaId },
            reason = "Betriebliche Neuzuweisung"
        });

        notCandidate.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        groupTarget.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        missingReason.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        assigned.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ListTasksAsync(bert)).Should().NotContain(task.Id);
        (await ListTasksAsync(anna)).Should().Contain(task.Id);
    }

    // Testzweck: Die Lifecycle-Auflösung beschriftet eine inzwischen deaktivierte
    // stabile Benutzerreferenz weiterhin, macht sie aber weder für Delegation noch
    // Zuweisung erneut auswählbar und verbirgt den Task vor fremden Benutzern.
    [Test]
    public async Task AssigneeResolution_ShouldKeepInactiveReferenceDisplayOnlyInAuthorizedActionContext()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var annaSubject = Guid.NewGuid();
        var directory = await PublishDirectoryAsync(
            context, annaSubject, annaInCandidateGroup: true);
        var assignment = $"<flowzer:taskAssignment mode=\"directory\" candidateUserIds=\"{directory.BertId}\" />";
        var task = await context.StartAsync("", assignmentExtensionXml: assignment);
        using var operation = context.CreateClient(isOperator: true, username: "operation");
        using var foreign = context.CreateClient(userId: Guid.NewGuid(), username: "foreign");
        (await operation.PostAsJsonAsync($"/usertask/{task.Id}/assign", new
        {
            expectedRevision = 0,
            assignee = new { kind = "user", id = directory.AnnaId },
            reason = "Vertretung"
        }))
            .EnsureSuccessStatusCode();
        await PublishDirectoryAsync(
            context, annaSubject, annaInCandidateGroup: true, annaActive: false);
        var body = new { subjects = new[] { new { kind = "user", id = directory.AnnaId } } };

        using var resolved = await operation.PostAsJsonAsync(
            $"/identity-directory/user-tasks/{task.Id}/assignees/resolve?action=assign", body);
        using var hidden = await foreign.PostAsJsonAsync(
            $"/identity-directory/user-tasks/{task.Id}/assignees/resolve?action=assign", body);

        resolved.StatusCode.Should().Be(HttpStatusCode.OK);
        hidden.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var item = Result(await resolved.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().Single();
        item.GetProperty("subject").GetProperty("id").GetGuid().Should().Be(directory.AnnaId);
        item.GetProperty("displayName").GetString().Should().Be("Anna");
        item.GetProperty("isActive").GetBoolean().Should().BeFalse();
        item.GetProperty("isSelectable").GetBoolean().Should().BeFalse();
    }

    // Testzweck: Ein weiterhin gültiges JWT darf nach Directory-Deaktivierung keine
    // bereits beanspruchte oder administrativ zugewiesene Aufgabe mehr öffnen/abschließen.
    // Das gilt auch bei Textmodellen mit nachträglicher Directory-Zuweisung.
    [TestCase(false, "/usertask")]
    [TestCase(false, "/form/result")]
    [TestCase(true, "/usertask")]
    [TestCase(true, "/form/result")]
    public async Task InactiveActualAssignee_ShouldLoseAllTaskAccess(bool textModel, string completionRoute)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var annaSubject = Guid.NewGuid();
        var directory = await PublishDirectoryAsync(context, annaSubject, annaInCandidateGroup: true);
        var assignment = textModel ? null
            : $"<flowzer:taskAssignment mode=\"directory\" candidateGroupIds=\"{directory.GroupId}\" />";
        var task = await context.StartAsync("", assignmentExtensionXml: assignment);
        using var anna = context.CreateClient(userId: annaSubject, username: "anna");
        using var operation = context.CreateClient(isOperator: true);
        if (textModel)
            (await operation.PostAsJsonAsync($"/usertask/{task.Id}/assign", new
            {
                expectedRevision = 0, assignee = new { kind = "user", id = directory.AnnaId },
                reason = "Betriebliche Zuweisung"
            })).EnsureSuccessStatusCode();
        else
            (await anna.PostAsJsonAsync($"/usertask/{task.Id}/claim", new { expectedRevision = 0 }))
                .EnsureSuccessStatusCode();

        (await anna.GetAsync($"/instance/{task.ProcessInstanceId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        await PublishDirectoryAsync(context, annaSubject, annaInCandidateGroup: true, annaActive: false);

        using (new FluentAssertions.Execution.AssertionScope())
        {
            (await ListTasksAsync(anna)).Should().NotContain(task.Id);
            foreach (var route in new[] { $"/usertask/{task.Id}", $"/usertask/{task.Id}/form",
                         $"/usertask/{task.Id}/draft", $"/instance/{task.ProcessInstanceId}" })
                (await anna.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.NotFound, route);
            var instances = Result(await anna.GetFromJsonAsync<JsonElement>("/instance"));
            instances.EnumerateArray().Should().NotContain(item =>
                item.GetProperty("instanceId").GetGuid() == task.ProcessInstanceId);
            (await anna.PostAsJsonAsync(completionRoute, Completion(task))).StatusCode
                .Should().Be(HttpStatusCode.NotFound, completionRoute);
            (await anna.PostAsJsonAsync($"/usertask/{task.Id}/claim", new
                { expectedRevision = 1 })).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await anna.PostAsJsonAsync($"/usertask/{task.Id}/release", new
                { expectedRevision = 1, reason = "Freigabe" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        await context.AssertStillActiveAsync(task);
        // Der Betrieb muss die verwaiste Aufgabe weiterhin korrigieren können.
        (await operation.PostAsJsonAsync($"/usertask/{task.Id}/release", new
            { expectedRevision = 1, reason = "Deaktiviertes Konto" })).EnsureSuccessStatusCode();
    }

    private static async Task<Guid[]> ListTasksAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/usertask");
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Result(payload).EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();
    }

    private static UserTaskResultDto Completion(UserTaskSubscription task) => new()
    {
        ProcessInstanceId = task.ProcessInstanceId,
        TokenId = task.Token.Id,
        FlowNodeId = task.Token.CurrentFlowNode!.Id
    };

    private static JsonElement Result(JsonElement payload) => payload.GetProperty("result");

    private static async Task<(Guid BertId, Guid AnnaId, Guid GroupId)> PublishDirectoryAsync(
        AuthenticatedWorkflowTestContext context,
        Guid annaSubject,
        bool annaInCandidateGroup,
        bool annaActive = true)
    {
        var importedBert = Guid.NewGuid();
        var importedAnna = Guid.NewGuid();
        var importedGroup = Guid.NewGuid();
        var completedAt = DateTime.UtcNow;
        var snapshot = new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(),
            Issuer = AuthenticatedWorkflowTestContext.Issuer,
            CompletedAtUtc = completedAt,
            Users =
            [
                User(importedBert, AuthenticatedWorkflowTestContext.UserId, "Bert"),
                User(importedAnna, annaSubject, "Anna", annaActive)
            ],
            Groups =
            [
                new DirectoryGroup
                {
                    Id = importedGroup,
                    SourceKind = DirectorySourceKind.Keycloak,
                    Issuer = AuthenticatedWorkflowTestContext.Issuer,
                    ExternalId = "review",
                    Name = "Review",
                    Path = "/team/review",
                    IsActive = true
                }
            ],
            Memberships =
            [
                new DirectoryMembership { UserId = importedBert, GroupId = importedGroup },
                .. annaInCandidateGroup
                    ? new[] { new DirectoryMembership { UserId = importedAnna, GroupId = importedGroup } }
                    : Array.Empty<DirectoryMembership>()
            ]
        };
        var started = completedAt.AddSeconds(-1);
        (await context.Storage.IdentityDirectoryStorage.TryStartSync(
            snapshot.Issuer, snapshot.GenerationId, started, started.AddMinutes(5))).Should().BeTrue();
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(snapshot);
        var published = (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!;
        return (
            published.Users.Single(user => user.Subject == AuthenticatedWorkflowTestContext.UserId.ToString()).Id,
            published.Users.Single(user => user.Subject == annaSubject.ToString()).Id,
            published.Groups.Single(group => group.ExternalId == "review").Id);
    }

    private static DirectoryUser User(Guid id, Guid subject, string displayName, bool active = true) => new()
    {
        Id = id,
        SourceKind = DirectorySourceKind.Keycloak,
        Issuer = AuthenticatedWorkflowTestContext.Issuer,
        Subject = subject.ToString(),
        DisplayName = displayName,
        IsActive = active
    };
}
