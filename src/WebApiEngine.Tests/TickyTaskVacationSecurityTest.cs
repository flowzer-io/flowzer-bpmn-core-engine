using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Model;

namespace WebApiEngine.Tests;

/// <summary>Die ausgelieferte Demo umgeht weder Formularvalidierung noch persönliche Aufgabenrechte.</summary>
[NonParallelizable]
public sealed class TickyTaskVacationSecurityTest
{
    // Testzweck: Read-only ist ein Serververtrag. Weder privater Zwischenstand noch
    // Abschluss darf die tatsächliche Rückfrage aus dem Fehlerpfad durch Browserdaten ändern.
    [TestCase(false)]
    [TestCase(true)]
    public async Task ForgedReturnReason_ShouldFailWithoutChangingCorrectionContext(bool saveDraft)
    {
        using var demo = new TickyTaskVacationTestContext();
        await demo.InitializeAsync(); await demo.StartAsync();
        await demo.CompleteAsync("Task_Personnel", "return", new() { ["begruendung"] = "Tatsächliche Rückfrage" });
        var correction = (await demo.TasksAsync()).Single();
        using var owner = demo.Client();
        await demo.ClaimAsync(owner, correction);
        var forged = demo.Application();
        forged["rueckfrage"] = "Gefälschte Rückfrage";
        using var response = saveDraft
            ? await owner.PutAsJsonAsync($"/usertask/{correction.Id}/draft", new { expectedRevision = 0, data = forged })
            : await owner.PostAsJsonAsync("/usertask", demo.Bound(correction, null, forged));
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        problem.GetProperty("errors").TryGetProperty("rueckfrage", out _).Should().BeTrue();
        (await demo.TasksAsync()).Should().ContainSingle().Which.Id.Should().Be(correction.Id);
        var root = (IDictionary<string, object?>)(await demo.InstanceAsync()).Tokens.Single(token => token.ParentTokenId is null).Variables!;
        root["rueckfrage"].Should().Be("Tatsächliche Rückfrage");
    }

    // Testzweck: Die Original-Startmaske prüft Pflicht-/Datums-/Bereichs-/Directory-
    // und Feldregeln; ungültige Daten oder ein erfundener Genehmiger starten keine Instanz.
    [TestCase("missing-substitute")]
    [TestCase("group")]
    [TestCase("unknown-user")]
    [TestCase("extra-property")]
    [TestCase("date-order")]
    [TestCase("zero-days")]
    [TestCase("supervisor-spoof")]
    public async Task InvalidApplication_ShouldReturnFieldErrorsWithoutStarting(string scenario)
    {
        using var demo = new TickyTaskVacationTestContext();
        await demo.InitializeAsync();
        var application = demo.Application();
        switch (scenario)
        {
            case "missing-substitute": application.Remove("vertretung"); break;
            case "group": application["vertretung"] = new { kind = "group", id = demo.PersonnelGroupId.ToString() }; break;
            case "unknown-user": application["vertretung"] = new { kind = "user", id = Guid.NewGuid().ToString() }; break;
            case "extra-property": application["vertretung"] = new { kind = "user", id = demo.SubstituteId.ToString(), displayName = "untrusted" }; break;
            case "date-order": application["bis"] = "2026-10-01"; break;
            case "zero-days": application["arbeitstage"] = 0; break;
            case "supervisor-spoof": application["demoSupervisor"] = new { kind = "user", id = demo.InitiatorId.ToString() }; break;
        }
        using var owner = demo.Client();
        using var response = await owner.PostAsJsonAsync($"/definition/meta/{TickyTaskVacationTestContext.DefinitionKey}/instance",
            new { expectedDefinitionId = demo.DefinitionId, variables = application });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        problem.GetProperty("errors").EnumerateObject().Should().NotBeEmpty();
        (await demo.Storage.InstanceStorage.GetAllInstances()).Should().BeEmpty();
        (await demo.Storage.ServiceTaskStorage.GetJobs()).Should().BeEmpty();
    }

