using Newtonsoft.Json;

namespace core_engine;

public partial class InstanceEngine
{
    private readonly List<Message> _outgoingMessages = [];

    /// <summary>
    /// Die Nachrichten, die diese Instanz seit dem letzten Abholen ausgesendet hat.
    ///
    /// Die Engine kennt keine anderen Instanzen und stellt sie deshalb nur bereit; zustellen
    /// muss der Aufrufer (in der Web-API die Geschäftslogik, in derselben Transaktion wie das
    /// Speichern der Instanz). Die Liste ist bewusst flüchtig: Sie gehört zu genau einem
    /// Engine-Lauf und wird nicht mit dem Tokenstand persistiert. Findet sich kein Empfänger,
    /// verfällt die Nachricht — BPMN puffert sie nicht.
    /// </summary>
    public IReadOnlyList<Message> OutgoingMessages => _outgoingMessages;

    /// <summary>
    /// Gibt die ausgehenden Nachrichten heraus und leert die Liste. So kann der Zustellpfad
    /// eine Nachricht nicht zweimal abarbeiten, auch wenn die Zustellung dieselbe Instanz
    /// erneut weiterlaufen lässt.
    /// </summary>
    public IReadOnlyList<Message> TakeOutgoingMessages()
    {
        var messages = _outgoingMessages.ToArray();
        _outgoingMessages.Clear();

        return messages;
    }

    /// <summary>
    /// Sammelt die Nachricht eines sendenden Elements ein.
    ///
    /// Der Korrelationsschlüssel steht bereits ausgewertet an der Nachrichtendefinition: Jeder
    /// Token löst seinen FlowNode beim Anlegen gegen die Prozessvariablen auf — derselbe Weg,
    /// über den auch die fangende Seite ihren Schlüssel bekommt. Als Variablen wandern
    /// ausschließlich die Eingabewerte des Elements nach <c>zeebe:ioMapping</c> mit; ohne
    /// Zuordnung sendet Flowzer bewusst nichts statt des ganzen Prozesskontextes.
    /// </summary>
    internal void ThrowMessage(Token token, MessageDefinition messageDefinition)
    {
        _outgoingMessages.Add(new Message
        {
            Name = messageDefinition.Name,
            CorrelationKey = messageDefinition.FlowzerCorrelationKey,
            Variables = JsonConvert.SerializeObject(token.Variables ?? new Variables())
        });
    }
}
