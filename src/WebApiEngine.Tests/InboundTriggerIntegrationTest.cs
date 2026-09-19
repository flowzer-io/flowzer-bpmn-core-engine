using System.Dynamic;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using WebApiEngine.BusinessLogic;
using WebApiEngine.InboundTriggers;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Der öffentliche Vertrag der eingehenden Auslöser: Verwaltung durch den Betrieb, Aufruf von
/// außen ohne Anmeldung und ausschließlich mit Signatur.
/// </summary>
[NonParallelizable]
public class InboundTriggerIntegrationTest
{
    // Testzweck: Ein gueltig signierter Aufruf startet eine Instanz und uebernimmt im Modus
    // "fields" genau die freigegebenen obersten Felder — und sonst nichts, auch wenn das
    // fremde System mehr schickt.
    [Test]
    public async Task Trigger_ShouldStartInstanceWithSelectedFieldsOnly()
    {
        using var context = new InboundTriggerTestContext();
        var workflow = await context.DeployStartWorkflowAsync();
        var created = await CreateStartTriggerAsync(context, workflow, "fields", ["orderId", "amount"]);
        using var client = context.CreateAnonymousClient();
        const string body = """{"orderId":"4711","amount":42,"internalNote":"nicht uebernehmen"}""";

        var response = await client.SendAsync(
            InboundTriggerTestContext.SignedRequest(created.Trigger.Key, created.Secret, body));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var result = await response.Content.ReadFromJsonAsync<StartResult>();
        result!.InstanceId.Should().NotBeEmpty();

        var instance = await context.Storage.InstanceStorage.GetProcessInstance(result.InstanceId);
        var variables = (IDictionary<string, object?>)instance.Tokens
            .Single(token => token.ParentTokenId is null).Variables!;
        variables.Should().ContainKey("orderId").WhoseValue.Should().Be("4711");
        variables.Should().ContainKey("amount");
        variables.Should().NotContainKey("internalNote");
    }

    // Testzweck: Im Modus "body" liegt der ganze Koerper unter einer einzigen Variablen und
    // nicht als einzelne Felder; sonst koennte ein fremdes System beliebige Prozessvariablen
    // ueberschreiben, indem es Felder mit passenden Namen schickt.
    [Test]
    public async Task Trigger_ShouldWrapTheWholeBodyInPayload_WhenModeIsBody()
    {
        using var context = new InboundTriggerTestContext();
        var workflow = await context.DeployStartWorkflowAsync();
        var created = await CreateStartTriggerAsync(context, workflow, "body", []);
        using var client = context.CreateAnonymousClient();
        const string body = """{"orderId":"4711","nested":{"a":1}}""";

        var response = await client.SendAsync(
            InboundTriggerTestContext.SignedRequest(created.Trigger.Key, created.Secret, body));

        var result = await response.Content.ReadFromJsonAsync<StartResult>();
        var instance = await context.Storage.InstanceStorage.GetProcessInstance(result!.InstanceId);
        var variables = (IDictionary<string, object?>)instance.Tokens
            .Single(token => token.ParentTokenId is null).Variables!;
        variables.Keys.Should().BeEquivalentTo([InboundTriggerPayload.BodyVariableName]);
        var payload = (IDictionary<string, object?>)variables[InboundTriggerPayload.BodyVariableName]!;
        payload.Should().ContainKey("orderId").WhoseValue.Should().Be("4711");
    }

    // Testzweck: Eine falsche Signatur wird abgewiesen, und der Fehlgrund landet am Ausloeser,
    // damit der Betrieb sieht, dass jemand mit falschem Geheimnis anklopft.
    [Test]
    public async Task Trigger_ShouldRejectAWrongSignature()
    {
        using var context = new InboundTriggerTestContext();
        var workflow = await context.DeployStartWorkflowAsync();
        var created = await CreateStartTriggerAsync(context, workflow, "fields", []);
        using var client = context.CreateAnonymousClient();

        var response = await client.SendAsync(InboundTriggerTestContext.SignedRequest(
            created.Trigger.Key, created.Secret, "{}", signatureOverride: "sha256=" + new string('0', 64)));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var stored = await ReadTriggerAsync(context, created.Trigger.Id);
        stored.LastFailureReason.Should().Be("signature");
        stored.LastFailureAt.Should().NotBeNull();
        stored.UseCount.Should().Be(0);
    }

