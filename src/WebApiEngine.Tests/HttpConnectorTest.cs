using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WebApiEngine.Connectors;
using Model;
using Variables = System.Dynamic.ExpandoObject;

namespace WebApiEngine.Tests;

/// <summary>
/// Der mitgelieferte HTTP-Konnektor. Er ist ein Worker wie jeder andere und entscheidet nur
/// ueber eines selbst: ob ein Ausgang technisch war (erneut versuchen) oder fachlich (im Modell
/// einen eigenen Weg nehmen).
/// </summary>
[NonParallelizable]
public class HttpConnectorTest
{
    private const string SecretVariable = "FLOWZER_CONNECTOR_SECRET_ZAHLUNG";

    // Testzweck: Der Erfolgsfall gibt Status, lesbare Header und den JSON-Koerper als Objekt
    // zurueck — nicht als Text. Nur so kann ein folgender Schritt einzelne Felder lesen.
    [Test]
    public async Task Execute_ShouldReturnStatusHeadersAndTheParsedJsonBody()
    {
        var handler = new RecordingConnectorHandler(_ =>
        {
            var response = RecordingConnectorHandler.Json("""{ "belegNummer": "R-2026-118", "betrag": 42 }""");
            response.Headers.TryAddWithoutValidation("X-Request-Id", "abc");
            return response;
        });

        var outcome = await Run(handler, ConnectorTestData.Inputs(("url", ConnectorTestData.AllowedUrl)));

        var completed = outcome.Should().BeOfType<ConnectorOutcome.Completed>().Subject;
        var values = ConnectorTestData.Read(completed.Variables);
        values["status"].Should().Be(200);
        var body = values["body"].Should().BeOfType<Variables>().Subject;
        ConnectorTestData.Read(body)["belegNummer"].Should().Be("R-2026-118");
        // Die Schreibweise des Headernamens bestimmt die Laufzeit, nicht der Server.
        var headers = ConnectorTestData.Read(values["headers"] as Variables);
        headers.Should().ContainKey("X-Request-ID").WhoseValue.Should().Be("abc");
        headers.Should().ContainKey("Content-Type");
    }

