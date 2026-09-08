using System.Dynamic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Model;
using Newtonsoft.Json;
using StorageSystem;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>Browserunabhängige Eingabegrenzen über echte JWT-/API-/Engine-Pfade.</summary>
[NonParallelizable]
public class FormSubmissionSecurityTest
{
    private const string Schema = """
        {"flowzer":{"contractVersion":1,"rules":[{"kind":"dateOrder","start":"from","end":"to","allowEqual":true}]},
         "components":[
          {"type":"textfield","key":"reason","input":true,"validate":{"required":true,"minLength":3,"maxLength":100}},
          {"type":"checkbox","key":"needDetails","input":true},
          {"type":"textarea","key":"details","input":true,"validate":{"required":true},"conditional":{"when":"needDetails","eq":"true","show":true}},
          {"type":"textfield","key":"employeeId","input":true,"disabled":true},
          {"type":"number","key":"days","input":true,"validate":{"min":1,"max":60}},
          {"type":"select","key":"kind","input":true,"dataSrc":"values","data":{"values":[{"value":"annual"},{"value":"special"}]}},
          {"type":"datetime","key":"from","input":true},
          {"type":"datetime","key":"to","input":true}]}
        """;

    private static IEnumerable<TestCaseData> InvalidCompletions()
    {
        (string Json, string Field)[] cases =
        [
            ("{}", "reason"), ("{\"reason\":\"ok\"}", "reason"),
            ("{\"reason\":\"Holiday\",\"privateSalary\":90000}", "privateSalary"),
            ("{\"reason\":\"Holiday\",\"employeeId\":\"another\"}", "employeeId"),
            ("{\"reason\":\"Holiday\",\"needDetails\":true}", "details"),
            ("{\"reason\":\"Holiday\",\"needDetails\":\"true\"}", "needDetails"),
            ("{\"reason\":{\"nested\":\"not-text\"}}", "reason"),
            ("{\"reason\":\"Holiday\",\"kind\":\"not-allowed\"}", "kind"),
            ("{\"reason\":\"Holiday\",\"days\":0}", "days"),
            ("{\"reason\":\"Holiday\",\"from\":\"2026-09-20\",\"to\":\"2026-09-10\"}", "to")
        ];
        foreach (var route in new[] { "/usertask", "/form/result" })
            foreach (var (json, field) in cases) yield return new TestCaseData(route, json, field);
    }

