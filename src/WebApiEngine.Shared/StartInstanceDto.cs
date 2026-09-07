using System.Dynamic;
using System.Text.Json.Serialization;

namespace WebApiEngine.Shared;

/// <summary>
/// Der Rumpf beim Start einer Instanz.
///
/// Er ist optional: Ein Workflow ohne Startformular startet wie bisher ohne Angaben. Traegt der
/// Workflow ein Startformular, sind <see cref="Variables"/> die ausgefuellten Werte — aus der
/// Konsole die Eingaben des Formulars, von aussen einfach ein JSON-Objekt.
/// </summary>
public class StartInstanceDto
{
    /// <summary>
    /// Die Startvariablen der Instanz. <c>null</c> heisst „keine Angabe gemacht" und ist etwas
    /// anderes als ein leeres Objekt: Bei einem Workflow mit Startformular wird der Start ohne
    /// Angabe abgelehnt, ein leeres Objekt dagegen angenommen.
    /// </summary>
    [JsonConverter(typeof(ExpandoObjectConverter))]
    public ExpandoObject? Variables { get; init; }
}