    // Testzweck: Eine gewählte Genehmigungsaktion kann nicht durch einen gefälschten
    // Entscheidungswert zu einer Ablehnung/Rückgabe werden; der Task bleibt unverändert offen.
    [Test]
    public async Task ForgedDecision_ShouldFailWithoutAdvancingTheRound()
    {
        using var demo = new TickyTaskVacationTestContext();
        await demo.InitializeAsync(); await demo.StartAsync();
        var task = (await demo.TasksAsync()).Single(item => item.Token.CurrentFlowNode!.Id == "Task_Supervisor");
        using var supervisor = demo.Client(TickyTaskVacationTestContext.SupervisorSubject);
        await demo.ClaimAsync(supervisor, task);
        using var response = await supervisor.PostAsJsonAsync("/usertask", demo.Bound(task, "approve", new() { ["entscheidung"] = "abgelehnt" }));
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await demo.TasksAsync()).Should().HaveCount(3);
        (await demo.InstanceAsync()).Tokens.Single(token => token.Id == task.Token.Id).OutputData.Should().BeNull();
    }

    // Testzweck: Zwei synthetische Personalgruppenmitglieder konkurrieren um denselben
    // echten Demo-Task. Genau eine Person übernimmt; die andere kann weder Formular noch
    // Entwurf öffnen. Übernahme und Abschluss nennen dieselbe echte Person samt TT-Client.
    [Test]
    public async Task PersonnelGroupRace_ShouldHaveOneAuditedPersonAndNoSecondWorker()
    {
        using var demo = new TickyTaskVacationTestContext();
        await demo.InitializeAsync(); await demo.StartAsync();
        var task = (await demo.TasksAsync()).Single(item => item.Token.CurrentFlowNode!.Id == "Task_Personnel");
        using var first = demo.Client(TickyTaskVacationTestContext.PersonnelSubject);
        using var second = demo.Client(TickyTaskVacationTestContext.OtherPersonnelSubject);
        var responses = await Task.WhenAll(first.PostAsJsonAsync($"/usertask/{task.Id}/claim", new { expectedRevision = 0 }),
            second.PostAsJsonAsync($"/usertask/{task.Id}/claim", new { expectedRevision = 0 }));
        try
        {
            responses.Should().ContainSingle(response => response.StatusCode == HttpStatusCode.OK);
            responses.Should().ContainSingle(response => response.StatusCode == HttpStatusCode.Conflict);
            var firstWon = responses[0].StatusCode == HttpStatusCode.OK;
            var winner = firstWon ? first : second;
            var loser = firstWon ? second : first;
            using var hiddenForm = await loser.GetAsync($"/usertask/{task.Id}/form");
            hiddenForm.StatusCode.Should().Be(HttpStatusCode.NotFound);
            using var hiddenDraft = await loser.GetAsync($"/usertask/{task.Id}/draft");
            hiddenDraft.StatusCode.Should().Be(HttpStatusCode.NotFound);
            using var completed = await winner.PostAsJsonAsync("/usertask", demo.Bound(task, "approve", new() { ["begruendung"] = "Persönlich geprüft" }));
            completed.StatusCode.Should().Be(HttpStatusCode.OK);
            var events = await demo.Storage.UserTaskLifecycleStorage.GetEvents(task.Id);
            events.Select(item => item.Action).Should().Equal("claim", "complete");
            var subject = (firstWon ? TickyTaskVacationTestContext.PersonnelSubject : TickyTaskVacationTestContext.OtherPersonnelSubject).ToString();
            foreach (var item in events)
            {
                item.AuthenticatedActor!.Identity.Subject.Should().Be(subject);
                item.AuthenticatedActor!.ClientId.Should().Be("synthetic-tickytask-demo");
            }
            var token = (await demo.InstanceAsync()).Tokens.Single(token => token.Id == task.Token.Id);
            token.CompletedByActor!.Identity.Subject.Should().Be(subject);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
}