    // Testzweck: Beide Abschlusswege prüfen gebundene Pflicht-/Typ-/Auswahl-/Kontextregeln
    // auf dem Server; Ablehnung verändert weder Instanz noch Aufgabenidentität.
    [TestCaseSource(nameof(InvalidCompletions))]
    public async Task Completion_ShouldRejectInvalidInputWithoutMutation(string route, string data, string field)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval", Schema);
        var task = await context.StartAsync("assignee=\"bert\"", Data("{\"employeeId\":\"known\"}"));
        using var client = context.CreateClient();
        using var response = await client.PostAsJsonAsync(route, Result(task, data));
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
        problem.GetProperty("successful").GetBoolean().Should().BeFalse();
        await context.AssertStillActiveAsync(task);
    }

    // Testzweck: Ein gültiger Abschluss ohne bedingt ausgeblendetes Pflichtfeld bleibt
    // möglich; unveränderter Read-only-Kontext fließt nicht als Ergebnis zurück.
    [TestCase("/usertask", false)]
    [TestCase("/form/result", false)]
    [TestCase("/usertask", true)]
    [TestCase("/form/result", true)]
    public async Task Completion_ShouldAcceptValidInputButExcludeContextFromResults(string route, bool isOperator)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval", Schema);
        var task = await context.StartAsync("assignee=\"bert\"", Data("{\"employeeId\":\"known\",\"privateSalary\":90000}"));
        using var client = context.CreateClient(isOperator: isOperator);
        var tasks = await client.GetFromJsonAsync<JsonElement>("/usertask");
        var initial = tasks.GetProperty("result")[0].GetProperty("token").GetProperty("variables");
        initial.GetProperty("employeeId").GetString().Should().Be("known");
        initial.TryGetProperty("privateSalary", out _).Should().BeFalse();
        using var response = await client.PostAsJsonAsync(route, Result(task,
            "{\"reason\":\"Holiday\",\"needDetails\":false,\"employeeId\":\"known\",\"kind\":\"annual\",\"days\":3}"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var instance = await context.Storage.InstanceStorage.GetProcessInstance(task.ProcessInstanceId!.Value);
        var output = (IDictionary<string, object?>)instance.Tokens.Single(token => token.Id == task.Token.Id).OutputData!;
        output.Should().NotContainKey("employeeId");
        output["reason"].Should().Be("Holiday");
    }

    // Testzweck: Rechte werden vor Feldregeln geprüft. Ein fremder Aufrufer erfährt auch
    // durch absichtlich ungültige Daten keine Formular-/Aufgabeninformationen.
    [Test]
    public async Task ForeignTask_ShouldRemainNotFoundBeforeValidation()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval", Schema);
        var task = await context.StartAsync("assignee=\"anna\"");
        using var client = context.CreateClient();
        (await client.PostAsJsonAsync("/usertask", Result(task, "{}"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // Testzweck: Ein direkter HTTP-Start ohne Browser kann Pflicht- und Wertebereichsregeln
    // nicht umgehen; bei Ablehnung darf keine Instanz angelegt werden.
    [Test]
    public async Task Start_ShouldRejectInvalidInputAndAcceptValidInput()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval", Schema);
        await context.DeployAsync("", startFormKey: "Approval");
        using var client = context.CreateClient();
        var path = "/definition/meta/Definitions_Completion/instance";
        using var rejected = await client.PostAsJsonAsync(path, new { variables = new { } });
        rejected.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().BeEmpty();
        using var accepted = await client.PostAsJsonAsync(path, new { variables = new { reason = "Holiday" } });
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().ContainSingle();
    }

    // Testzweck: Nicht serverseitig prüfbare Skripte und unbekannte Komponenten werden
    // nicht still ignoriert oder ausgeführt, sondern schon vor Aktivierung abgelehnt.
    [TestCase("{\"components\":[{\"type\":\"textfield\",\"key\":\"reason\",\"validate\":{\"custom\":\"valid = true;\"}}]}")]
    // Testzweck: Unbekannte Typen öffnen keinen ungeprüften Ergebnis-Scope.
    [TestCase("{\"components\":[{\"type\":\"unknown\",\"key\":\"payload\"}]}")]
    public async Task Deployment_ShouldRejectUnsupportedContracts(string schema)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await FormTestSeed.StoreAsync(context.Storage, "Approval", schema);
        Func<Task> deploy = () => context.DeployAsync("");
        await deploy.Should().ThrowAsync<InvalidOperationException>().WithMessage("*contract*");
        (await context.Storage.DefinitionStorage.GetDeployedDefinition("Definitions_Completion")).Should().BeNull();
    }

    // Testzweck: Ein direkter Workflow-Start akzeptiert nur aktuell aktive typisierte
    // Referenzen. Eine Deaktivierung zwischen Anzeige und Submission wird erneut geprueft.
    [Test]
    public async Task Start_ShouldRevalidateDirectorySubjectAgainstCurrentSnapshot()
    {
        using var context = new AuthenticatedWorkflowTestContext();
        var subjectId = await PublishDirectoryUserAsync(context, active: true);
        await FormTestSeed.StoreAsync(context.Storage, "Approval", DirectorySchema);
        await context.DeployAsync("", startFormKey: "Approval");
        using var client = context.CreateClient();
        var path = "/definition/meta/Definitions_Completion/instance";

        using var accepted = await client.PostAsJsonAsync(path, new
        {
            variables = new { representative = new { kind = "user", id = subjectId } }
        });
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);

        (await PublishDirectoryUserAsync(context, active: false)).Should().Be(subjectId);
        using var rejected = await client.PostAsJsonAsync(path, new
        {
            variables = new { representative = new { kind = "user", id = subjectId } }
        });
        rejected.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").GetProperty("representative")[0].GetString()
            .Should().Be("selection.invalid");
    }

    // Testzweck: Beide historischen Abschlussrouten verwenden fuer Verzeichnisfelder denselben
    // autoritativen Snapshot und lehnen Freitext statt SubjectRef browserunabhaengig ab.
    [TestCase("/usertask")]
    [TestCase("/form/result")]
    public async Task Completion_ShouldRequireTypedDirectorySubject(string route)
    {
        using var context = new AuthenticatedWorkflowTestContext();
        await PublishDirectoryUserAsync(context, active: true);
        await FormTestSeed.StoreAsync(context.Storage, "Approval", DirectorySchema);
        var task = await context.StartAsync("assignee=\"bert\"");
        using var client = context.CreateClient();

        using var response = await client.PostAsJsonAsync(route, Result(task, "{\"representative\":\"Anna\"}"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("errors").GetProperty("representative")[0].GetString()
            .Should().Be("type.subject_ref");
        await context.AssertStillActiveAsync(task);
    }

    private const string DirectorySchema = """
        {"flowzer":{"contractVersion":2},"components":[
          {"type":"flowzerSubject","key":"representative","validate":{"required":true},
           "flowzer":{"subjectSelection":{"allowUsers":true,"allowGroups":false}}}]}
        """;

    private static async Task<Guid> PublishDirectoryUserAsync(
        AuthenticatedWorkflowTestContext context,
        bool active)
    {
        var now = DateTime.UtcNow;
        var snapshot = new DirectorySnapshot
        {
            GenerationId = Guid.NewGuid(),
            Issuer = AuthenticatedWorkflowTestContext.Issuer,
            CompletedAtUtc = now,
            Users =
            [
                new DirectoryUser
                {
                    Id = Guid.NewGuid(),
                    SourceKind = DirectorySourceKind.Keycloak,
                    Issuer = AuthenticatedWorkflowTestContext.Issuer,
                    Subject = "subject-representative",
                    DisplayName = "Anna Vertreterin",
                    IsActive = active
                }
            ]
        };
        (await context.Storage.IdentityDirectoryStorage.TryStartSync(
            snapshot.Issuer, snapshot.GenerationId, now, now.AddMinutes(5))).Should().BeTrue();
        await context.Storage.IdentityDirectoryStorage.PublishSnapshot(snapshot);
        return (await context.Storage.IdentityDirectoryStorage.GetActiveSnapshot())!
            .Users.Single(user => user.Subject == "subject-representative").Id;
    }

    private static ExpandoObject Data(string json) => JsonConvert.DeserializeObject<ExpandoObject>(json)!;
    private static UserTaskResultDto Result(UserTaskSubscription task, string json) => new()
    {
        ProcessInstanceId = task.ProcessInstanceId, TokenId = task.Token.Id,
        FlowNodeId = task.Token.CurrentFlowNode!.Id, Data = Data(json)
    };
}
