using MimeKit;

namespace WebApiEngine.Connectors;

/// <summary>
/// Mitgelieferter Worker fuer E-Mail (<c>flowzer:email</c>).
///
/// Der Absender kommt aus der Konfiguration, nicht aus dem Modell: Sonst entschiede ein
/// Workflow, in wessen Namen das Haus schreibt. Die Freigabeliste der Empfaengerdomaenen ist
/// absichtlich leer voreingestellt — ein Testsystem mit echten Vorgangsdaten soll nicht
/// versehentlich nach aussen mailen.
/// </summary>
public sealed class EmailConnector(
    ISmtpSender smtpSender,
    FlowzerConnectorOptions options) : IBuiltInConnector
{
    public const string InvalidRequestErrorCode = "EMAIL_INVALID_REQUEST";
    public const string NotAllowedErrorCode = "EMAIL_NOT_ALLOWED";

    public string JobType => EmailConnectorOptions.JobType;

    public string Name => "email";

    public bool Enabled => options.Email.Enabled;

    public async Task<ConnectorOutcome> Execute(ServiceTaskJob job, CancellationToken cancellationToken)
    {
        var settings = options.Email;
        var inputs = ConnectorValues.Read(job.Variables);

        var to = ConnectorValues.AsTextList(ConnectorValues.Get(inputs, "to"));
        var cc = ConnectorValues.AsTextList(ConnectorValues.Get(inputs, "cc"));
        var bcc = ConnectorValues.AsTextList(ConnectorValues.Get(inputs, "bcc"));
        var subject = ConnectorValues.AsString(ConnectorValues.Get(inputs, "subject"));
        var text = ConnectorValues.AsString(ConnectorValues.Get(inputs, "text"));
        var html = ConnectorValues.AsString(ConnectorValues.Get(inputs, "html"));

        if (to.Count == 0)
        {
            return Invalid("At least one recipient in 'to' is required.");
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            return Invalid("A non-empty 'subject' is required.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Invalid("A non-empty 'text' is required.");
        }

        var message = new MimeMessage();
        if (!TryAdd(message.From, settings.From))
        {
            return Invalid($"The configured sender address '{settings.From}' is not a valid mailbox.");
        }

        foreach (var (field, addresses) in new[] { (message.To, to), (message.Cc, cc), (message.Bcc, bcc) })
        {
            foreach (var address in addresses)
            {
                if (!TryAdd(field, address))
                {
                    return Invalid($"'{address}' is not a valid mailbox.");
                }
            }
        }

        var recipients = message.To.Concat(message.Cc).Concat(message.Bcc)
            .OfType<MailboxAddress>()
            .ToList();
        var refused = recipients.FirstOrDefault(address => !IsAllowedRecipient(address.Address, settings));
        if (refused is not null)
        {
            return new ConnectorOutcome.BpmnError(
                NotAllowedErrorCode,
                settings.ResolvedAllowedRecipientDomains.Length == 0
                    ? "No recipient domains are configured; ask the operator to allow the domain."
                    : $"Recipient domain of '{refused.Address}' is not allowed.",
                ConnectorValues.Build(("recipient", refused.Address)));
        }

        // Die Kennung hier setzen, nicht erst im Versandweg: Sie geht als `messageId` in den
        // Prozess zurueck und soll auch dann feststehen, wenn der Server keine eigene meldet.
        message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId();
        message.Subject = subject;
        message.Body = new BodyBuilder
        {
            TextBody = text,
            HtmlBody = string.IsNullOrWhiteSpace(html) ? null : html
        }.ToMessageBody();

        try
        {
            var result = await smtpSender.Send(message, cancellationToken);
            return new ConnectorOutcome.Completed(ConnectorValues.Build(
                ("messageId", result.MessageId),
                ("acceptedRecipients", result.AcceptedRecipients.ToList<object?>())));
        }
        catch (SmtpDeliveryException exception)
        {
            return new ConnectorOutcome.Failed(
                $"SMTP delivery failed: {exception.Message}",
                settings.ResolvedRetryBackoff);
        }
    }

    private static bool TryAdd(InternetAddressList field, string address)
    {
        if (!MailboxAddress.TryParse(address, out var mailbox))
        {
            return false;
        }

        field.Add(mailbox);
        return true;
    }

    /// <summary>
    /// Leere Freigabeliste heisst "kein Versand". Das ist der sichere Ausgang: Eine
    /// Installation ohne ausdrueckliche Angabe soll nichts nach aussen schicken.
    /// </summary>
    private static bool IsAllowedRecipient(string address, EmailConnectorOptions settings)
    {
        var allowedDomains = settings.ResolvedAllowedRecipientDomains;
        if (allowedDomains.Length == 0)
        {
            return false;
        }

        var separator = address.LastIndexOf('@');
        if (separator < 0 || separator == address.Length - 1)
        {
            return false;
        }

        var domain = address[(separator + 1)..];
        return allowedDomains.Any(pattern =>
            string.Equals(pattern, domain, StringComparison.OrdinalIgnoreCase)
            || (pattern.StartsWith("*.", StringComparison.Ordinal)
                && domain.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase)));
    }

    private static ConnectorOutcome Invalid(string message) =>
        new ConnectorOutcome.BpmnError(InvalidRequestErrorCode, message, null);
}
