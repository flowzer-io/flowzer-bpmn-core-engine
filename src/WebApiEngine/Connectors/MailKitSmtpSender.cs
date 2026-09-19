using System.Net.Sockets;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace WebApiEngine.Connectors;

/// <summary>
/// Versand ueber MailKit. Bewusst nicht <c>System.Net.Mail.SmtpClient</c>: Der ist als
/// veraltet gekennzeichnet und behandelt moderne TLS-Aushandlung und Fehlermeldungen des
/// Servers nicht zuverlaessig genug fuer einen unbeaufsichtigten Dienst.
///
/// Das Passwort wird erst hier aufgeloest und verlaesst die Methode nicht.
/// </summary>
public sealed class MailKitSmtpSender(
    FlowzerConnectorOptions options,
    ConnectorSecretResolver secretResolver) : ISmtpSender
{
    public async Task<SmtpSendResult> Send(MimeMessage message, CancellationToken cancellationToken)
    {
        var smtp = options.Email.Smtp;
        using var client = new SmtpClient
        {
            Timeout = smtp.ResolvedTimeoutSeconds * 1000
        };

        try
        {
            // Ohne STARTTLS bleibt die Verbindung unverschluesselt; das ist eine bewusste
            // Betriebsentscheidung fuer ein Relay im eigenen Netz, kein Standard.
            await client.ConnectAsync(
                smtp.Host,
                smtp.Port,
                smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(smtp.Username))
            {
                var password = secretResolver.ResolveByName(smtp.PasswordSecretName);
                if (password is null)
                {
                    throw new SmtpDeliveryException("The configured SMTP password secret could not be resolved.");
                }

                await client.AuthenticateAsync(smtp.Username, password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception exception) when (exception
            is SmtpCommandException
            or SmtpProtocolException
            or SslHandshakeException
            or AuthenticationException
            or SocketException
            or IOException)
        {
            // Die Meldung des Servers kann den Empfaenger nennen, nie aber das Passwort:
            // Es wird oben nur an MailKit uebergeben und steht in keiner Ausnahme.
            throw new SmtpDeliveryException(exception.Message, exception);
        }

        return new SmtpSendResult(
            message.MessageId ?? string.Empty,
            message.To.Concat(message.Cc).Concat(message.Bcc)
                .OfType<MailboxAddress>()
                .Select(address => address.Address)
                .ToList());
    }
}