    // Testzweck: Methode, Query und Koerper aus den Auftragsvariablen kommen tatsaechlich am
    // Ziel an; sonst liefe ein Modell mit deklarierten Eingaben ins Leere.
    [Test]
    public async Task Execute_ShouldSendMethodQueryAndJsonBody()
    {
        var handler = new RecordingConnectorHandler(_ => RecordingConnectorHandler.Json("{}"));

        await Run(handler, ConnectorTestData.Inputs(
            ("url", ConnectorTestData.AllowedUrl),
            ("method", "post"),
            ("query", ConnectorTestData.Inputs(("mandant", "maass it"))),
            ("body", ConnectorTestData.Inputs(("betrag", 42)))));

        handler.Request!.Method.Method.Should().Be("POST");
        handler.Request.RequestUri!.Query.Should().Contain("mandant=maass%20it");
        handler.RequestBody.Should().Be("""{"betrag":42}""");
        handler.Request.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    // Testzweck: `secret:NAME` wird erst beim Aufruf aus der Umgebung aufgeloest. Der Klartext
    // darf weder im Auftrag noch im Ergebnis stehen — sonst stuende er im Vorgang und in jeder
    // Betriebssicht, die den Auftrag zeigt.
    [Test]
    public async Task Execute_ShouldResolveTheAuthorizationSecret_WithoutLeakingItIntoJobOrResult()
    {
        Environment.SetEnvironmentVariable(SecretVariable, "Bearer geheim-123");
        try
        {
            var handler = new RecordingConnectorHandler(_ => RecordingConnectorHandler.Json("{}"));
            var job = ConnectorTestData.Job(HttpConnectorOptions.JobType, ConnectorTestData.Inputs(
                ("url", ConnectorTestData.AllowedUrl),
                ("authorization", "secret:ZAHLUNG")));

            var outcome = await Run(handler, job);

            handler.Authorization!.ToString().Should().Be("Bearer geheim-123");
            ConnectorTestData.Read(job.Variables)["authorization"].Should().Be("secret:ZAHLUNG");
            var completed = outcome.Should().BeOfType<ConnectorOutcome.Completed>().Subject;
            Newtonsoft.Json.JsonConvert.SerializeObject(completed.Variables)
                .Should().NotContain("geheim-123");
        }
        finally
        {
            Environment.SetEnvironmentVariable(SecretVariable, null);
        }
    }

    // Testzweck: Eine nicht aufloesbare Secret-Referenz ist eine unvollstaendige Anfrage, kein
    // Netzproblem. Ein zweiter Versuch aenderte nichts, also wird sie nicht wiederholt — und der
    // Name der Referenz bleibt aus der Meldung heraus.
    [Test]
    public async Task Execute_ShouldReportAnInvalidRequest_WhenTheSecretCannotBeResolved()
    {
        var handler = new RecordingConnectorHandler(_ => RecordingConnectorHandler.Json("{}"));

        var outcome = await Run(handler, ConnectorTestData.Inputs(
            ("url", ConnectorTestData.AllowedUrl),
            ("authorization", "secret:NICHT_VORHANDEN")));

        var error = outcome.Should().BeOfType<ConnectorOutcome.BpmnError>().Subject;
        error.ErrorCode.Should().Be(HttpConnector.InvalidRequestErrorCode);
        error.ErrorMessage.Should().NotContain("NICHT_VORHANDEN");
        handler.Request.Should().BeNull("ohne aufloesbares Secret darf kein Aufruf hinausgehen");
    }

    // Testzweck: Ohne Adresse gibt es nichts zu tun; das ist ein Modellfehler und kein Grund,
    // den Auftrag wieder und wieder zu vergeben.
    [Test]
    public async Task Execute_ShouldReportAnInvalidRequest_WhenTheUrlIsMissing()
    {
        var handler = new RecordingConnectorHandler(_ => RecordingConnectorHandler.Json("{}"));

        var outcome = await Run(handler, ConnectorTestData.Inputs(("method", "GET")));

        outcome.Should().BeOfType<ConnectorOutcome.BpmnError>()
            .Which.ErrorCode.Should().Be(HttpConnector.InvalidRequestErrorCode);
    }

    // Testzweck: Ein nicht freigegebenes Ziel wird gar nicht erst aufgerufen. Ohne diese Regel
    // waere jedes Modell eine Aufforderung an die Engine, beliebige Adressen zu besuchen.
    [Test]
    public async Task Execute_ShouldRefuseAHostOutsideTheAllowList()
    {
        var handler = new RecordingConnectorHandler(_ => RecordingConnectorHandler.Json("{}"));

        var outcome = await Run(handler, ConnectorTestData.Inputs(("url", "https://boeser.example/hook")));

        outcome.Should().BeOfType<ConnectorOutcome.BpmnError>()
            .Which.ErrorCode.Should().Be(HttpConnector.NotAllowedErrorCode);
        handler.Request.Should().BeNull();
    }

    // Testzweck: Eine leere Freigabeliste heisst "kein Aufruf moeglich". Ein aktivierter
    // Konnektor ohne ausdrueckliches Ziel darf nicht heimlich alles duerfen.
    [Test]
    public async Task Execute_ShouldRefuseEveryTarget_WhenNoHostIsAllowed()
    {
        var handler = new RecordingConnectorHandler(_ => RecordingConnectorHandler.Json("{}"));

        var outcome = await Run(
            handler,
            ConnectorTestData.Inputs(("url", ConnectorTestData.AllowedUrl)),
            options => options.Http.AllowedHosts = []);

        outcome.Should().BeOfType<ConnectorOutcome.BpmnError>()
            .Which.ErrorCode.Should().Be(HttpConnector.NotAllowedErrorCode);
    }

    // Testzweck: Ein freigegebener Name, der ins eigene Netz zeigt, bleibt verboten — dieselbe
    // Regel wie bei den Worker-Webhooks.
    [Test]
    public async Task Execute_ShouldRefuseAnInternalAddress_EvenWhenItIsAllowed()
    {
        var handler = new RecordingConnectorHandler(_ => RecordingConnectorHandler.Json("{}"));

        var outcome = await Run(
            handler,
            ConnectorTestData.Inputs(("url", "https://169.254.169.254/latest/meta-data")),
            options => options.Http.AllowedHosts = ["169.254.169.254"]);

        outcome.Should().BeOfType<ConnectorOutcome.BpmnError>()
            .Which.ErrorCode.Should().Be(HttpConnector.NotAllowedErrorCode);
        handler.Request.Should().BeNull();
    }

    // Testzweck: Ein Serverfehler ist technisch. Er wird mit der konfigurierten Wartezeit
    // gemeldet, damit derselbe Auftrag es spaeter noch einmal versuchen kann.
    [Test]
    public async Task Execute_ShouldFailWithBackoff_OnAServerError()
    {
        var handler = new RecordingConnectorHandler(_ =>
            RecordingConnectorHandler.Text("boom", HttpStatusCode.InternalServerError));

        var outcome = await Run(handler, ConnectorTestData.Inputs(("url", ConnectorTestData.AllowedUrl)));

        var failed = outcome.Should().BeOfType<ConnectorOutcome.Failed>().Subject;
        failed.Backoff.Should().Be(TimeSpan.FromSeconds(30));
        failed.Message.Should().Contain("500");
    }

    // Testzweck: Ein Zeitablauf ist ebenfalls technisch; der Auftrag bleibt wiederholbar.
    [Test]
    public async Task Execute_ShouldFail_OnATimeout()
    {
        var outcome = await Run(
            new NeverAnsweringHandler(),
            ConnectorTestData.Inputs(("url", ConnectorTestData.AllowedUrl), ("timeoutSeconds", 1)));

        outcome.Should().BeOfType<ConnectorOutcome.Failed>()
            .Which.Message.Should().Contain("timed out");
    }

    // Testzweck: Ein 4xx ist eine Antwort, kein Ausfall. Mit `errorOn4xx` wird daraus ein
    // BPMN-Fehler `HTTP_404`, den ein Error-Boundary am Service-Task fangen kann.
    [Test]
    public async Task Execute_ShouldThrowABpmnError_ForAClientErrorWhenConfigured()
    {
        var handler = new RecordingConnectorHandler(_ =>
            RecordingConnectorHandler.Text("nicht gefunden", HttpStatusCode.NotFound));

        var outcome = await Run(handler, ConnectorTestData.Inputs(("url", ConnectorTestData.AllowedUrl)));

        var error = outcome.Should().BeOfType<ConnectorOutcome.BpmnError>().Subject;
        error.ErrorCode.Should().Be("HTTP_404");
        error.ErrorMessage.Should().Contain("404");
        var values = ConnectorTestData.Read(error.Variables);
        values["status"].Should().Be(404);
        values["body"].Should().Be("nicht gefunden");
    }

    // Testzweck: Wer `errorOn4xx` abschaltet, will den Status selbst bewerten; dann ist auch
    // ein 404 ein regulaeres Ergebnis.
    [Test]
    public async Task Execute_ShouldCompleteOnAClientError_WhenErrorOn4xxIsOff()
    {
        var handler = new RecordingConnectorHandler(_ =>
            RecordingConnectorHandler.Text("nicht gefunden", HttpStatusCode.NotFound));

        var outcome = await Run(handler, ConnectorTestData.Inputs(
            ("url", ConnectorTestData.AllowedUrl),
            ("errorOn4xx", false)));

        var completed = outcome.Should().BeOfType<ConnectorOutcome.Completed>().Subject;
        ConnectorTestData.Read(completed.Variables)["status"].Should().Be(404);
    }

    // Testzweck: Die Antwortgroesse bestimmt das fremde System, der Speicherbedarf aber nicht.
    // Ueber der Grenze wird gekuerzt — und gekuerztes JSON kommt bewusst als Text zurueck,
    // statt als halbes Objekt.
    [Test]
    public async Task Execute_ShouldTruncateAResponseBeyondTheConfiguredLimit()
    {
        var oversized = new string('x', 5000);
        var handler = new RecordingConnectorHandler(_ =>
            RecordingConnectorHandler.Json($$"""{ "text": "{{oversized}}" }"""));

        var outcome = await Run(
            handler,
            ConnectorTestData.Inputs(("url", ConnectorTestData.AllowedUrl)),
            options => options.Http.MaxResponseBytes = 2048);

        var completed = outcome.Should().BeOfType<ConnectorOutcome.Completed>().Subject;
        var body = ConnectorTestData.Read(completed.Variables)["body"].Should().BeOfType<string>().Subject;
        Encoding.UTF8.GetByteCount(body).Should().Be(2048);
    }

    private static Task<ConnectorOutcome> Run(
        HttpMessageHandler handler,
        Variables inputs,
        Action<FlowzerConnectorOptions>? configure = null) =>
        Run(handler, ConnectorTestData.Job(HttpConnectorOptions.JobType, inputs), configure);

    private static Task<ConnectorOutcome> Run(
        HttpMessageHandler handler,
        ServiceTaskJob job,
        Action<FlowzerConnectorOptions>? configure = null)
    {
        var options = new FlowzerConnectorOptions
        {
            Http = new HttpConnectorOptions
            {
                Enabled = true,
                AllowedHosts = [ConnectorTestData.AllowedHost]
            }
        };
        configure?.Invoke(options);

        var connector = new HttpConnector(
            new SingleHandlerHttpClientFactory(handler),
            options,
            new ConnectorSecretResolver(options),
            NullLogger<HttpConnector>.Instance);

        return connector.Execute(job, CancellationToken.None);
    }
}
