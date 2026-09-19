using FluentAssertions;
using MimeKit;
using WebApiEngine.Connectors;
using Variables = System.Dynamic.ExpandoObject;

namespace WebApiEngine.Tests;

/// <summary>
/// Der mitgelieferte E-Mail-Konnektor. Der Absender kommt aus der Konfiguration, nicht aus dem
/// Modell, und ohne freigegebene Empfaengerdomaene verlaesst nichts das Haus.
/// </summary>
[NonParallelizable]
public class EmailConnectorTest
{
    // Testzweck: Der Erfolgsfall versendet mit konfiguriertem Absender und meldet Kennung und
    // angenommene Empfaenger in den Prozess zurueck.
    [Test]
    public async Task Execute_ShouldSendTheMessageAndReturnMessageIdAndRecipients()
    {
        var sender = new FakeSmtpSender();

        var outcome = await Run(sender, ConnectorTestData.Inputs(
            ("to", "empfaenger@maass.it"),
            ("cc", new List<object?> { "kopie@maass.it" }),
            ("subject", "Urlaubsantrag"),
            ("text", "Bitte pruefen."),
            ("html", "<p>Bitte pruefen.</p>")));

        var completed = outcome.Should().BeOfType<ConnectorOutcome.Completed>().Subject;
        var values = ConnectorTestData.Read(completed.Variables);
        values["messageId"].Should().BeOfType<string>().Which.Should().NotBeEmpty();
        values["acceptedRecipients"].Should().BeEquivalentTo(new object?[]
        {
            "empfaenger@maass.it", "kopie@maass.it"
        });

        var message = sender.Sent.Should().ContainSingle().Subject;
        message.From.Mailboxes.Single().Address.Should().Be("flowzer@maass.it");
        message.Subject.Should().Be("Urlaubsantrag");
        message.HtmlBody.Should().Contain("Bitte pruefen.");
    }

    // Testzweck: Eine Empfaengerdomaene ausserhalb der Freigabe ist ein fachlicher Ausgang mit
    // eigenem Weg im Modell — und die Nachricht geht nicht hinaus.
    [Test]
    public async Task Execute_ShouldThrowABpmnError_ForARecipientOutsideTheAllowList()
    {
        var sender = new FakeSmtpSender();

        var outcome = await Run(sender, ConnectorTestData.Inputs(
            ("to", "jemand@fremde.example"),
            ("subject", "Urlaubsantrag"),
            ("text", "Bitte pruefen.")));

        outcome.Should().BeOfType<ConnectorOutcome.BpmnError>()
            .Which.ErrorCode.Should().Be(EmailConnector.NotAllowedErrorCode);
        sender.Sent.Should().BeEmpty();
    }

    // Testzweck: Eine leere Freigabeliste heisst ausdruecklich "kein Versand". Ein Testsystem
    // mit echten Vorgangsdaten soll nicht versehentlich nach aussen mailen.
    [Test]
    public async Task Execute_ShouldSendNothing_WhenNoRecipientDomainIsAllowed()
    {
        var sender = new FakeSmtpSender();

        var outcome = await Run(
            sender,
            ConnectorTestData.Inputs(
                ("to", "empfaenger@maass.it"),
                ("subject", "Urlaubsantrag"),
                ("text", "Bitte pruefen.")),
            options => options.Email.AllowedRecipientDomains = []);

        outcome.Should().BeOfType<ConnectorOutcome.BpmnError>()
            .Which.ErrorCode.Should().Be(EmailConnector.NotAllowedErrorCode);
        sender.Sent.Should().BeEmpty();
    }

    // Testzweck: Fehlt eine Pflichtangabe, ist die Anfrage unvollstaendig. Ein zweiter Versuch
    // aenderte daran nichts, also wird sie nicht wiederholt, sondern fachlich gemeldet.
    [TestCase("to")]
    [TestCase("subject")]
    [TestCase("text")]
    public async Task Execute_ShouldThrowABpmnError_WhenAMandatoryFieldIsMissing(string missing)
    {
        var sender = new FakeSmtpSender();
        var inputs = ConnectorTestData.Inputs(
            ("to", "empfaenger@maass.it"),
            ("subject", "Urlaubsantrag"),
            ("text", "Bitte pruefen."));
        ((IDictionary<string, object?>)inputs).Remove(missing);

        var outcome = await Run(sender, inputs);

        outcome.Should().BeOfType<ConnectorOutcome.BpmnError>()
            .Which.ErrorCode.Should().Be(EmailConnector.InvalidRequestErrorCode);
        sender.Sent.Should().BeEmpty();
    }

    // Testzweck: Ein nicht erreichbarer SMTP-Server ist technisch. Der Auftrag wartet die
    // konfigurierte Zeit und wird dann erneut vergeben.
    [Test]
    public async Task Execute_ShouldFailWithBackoff_WhenTheServerCannotBeReached()
    {
        var sender = new FakeSmtpSender(new SmtpDeliveryException("Connection refused"));

        var outcome = await Run(sender, ConnectorTestData.Inputs(
            ("to", "empfaenger@maass.it"),
            ("subject", "Urlaubsantrag"),
            ("text", "Bitte pruefen.")));

        var failed = outcome.Should().BeOfType<ConnectorOutcome.Failed>().Subject;
        failed.Backoff.Should().Be(TimeSpan.FromSeconds(30));
        failed.Message.Should().Contain("Connection refused");
    }

    // Testzweck: Eine unbrauchbare Empfaengerangabe ist ein fachlicher Ausgang und kein
    // Netzproblem; sie darf nicht endlos wiederholt werden, und es geht nichts hinaus.
    [TestCase("kein-postfach")]
    [TestCase("<<>>")]
    public async Task Execute_ShouldNotSend_ForAnUnusableRecipient(string recipient)
    {
        var sender = new FakeSmtpSender();

        var outcome = await Run(sender, ConnectorTestData.Inputs(
            ("to", recipient),
            ("subject", "Urlaubsantrag"),
            ("text", "Bitte pruefen.")));

        outcome.Should().BeOfType<ConnectorOutcome.BpmnError>();
        sender.Sent.Should().BeEmpty();
    }

    private static Task<ConnectorOutcome> Run(
        ISmtpSender sender,
        Variables inputs,
        Action<FlowzerConnectorOptions>? configure = null)
    {
        var options = new FlowzerConnectorOptions
        {
            Email = new EmailConnectorOptions
            {
                Enabled = true,
                From = "flowzer@maass.it",
                AllowedRecipientDomains = ["maass.it"],
                Smtp = new SmtpOptions { Host = "smtp.maass.it" }
            }
        };
        configure?.Invoke(options);

        return new EmailConnector(sender, options).Execute(
            ConnectorTestData.Job(EmailConnectorOptions.JobType, inputs),
            CancellationToken.None);
    }
}