    // Testzweck: Ein alter Zeitstempel wird abgewiesen, auch wenn die Signatur dazu passt.
    // Ohne dieses Fenster liesse sich ein einmal mitgelesener Aufruf beliebig wiederholen.
    [Test]
    public async Task Trigger_ShouldRejectAnExpiredTimestamp()
    {
        using var context = new InboundTriggerTestContext();
        var workflow = await context.DeployStartWorkflowAsync();
        var created = await CreateStartTriggerAsync(context, workflow, "fields", []);
        using var client = context.CreateAnonymousClient();

        var response = await client.SendAsync(InboundTriggerTestContext.SignedRequest(
            created.Trigger.Key, created.Secret, "{}", sentAt: DateTimeOffset.UtcNow.AddMinutes(-10)));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadTriggerAsync(context, created.Trigger.Id)).LastFailureReason.Should().Be("timestamp");
    }

    // Testzweck: Ein unbekannter und ein abgeschalteter Schluessel antworten identisch. Ein
    // Unterschied verriete, welche Schluessel es gibt und welche Anbindung gerade ruht.
    [Test]
    public async Task Trigger_ShouldAnswerIdenticallyForUnknownAndDisabledKeys()
    {
        using var context = new InboundTriggerTestContext();
        var workflow = await context.DeployStartWorkflowAsync();
        var created = await CreateStartTriggerAsync(context, workflow, "fields", []);
        using var operatorClient = context.CreateOperatorClient();
        await DisableAsync(operatorClient, created.Trigger);
        using var client = context.CreateAnonymousClient();

        var disabled = await client.SendAsync(
            InboundTriggerTestContext.SignedRequest(created.Trigger.Key, created.Secret, "{}"));
        var unknown = await client.SendAsync(
            InboundTriggerTestContext.SignedRequest("gibtesnicht", created.Secret, "{}"));

        disabled.StatusCode.Should().Be(HttpStatusCode.NotFound);
        unknown.StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Der traceId identifiziert die einzelne Anfrage und darf sich unterscheiden; alles
        // andere muss gleich sein, sonst waere die Antwort ein Orakel ueber den Bestand.
        WithoutTraceId(await disabled.Content.ReadAsStringAsync()).Should()
            .Be(WithoutTraceId(await unknown.Content.ReadAsStringAsync()));
    }

    // Testzweck: Ein zu grosser Koerper wird abgewiesen, bevor er gelesen oder geparst wird.
    [Test]
    public async Task Trigger_ShouldRejectAnOversizedBody()
    {
        using var context = new InboundTriggerTestContext();
        var workflow = await context.DeployStartWorkflowAsync();
        var created = await CreateStartTriggerAsync(context, workflow, "body", []);
        using var client = context.CreateAnonymousClient();
        var body = "{\"padding\":\"" + new string('x', InboundTriggerPayload.MaxBodyBytes + 1) + "\"}";

        var response = await client.SendAsync(
            InboundTriggerTestContext.SignedRequest(created.Trigger.Key, created.Secret, body));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    // Testzweck: Ein Workflow ohne deployte Version laesst sich nicht starten; das ist ein
    // fachlicher Fehler am Ziel (422) und nicht ein Fehler des Aufrufers.
    [Test]
    public async Task Trigger_ShouldAnswer422_WhenTheWorkflowHasNoDeployedVersion()
    {
        using var context = new InboundTriggerTestContext();
        var workflow = await context.RegisterUndeployedWorkflowAsync();
        var created = await CreateStartTriggerAsync(context, workflow, "fields", []);
        using var client = context.CreateAnonymousClient();

        var response = await client.SendAsync(
            InboundTriggerTestContext.SignedRequest(created.Trigger.Key, created.Secret, "{}"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadTriggerAsync(context, created.Trigger.Id)).LastFailureReason.Should().Be("not-deployed");
    }

    // Testzweck: Ein Nachrichtenausloeser korreliert ueber den konfigurierten Pfad an eine
    // wartende Instanz und meldet das zurueck.
    [Test]
    public async Task Trigger_ShouldCorrelateToAWaitingInstance()
    {
        using var context = new InboundTriggerTestContext();
        var workflow = await context.DeployMessageWorkflowAsync();
        var variables = new ExpandoObject();
        ((IDictionary<string, object?>)variables)["orderId"] = "4711";
        var instance = await context.Services.GetRequiredService<BpmnBusinessLogic>()
            .StartProcessInstance(workflow, variables);
        var created = await CreateMessageTriggerAsync(context, "OrderPaid", "order.id");
        using var client = context.CreateAnonymousClient();

        var response = await client.SendAsync(InboundTriggerTestContext.SignedRequest(
            created.Trigger.Key, created.Secret, """{"order":{"id":"4711"}}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await response.Content.ReadFromJsonAsync<MessageResult>())!.Correlated.Should().BeTrue();
        (await context.Storage.InstanceStorage.GetProcessInstance(instance.InstanceId))
            .IsFinished.Should().BeTrue();
    }

    // Testzweck: Wartet keine Instanz, ist das kein Fehler, sondern eine Antwort mit
    // correlated=false. Eine Fehlermeldung waere fuer einen Aufrufer ein Weg herauszufinden,
    // welche Vorgaenge es in dieser Installation gibt.
    [Test]
    public async Task Trigger_ShouldReportNotCorrelated_WhenNoInstanceIsWaiting()
    {
        using var context = new InboundTriggerTestContext();
        await context.DeployMessageWorkflowAsync();
        var created = await CreateMessageTriggerAsync(context, "OrderPaid", "order.id");
        using var client = context.CreateAnonymousClient();

        var response = await client.SendAsync(InboundTriggerTestContext.SignedRequest(
            created.Trigger.Key, created.Secret, """{"order":{"id":"unbekannt"}}"""));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await response.Content.ReadFromJsonAsync<MessageResult>())!.Correlated.Should().BeFalse();
        // Auch dieser Aufruf hat getan, wozu der Ausloeser da ist, und zaehlt deshalb.
        (await ReadTriggerAsync(context, created.Trigger.Id)).UseCount.Should().Be(1);
    }

    // Testzweck: Das Geheimnis steht ausschliesslich in der Antwort des Anlegens. Ein spaeteres
    // Lesen liefert es nie — sonst waere die Liste ein zweiter Weg, an es zu kommen.
    [Test]
    public async Task Management_ShouldReturnTheSecretOnlyOnce()
    {
        using var context = new InboundTriggerTestContext();
        var workflow = await context.DeployStartWorkflowAsync();
        var created = await CreateStartTriggerAsync(context, workflow, "fields", []);
        using var client = context.CreateOperatorClient();

        var listResponse = await client.GetAsync("/inbound-trigger");
        var raw = await listResponse.Content.ReadAsStringAsync();

        created.Secret.Should().NotBeNullOrWhiteSpace();
        raw.Should().NotContain(created.Secret);
        raw.Should().Contain(created.Trigger.Key, "der Schluessel ist kein Geheimnis und wird gebraucht");
        raw.Should().NotContain("secretHash", "die Ableitung gehoert nicht in die Projektion");
    }

    // Testzweck: Nach dem Erneuern gilt nur noch das neue Geheimnis. Bliebe das alte gueltig,
    // liesse sich ein verlorenes Geheimnis nicht zurueckziehen.
    [Test]
    public async Task Management_ShouldInvalidateTheOldSecretAfterRotation()
    {
        using var context = new InboundTriggerTestContext();
        var workflow = await context.DeployStartWorkflowAsync();
        var created = await CreateStartTriggerAsync(context, workflow, "fields", []);
        using var operatorClient = context.CreateOperatorClient();
        var rotated = await ReadResultAsync<InboundTriggerSecretDto>(
            await operatorClient.PostAsync($"/inbound-trigger/{created.Trigger.Id}/rotate-secret", null));
        using var client = context.CreateAnonymousClient();

        var withOld = await client.SendAsync(
            InboundTriggerTestContext.SignedRequest(created.Trigger.Key, created.Secret, "{}"));
        var withNew = await client.SendAsync(
            InboundTriggerTestContext.SignedRequest(created.Trigger.Key, rotated.Secret, "{}"));

        rotated.Secret.Should().NotBe(created.Secret);
        withOld.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        withNew.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    // Testzweck: Die Verwaltung gehoert dem Betrieb. Wer hier anlegt, oeffnet eine Adresse, die
    // ohne Anmeldung Workflows startet — das darf keine blosse Zugangsrolle koennen.
    [Test]
    public async Task Management_ShouldRequireTheOperatorRole()
    {
        using var context = new InboundTriggerTestContext();
        var workflow = await context.DeployStartWorkflowAsync();
        using var plain = context.CreatePlainClient();
        using var anonymous = context.CreateAnonymousClient();

        var listed = await plain.GetAsync("/inbound-trigger");
        var createdWithoutRole = await plain.PostAsJsonAsync("/inbound-trigger", new
        {
            name = "Ohne Rolle", kind = "start", definitionId = workflow, variablesMode = "fields"
        });
        var listedAnonymously = await anonymous.GetAsync("/inbound-trigger");

        listed.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        createdWithoutRole.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        listedAnonymously.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Testzweck: Ein erfolgreicher Aufruf zaehlt und vermerkt den Zeitpunkt; die Fehlerfelder
    // bleiben dabei leer. Ohne beides liesse sich im Betrieb nicht unterscheiden, ob ein
    // fremdes System gar nicht ankommt oder nur nichts findet.
    [Test]
    public async Task Trigger_ShouldCountEverySuccessfulCall()
    {
        using var context = new InboundTriggerTestContext();
        var workflow = await context.DeployStartWorkflowAsync();
        var created = await CreateStartTriggerAsync(context, workflow, "fields", []);
        using var client = context.CreateAnonymousClient();

        await client.SendAsync(InboundTriggerTestContext.SignedRequest(created.Trigger.Key, created.Secret, "{}"));
        await client.SendAsync(InboundTriggerTestContext.SignedRequest(created.Trigger.Key, created.Secret, "{}"));

        var stored = await ReadTriggerAsync(context, created.Trigger.Id);
        stored.UseCount.Should().Be(2);
        stored.LastUsedAt.Should().NotBeNull();
        stored.LastFailureAt.Should().BeNull();
    }

    // Testzweck: Eine vom Ausloeser gestartete Instanz hat keinen menschlichen Initiator. Der
    // Betrieb sieht sie, eine blosse Zugangsrolle nicht — die Initiatorregel der
    // Rechtepruefung darf die technische Kennung nicht versehentlich jemandem zuordnen.
    [Test]
    public async Task Trigger_ShouldStartInstancesWithoutAHumanInitiator()
    {
        using var context = new InboundTriggerTestContext();
        var workflow = await context.DeployStartWorkflowAsync();
        var created = await CreateStartTriggerAsync(context, workflow, "fields", []);
        using var anonymous = context.CreateAnonymousClient();
        var started = await anonymous.SendAsync(
            InboundTriggerTestContext.SignedRequest(created.Trigger.Key, created.Secret, "{}"));
        var instanceId = (await started.Content.ReadFromJsonAsync<StartResult>())!.InstanceId;

        using var operatorClient = context.CreateOperatorClient();
        using var plain = context.CreatePlainClient();
        var seenByOperator = await ReadResultAsync<ProcessInstanceInfoDto[]>(
            await operatorClient.GetAsync("/instance"));
        var seenByPlain = await ReadResultAsync<ProcessInstanceInfoDto[]>(await plain.GetAsync("/instance"));

        seenByOperator.Should().ContainSingle(instance => instance.InstanceId == instanceId);
        seenByPlain.Should().BeEmpty("die technische Ausloeserkennung ist kein Benutzerkonto");

        var instance = await context.Storage.InstanceStorage.GetProcessInstance(instanceId);
        var master = instance.Tokens.Single(token => token.ParentTokenId is null);
        master.Initiator!.Issuer.Should().Be(InboundTriggerIdentity.Issuer);
        master.Initiator.Subject.Should().Be(created.Trigger.Id.ToString());
    }

    // Testzweck: Das Kontingent gilt je Ausloeser. Ohne eine eigene Partition fielen alle
    // anonymen Aufrufe in dieselbe Adresspartition, und ein einzelner Ausloeser koennte das
    // Kontingent aller verbrauchen.
    [Test]
    public async Task Trigger_ShouldLimitCallsPerKey()
    {
        using var context = new InboundTriggerTestContext(new Dictionary<string, string?>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:PermitLimit"] = "1000",
            ["RateLimiting:WindowSeconds"] = "60",
            ["InboundTriggers:PermitLimit"] = "2",
            ["InboundTriggers:WindowSeconds"] = "60"
        });
        var workflow = await context.DeployStartWorkflowAsync();
        var first = await CreateStartTriggerAsync(context, workflow, "fields", []);
        var second = await CreateStartTriggerAsync(context, workflow, "fields", []);
        using var client = context.CreateAnonymousClient();

        var statusCodes = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            statusCodes.Add((await client.SendAsync(
                InboundTriggerTestContext.SignedRequest(first.Trigger.Key, first.Secret, "{}"))).StatusCode);
        }

        // Der zweite Ausloeser hat sein eigenes Fenster und bleibt davon unberuehrt.
        var otherKey = await client.SendAsync(
            InboundTriggerTestContext.SignedRequest(second.Trigger.Key, second.Secret, "{}"));

        statusCodes.Take(2).Should().AllBeEquivalentTo(HttpStatusCode.Accepted);
        statusCodes[2].Should().Be(HttpStatusCode.TooManyRequests);
        otherKey.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    // Testzweck: Derselbe Idempotenzschluessel startet keine zweite Instanz. Ein fremdes System
    // wiederholt einen unsicheren Netzwerkaufruf, und ohne diesen Schutz liefe der Vorgang
    // doppelt.
    [Test]
    public async Task Trigger_ShouldNotStartTwice_WhenTheSameIdempotencyKeyIsRepeated()
    {
        using var context = new InboundTriggerTestContext();
        var workflow = await context.DeployStartWorkflowAsync();
        var created = await CreateStartTriggerAsync(context, workflow, "fields", ["orderId"]);
        using var client = context.CreateAnonymousClient();
        const string body = """{"orderId":"4711"}""";

        var first = await client.SendAsync(InboundTriggerTestContext.SignedRequest(
            created.Trigger.Key, created.Secret, body, idempotencyKey: "abgabe-1"));
        var repeated = await client.SendAsync(InboundTriggerTestContext.SignedRequest(
            created.Trigger.Key, created.Secret, body, idempotencyKey: "abgabe-1"));

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        repeated.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var firstInstance = (await first.Content.ReadFromJsonAsync<StartResult>())!.InstanceId;
        var repeatedInstance = (await repeated.Content.ReadFromJsonAsync<StartResult>())!.InstanceId;
        repeatedInstance.Should().Be(firstInstance);
        (await context.Storage.InstanceStorage.GetAllInstances()).Should().ContainSingle();
    }

    // Testzweck: Ohne installationsweiten Schluessel gibt es nichts, worunter ein Geheimnis
    // sicher liegen koennte. Dann wird kein Ausloeser angelegt, statt einen ungeschuetzten
    // anzunehmen.
    [Test]
    public async Task Management_ShouldRefuseWithoutAnInstallationKey()
    {
        using var context = new InboundTriggerTestContext(new Dictionary<string, string?>
        {
            ["InboundTriggers:SecretKey"] = ""
        });
        var workflow = await context.DeployStartWorkflowAsync();
        using var client = context.CreateOperatorClient();

        var response = await client.PostAsJsonAsync("/inbound-trigger", new
        {
            name = "Ohne Schluessel", kind = "start", definitionId = workflow, variablesMode = "fields"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SecretKey");
    }

    // Testzweck: Ein Ausloeser darf nur auf einen Workflow zeigen, den es gibt. Sonst faellt
    // der Fehler erst beim fremden System auf, nicht beim Betrieb.
    [Test]
    public async Task Management_ShouldRejectAnUnknownWorkflow()
    {
        using var context = new InboundTriggerTestContext();
        using var client = context.CreateOperatorClient();

        var response = await client.PostAsJsonAsync("/inbound-trigger", new
        {
            name = "Ins Leere", kind = "start", definitionId = "gibtesnicht", variablesMode = "fields"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<InboundTriggerSecretDto> CreateStartTriggerAsync(
        InboundTriggerTestContext context,
        string definitionId,
        string variablesMode,
        string[] allowedFields)
    {
        using var client = context.CreateOperatorClient();
        return await ReadResultAsync<InboundTriggerSecretDto>(
            await client.PostAsJsonAsync("/inbound-trigger", new
            {
                name = "Ticketsystem",
                kind = "start",
                definitionId,
                variablesMode,
                allowedFields
            }));
    }

    private static async Task<InboundTriggerSecretDto> CreateMessageTriggerAsync(
        InboundTriggerTestContext context,
        string messageName,
        string correlationKeyPath)
    {
        using var client = context.CreateOperatorClient();
        return await ReadResultAsync<InboundTriggerSecretDto>(
            await client.PostAsJsonAsync("/inbound-trigger", new
            {
                name = "Zahlungsdienst",
                kind = "message",
                messageName,
                correlationKeyPath,
                variablesMode = "fields",
                allowedFields = Array.Empty<string>()
            }));
    }

    private static async Task DisableAsync(HttpClient client, InboundTriggerDto trigger)
    {
        var response = await client.PutAsJsonAsync($"/inbound-trigger/{trigger.Id}", new
        {
            name = trigger.Name,
            enabled = false,
            definitionId = trigger.DefinitionId,
            messageName = trigger.MessageName,
            correlationKeyPath = trigger.CorrelationKeyPath,
            variablesMode = "fields",
            allowedFields = trigger.AllowedFields
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<InboundTriggerDto> ReadTriggerAsync(InboundTriggerTestContext context, Guid id)
    {
        using var client = context.CreateOperatorClient();
        var triggers = await ReadResultAsync<InboundTriggerDto[]>(await client.GetAsync("/inbound-trigger"));
        return triggers.Single(trigger => trigger.Id == id);
    }

    private static async Task<T> ReadResultAsync<T>(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadFromJsonAsync<ApiStatusResult<T>>();
        payload!.Successful.Should().BeTrue(payload.ErrorMessage);
        return payload.Result!;
    }

    private static string WithoutTraceId(string payload) =>
        System.Text.RegularExpressions.Regex.Replace(payload, "\"traceId\"\\s*:\\s*\"[^\"]*\"", "\"traceId\":\"\"");

    private sealed record StartResult(Guid InstanceId);

    private sealed record MessageResult(bool Correlated);
}
