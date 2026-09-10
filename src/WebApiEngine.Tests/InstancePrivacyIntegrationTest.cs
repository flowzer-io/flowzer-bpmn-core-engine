using System.Dynamic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Model;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Objektrechte gelten für Detail, Listen, Startantwort und sämtliche Nebenrouten.</summary>
[NonParallelizable]
public class InstancePrivacyIntegrationTest
{
    private static readonly Guid OtherUserId = Guid.Parse("4a09bd7a-e0d8-4e9c-9c3c-171471b91db4");

    // Testzweck: Auch alternative Subscription-Routen dürfen fremde Vorgänge weder
    // bestätigen noch deren Tokens, Korrelationen oder Variablen ausliefern.
    [TestCase("")]
    [TestCase("/subscription/messages")]
    [TestCase("/subscription/signals")]
    [TestCase("/subscription/timers")]
    [TestCase("/subscription/services")]
    [TestCase("/subscription/userTasks")]
    public async Task ForeignInstance_ShouldBeIndistinguishableFromMissingInstance(string suffix)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"anna\"");
        using var client = context.CreateClient();

        using var foreign = await client.GetAsync($"/instance/{task.ProcessInstanceId}{suffix}");
        using var missing = await client.GetAsync($"/instance/{Guid.NewGuid()}{suffix}");

        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await foreign.Content.ReadAsStringAsync()).Should().Be(await missing.Content.ReadAsStringAsync());
    }

    // Testzweck: Der neue Historienvertrag verwendet Problem Details, verrät aber
    // anhand von Status, Titel und Detail nicht, ob eine fremde Instanz existiert.
    [Test]
    public async Task ForeignHistory_ShouldBeIndistinguishableFromMissingHistory()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"anna\"");
        using var client = context.CreateClient();

        using var foreign = await client.GetAsync($"/instance/{task.ProcessInstanceId}/history");
        using var missing = await client.GetAsync($"/instance/{Guid.NewGuid()}/history");

        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        foreign.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var foreignProblem = await foreign.Content.ReadFromJsonAsync<JsonElement>();
        var missingProblem = await missing.Content.ReadFromJsonAsync<JsonElement>();
        foreignProblem.GetProperty("title").GetString().Should()
            .Be(missingProblem.GetProperty("title").GetString());
        foreignProblem.GetProperty("detail").GetString().Should()
            .Be(missingProblem.GetProperty("detail").GetString());
    }

    // Testzweck: Listen dürfen keine fremden Vorgänge und keine durch Modellierungsrechte
    // erschlichenen Personalvorgänge enthalten.
    [TestCase(false)]
    [TestCase(true)]
    public async Task InstanceList_ShouldExcludeForeignInstancesEvenForModelers(bool isModeler)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"anna\"");
        using var client = context.CreateClient(isModeler: isModeler);

        var response = await client.GetFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto[]>>("/instance");

        response!.Result.Should().BeEmpty();
        using var detail = await client.GetAsync($"/instance/{task.ProcessInstanceId}");
        detail.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Schon die Startantwort muss datensparsam sein; nachfolgende gefilterte
    // GET-Routen helfen nicht, wenn ein POST sofort alle internen Tokens zurückgibt.
    [Test]
    public async Task HttpStart_ShouldReturnOnlyAnOverviewAndKeepTheInitiatorAfterCompletion()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await context.DeployAsync("assignee=\"anna\"");
        using var owner = context.CreateClient();
        var instanceId = await StartWithAsync(owner);
        var task = (await context.Storage.SubscriptionStorage.GetAllUserTasks(instanceId)).Single();
        using var worker = context.CreateClient(isOperator: true);
        using var completion = await worker.PostAsJsonAsync("/usertask", ResultFor(task));
        completion.EnsureSuccessStatusCode();

        using var after = await owner.GetAsync($"/instance/{instanceId}");
        after.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await after.Content.ReadFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>();
        dto!.Result!.State.Should().Be(ProcessInstanceStateDto.Completed);
        dto.Result.Tokens.Should().BeEmpty();
        var list = await owner.GetFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto[]>>("/instance");
        list!.Result.Should().ContainSingle(item => item.InstanceId == instanceId);
        list.Result![0].Tokens.Should().BeEmpty();
    }

    // Testzweck: Dieselbe Subject-ID in einem anderen gültigen Issuer sowie gleiche Namen
    // sind kein Besitznachweis; eine Umbenennung des tatsächlichen Initiators bleibt erlaubt.
    [Test]
    public async Task InitiatorAccess_ShouldUseIssuerAndSubjectNotNameOrSubmittedIdentity()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await context.DeployAsync("assignee=\"nobody\"");
        using var owner = context.CreateClient();
        var instanceId = await StartWithAsync(owner);
        using var renamed = context.CreateClient(username: "renamed");
        using var sameName = context.CreateClient(userId: OtherUserId);
        using var otherIssuer = context.CreateClient(issuer: AuthenticatedWorkflowTestContext.Issuer + "-second");

        using var allowed = await renamed.GetAsync($"/instance/{instanceId}");
        using var deniedName = await sameName.GetAsync($"/instance/{instanceId}");
        using var deniedIssuer = await otherIssuer.GetAsync($"/instance/{instanceId}");

        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        deniedName.StatusCode.Should().Be(HttpStatusCode.NotFound);
        deniedIssuer.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Auch Antragsteller dürfen technische Warteobjekte nicht über Nebenrouten
    // lesen; ein sichtbarer Status ist kein Recht auf Korrelationen oder Prozessvariablen.
    [TestCase("messages")]
    [TestCase("signals")]
    [TestCase("timers")]
    [TestCase("services")]
    [TestCase("userTasks")]
    public async Task Initiator_ShouldNotReceiveTechnicalSubscriptions(string kind)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await context.DeployAsync("assignee=\"anna\"");
        using var owner = context.CreateClient();
        var instanceId = await StartWithAsync(owner);

        using var response = await owner.GetAsync($"/instance/{instanceId}/subscription/{kind}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Aktuelle Aufgabenrechte gewähren nur eine Übersicht und verschwinden nach
    // Abschluss; eine frühere Bearbeitung ist noch kein dauerhaftes Historienrecht.
    [Test]
    public async Task TaskReader_ShouldReceiveOnlyOverviewAndLoseAccessAfterCompletion()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"bert\"");
        using var client = context.CreateClient();

        var overview = await client.GetFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>($"/instance/{task.ProcessInstanceId}");
        overview!.Result!.Tokens.Should().BeEmpty();
        using var completion = await client.PostAsJsonAsync("/form/result", ResultFor(task));
        completion.EnsureSuccessStatusCode();
        using var after = await client.GetAsync($"/instance/{task.ProcessInstanceId}");
        after.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var history = await client.GetAsync($"/instance/{task.ProcessInstanceId}/history");
        history.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Die erste append-only Vorgangshistorie liefert nur stabile,
    // datensparsame Task-Lifecycle-Fakten und keinerlei interne Personen- oder Formulardaten.
    [Test]
    public async Task History_ShouldReturnMinimalLifecycleEventsAfterTaskCompletion()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("candidateGroups=\"review\"");
        using var worker = context.CreateClient(username: "bert");
        using var operation = context.CreateClient(isOperator: true, username: "operator");
        (await worker.PostAsJsonAsync($"/usertask/{task.Id}/claim", new { expectedRevision = 0 }))
            .EnsureSuccessStatusCode();
        (await worker.PostAsJsonAsync($"/usertask/{task.Id}/release", new
        {
            expectedRevision = 1,
            reason = "Vertraulicher interner Grund"
        })).EnsureSuccessStatusCode();
        (await worker.PostAsJsonAsync($"/usertask/{task.Id}/claim", new { expectedRevision = 2 }))
            .EnsureSuccessStatusCode();
        var completion = ResultFor(task);
        completion.ExpectedTaskRevision = 3;
        (await worker.PostAsJsonAsync("/usertask", completion)).EnsureSuccessStatusCode();

        using var response = await operation.GetAsync($"/instance/{task.ProcessInstanceId}/history");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(json).GetProperty("result");
        payload.GetProperty("instanceId").GetGuid().Should().Be(task.ProcessInstanceId!.Value);
        var events = payload.GetProperty("events").EnumerateArray().ToArray();
        events.Select(item => item.GetProperty("action").GetString())
            .Should().Equal("claim", "release", "claim", "complete");
        events.Should().OnlyContain(item =>
            item.GetProperty("userTaskId").GetGuid() == task.Id
            && item.GetProperty("flowNodeId").GetString() == "Review");
        json.Should().NotContain("Vertraulicher interner Grund")
            .And.NotContain("actorUserId")
            .And.NotContain("ownerKey")
            .And.NotContain("displayName")
            .And.NotContain("correlationId")
            .And.NotContain("variables")
            .And.NotContain("formData");
    }

    // Testzweck: Ein verifizierter Initiator darf die minimale fachliche Historie
    // seines abgeschlossenen Vorgangs lesen, ohne dadurch technische Tokens zu erhalten.
    [Test]
    public async Task Initiator_ShouldKeepMinimalHistoryAfterCompletion()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await context.DeployAsync("assignee=\"anna\"");
        using var owner = context.CreateClient();
        var instanceId = await StartWithAsync(owner);
        var task = (await context.Storage.SubscriptionStorage.GetAllUserTasks(instanceId)).Single();
        using var operation = context.CreateClient(isOperator: true, username: "operator");
        (await operation.PostAsJsonAsync("/usertask", ResultFor(task))).EnsureSuccessStatusCode();

        var history = await owner.GetFromJsonAsync<ApiStatusResult<ProcessHistoryDto>>(
            $"/instance/{instanceId}/history");

        history!.Result!.InstanceId.Should().Be(instanceId);
        history.Result.Events.Should().ContainSingle().Which.Action.Should().Be("complete");
    }

    // Testzweck: Technische und historische Instanzen ohne verifizierten Initiator dürfen
    // nicht über ein altes Formularfeld UserId nachträglich in Besitz genommen werden.
    [Test]
    public async Task LegacyInstance_ShouldNotTreatFormUserIdAsOwnership()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        ExpandoObject variables = new();
        ((IDictionary<string, object?>)variables)["UserId"] = AuthenticatedWorkflowTestContext.UserId;
        var task = await context.StartAsync("assignee=\"anna\"", variables);
        using var client = context.CreateClient();

        using var response = await client.GetAsync($"/instance/{task.ProcessInstanceId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Der Betrieb behält seine Diagnose einschließlich Tokens und technischer
    // Endpunkte; derselbe Vertrag kennzeichnet diese ausdrücklich als canInspect.
    [Test]
    public async Task Operator_ShouldKeepDiagnostics()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"anna\"");
        using var client = context.CreateClient(isOperator: true);

        var payload = await client.GetFromJsonAsync<JsonElement>($"/instance/{task.ProcessInstanceId}");
        payload.GetProperty("result").GetProperty("tokens").GetArrayLength().Should().BeGreaterThan(0);
        payload.GetProperty("result").GetProperty("canInspect").GetBoolean().Should().BeTrue();
        using var subscriptions = await client.GetAsync($"/instance/{task.ProcessInstanceId}/subscription/userTasks");
        subscriptions.EnsureSuccessStatusCode();
    }

    // Testzweck: Aufgaben-DTOs dürfen nicht die datensparsame Instanzansicht umgehen. Nur
    // explizite skalare Formularfelder verlassen den Server, keine Ergebnis-/Modellgraphen.
    [Test]
    public async Task TaskList_ShouldProjectOnlyDeclaredFormContext()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var formId = Guid.NewGuid();
        await context.Storage.FormStorage.SaveFormMetaData(new FormMetadata { FormId = formId, Name = "Approval" });
        await context.Storage.FormStorage.SaveForm(new Form
        {
            Id = Guid.NewGuid(), FormId = formId, Version = new Model.Version(1, 0),
            FormData = """{"components":[{"type":"textarea","input":true,"disabled":true,"key":"vorgang"}]}"""
        });
        var task = await context.StartAsync("assignee=\"bert\"");
        task.Token.Variables = new ExpandoObject();
        var data = (IDictionary<string, object?>)task.Token.Variables;
        data["vorgang"] = "Öffentlich freigegebener Kontext";
        data["privateSalary"] = "SHOULD_NOT_LEAK";
        task.Token.OutputData = task.Token.Variables;
        await context.Storage.SubscriptionStorage.AddUserTaskSubscription(task);
        var stored = await context.Storage.InstanceStorage.GetProcessInstance(task.ProcessInstanceId!.Value);
        stored.Tokens.Single(token => token.Id == task.Token.Id).Variables = task.Token.Variables;
        await context.Storage.InstanceStorage.AddOrUpdateInstance(stored);
        using var client = context.CreateClient();

        var response = await client.GetFromJsonAsync<JsonElement>("/usertask");
        var token = response.GetProperty("result")[0].GetProperty("token");

        token.GetProperty("variables").GetProperty("vorgang").GetString().Should().Be("Öffentlich freigegebener Kontext");
        response.GetRawText().Should().NotContain("SHOULD_NOT_LEAK");
        token.TryGetProperty("currentFlowElement", out _).Should().BeFalse();
        token.TryGetProperty("outputData", out _).Should().BeFalse();
    }

    // Testzweck: Die Besitzzuordnung bleibt auch beim terminalen Abbruch erhalten,
    // obwohl die dabei entfernten Aufgaben keinen alternativen Zugriff mehr gewähren.
    [Test]
    public async Task Initiator_ShouldKeepOverviewAfterCancellation()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await context.DeployAsync("assignee=\"anna\"");
        using var owner = context.CreateClient();
        var instanceId = await StartWithAsync(owner);
        using var operatorClient = context.CreateClient(isOperator: true);
        using var cancellation = await operatorClient.PostAsync($"/instance/{instanceId}/cancel", null);
        cancellation.EnsureSuccessStatusCode();

        var result = await owner.GetFromJsonAsync<ApiStatusResult<ProcessInstanceInfoDto>>($"/instance/{instanceId}");
        result!.Result!.State.Should().Be(ProcessInstanceStateDto.Terminated);
        result.Result.Tokens.Should().BeEmpty();
    }

    // Testzweck: Eine veraltete Subscription darf keine Sichtbarkeit auf eine andere
    // Definitions-/Instanzbindung verschaffen, auch wenn der Name der Person passt.
    [Test]
    public async Task StaleTask_ShouldNotGrantOverview()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var task = await context.StartAsync("assignee=\"bert\"");
        task.DefinitionId = Guid.NewGuid();
        await context.Storage.SubscriptionStorage.AddUserTaskSubscription(task);
        using var client = context.CreateClient();

        using var result = await client.GetAsync($"/instance/{task.ProcessInstanceId}");

        result.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Guid.Empty ist kein identifizierter Akteur, auch nicht mit Operatorrolle.
    [Test]
    public async Task EmptyIdentity_ShouldNotReadInstanceData()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        using var client = context.CreateClient(isOperator: true, userId: Guid.Empty);

        using var result = await client.GetAsync("/instance");

        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<Guid> StartWithAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync("/definition/meta/Definitions_Completion/instance", new
        {
            variables = new { privateSalary = "SHOULD_NOT_LEAK", UserId = OtherUserId },
            initiator = new { issuer = AuthenticatedWorkflowTestContext.Issuer, subject = OtherUserId.ToString() }
        });
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("result").GetProperty("tokens").GetArrayLength().Should().Be(0);
        payload.GetProperty("result").GetProperty("canInspect").GetBoolean().Should().BeFalse();
        payload.GetRawText().Should().NotContain("SHOULD_NOT_LEAK");
        return payload.GetProperty("result").GetProperty("instanceId").GetGuid();
    }

    private static UserTaskResultDto ResultFor(UserTaskSubscription task) => new()
    {
        ProcessInstanceId = task.ProcessInstanceId, TokenId = task.Token.Id, FlowNodeId = "Review"
    };
}
