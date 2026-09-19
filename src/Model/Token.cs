using BPMN.Foundation;

namespace Model;

public class Token
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid ProcessInstanceId { get; init; }

    public required IBaseElement CurrentBaseElement { get; init; }

    /// <summary>
    /// Dasselbe Element wie <see cref="CurrentBaseElement"/>, nur als Flow-Node gelesen.
    ///
    /// Ausdruecklich nicht persistiert: Ohne Setter wuerde Newtonsoft.Json die gespeicherte
    /// Kopie beim Laden nicht neu erzeugen, sondern in das bereits aufgebaute
    /// <see cref="CurrentBaseElement"/> hineinschreiben — und dabei jede Liste des
    /// Modellelements ein zweites Mal fuellen (Dokumentation, Kandidaten, Mappings).
    /// Der Wert ist ohnehin ableitbar; gespeichert wird deshalb nur das Element selbst.
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    public FlowNode? CurrentFlowNode => CurrentBaseElement as FlowNode;

    public required List<BoundaryEvent> ActiveBoundaryEvents { get; init; }
    private FlowNodeState _state = FlowNodeState.Ready;

    public FlowNodeState State
    {
        get => _state;
        set
        {
            _state = value;
            LastStateChangeTime = DateTime.UtcNow;
        }
    }

    // Der Setter ist nötig, damit der Startzeitpunkt beim Laden aus der Ablage erhalten
    // bleibt: Newtonsoft.Json überspringt schreibgeschützte Auto-Properties, wodurch
    // jeder Ladevorgang den Zeitstempel auf "jetzt" zurücksetzen würde.
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    // Der State-Setter schreibt LastStateChangeTime auf "jetzt". Beim Laden aus der Ablage muss
    // der persistierte Zeitstempel deshalb nach State gesetzt werden; die feste Reihenfolge macht
    // das unabhaengig von der Deklarationsreihenfolge und vom Erzeuger der JSON-Datei.
    [Newtonsoft.Json.JsonProperty(Order = 100)]
    public DateTime LastStateChangeTime { get; set; } = DateTime.UtcNow;
    public Token? PreviousToken { get; set; }
    public SequenceFlow? LastSequenceFlow { get; set; }

    public Variables? Variables { get; set; }
    public Variables? OutputData { get; set; }

    /// <summary>
    /// Serverseitig ermittelter Akteur des Aufgabenabschlusses, unabhängig von Formulardaten.
    /// Null bei alten Tokens oder internen Abschlüssen ohne Benutzer-/Workeridentität.
    /// </summary>
    public Guid? CompletedByUserId { get; set; }

    /// <summary>
    /// Nur am Master-Token: verifizierter Initiator des direkten Starts. Bleibt mit dem
    /// Tokenbestand bei jedem Speichern/Neuladen erhalten, ohne Variablen umzudeuten.
    /// Null für historische und technische Starts. Nicht Teil gewöhnlicher Token-DTOs.
    /// </summary>
    public AuthenticatedSubject? Initiator { get; set; }

    public Guid? ParentTokenId { get; init; }

    /// <summary>
    /// Nur an den Tokens, die ein ereignisbasiertes Gateway gleichzeitig scharf gestellt hat:
    /// die gemeinsame Kennung dieser Gruppe. Trifft eines der Ereignisse ein, zieht die Engine
    /// die uebrigen Mitglieder derselben Gruppe zurueck — genau so verschwinden auch deren
    /// Message-, Signal- und Timer-Subscriptions. Null an jedem anderen Token.
    /// </summary>
    public Guid? EventGroupId { get; set; }

    /// <summary>
    /// Nur an den Tokens, die ein inklusives Gateway als Split erzeugt hat: die gemeinsame
    /// Kennung dieser Verzweigung. Sie wandert mit dem Token durch seinen Zweig, damit der
    /// zugehoerige Join weiss, welche Tokens zusammengehoeren. Null an jedem anderen Token.
    /// </summary>
    public Guid? InclusiveForkId { get; set; }

    /// <summary>
    /// Zu <see cref="InclusiveForkId"/>: wie viele Zweige der Split aktiviert hat. Der Join
    /// wartet auf genau diese Anzahl und setzt die Merkzelle danach zurueck.
    /// </summary>
    public int? InclusiveForkSize { get; set; }

    /// <summary>
    /// Nur an einem Token, das an einer Call Activity wartet: die Instanz, die dieser Schritt
    /// gestartet hat. Sie ist zugleich die Merkfaehigkeit der Engine — ein Token mit gesetzter
    /// Kennung fordert keinen zweiten Aufruf mehr an, auch nicht nach einem Neuladen aus der
    /// Ablage. Null an jedem anderen Token und vor dem Start des Kindes.
    /// </summary>
    public Guid? CalledInstanceId { get; set; }

    /// <summary>
    /// Nur am Master-Token einer aufgerufenen Instanz: die Instanz, deren Call Activity sie
    /// gestartet hat. Steht wie <see cref="Initiator"/> am Master, damit die Herkunft jeden
    /// Speicher- und Ladevorgang ueberlebt, ohne in den Prozessvariablen zu landen.
    /// </summary>
    public Guid? CallingInstanceId { get; set; }

    /// <summary>
    /// Nur am Master-Token einer aufgerufenen Instanz: das wartende Token der aufrufenden
    /// Instanz, das mit dem Ende dieses Vorgangs weiterlaeuft.
    /// </summary>
    public Guid? CallingTokenId { get; set; }

    public override string ToString()
    {
        return $"{CurrentBaseElement.GetType()} " +
               (CurrentBaseElement.GetType().IsAssignableTo(typeof(FlowNode))
                   ? CurrentFlowNode?.Name
                   : CurrentBaseElement.Id)
               + $" ({State} + )";
    }
}
