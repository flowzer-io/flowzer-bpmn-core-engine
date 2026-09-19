using MimeKit;

namespace WebApiEngine.Connectors;

/// <summary>
/// Der Versandweg des E-Mail-Konnektors. Als eigene Abstraktion, damit Tests den Versand
/// ersetzen koennen, ohne einen SMTP-Server zu brauchen — und damit im Produktivpfad genau
/// eine Stelle das Passwort kennt.
/// </summary>
public interface ISmtpSender
{
    Task<SmtpSendResult> Send(MimeMessage message, CancellationToken cancellationToken);
}

/// <summary>Ergebnis eines angenommenen Versands.</summary>
public sealed record SmtpSendResult(string MessageId, IReadOnlyList<string> AcceptedRecipients);

/// <summary>
/// Der SMTP-Server hat die Nachricht nicht angenommen. Der Konnektor meldet das als
/// technischen Fehlschlag mit Wartezeit, nicht als fachlichen BPMN-Fehler: Ein zweiter
/// Versuch kann gelingen.
/// </summary>
public sealed class SmtpDeliveryException(string message, Exception? innerException = null)
    : Exception(message, innerException);
