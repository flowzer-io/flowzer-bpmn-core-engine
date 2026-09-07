using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace StorageSystem;

/// <summary>
/// Die eine Regel fuer Katalog-Kennungen (<c>DefinitionId</c>).
///
/// Eine Kennung kommt aus dem Request (<c>POST</c>/<c>PUT /definition/meta</c>) oder aus dem
/// hochgeladenen BPMN-XML (<c>definitions/@id</c>) und wird in der Dateiablage zum Dateinamen.
/// Ohne Pruefung zeigte eine Kennung wie <c>../../x</c> aus dem Ablageordner heraus; das
/// Speichern und das Loeschen traefen dann eine fremde Datei (CodeQL <c>cs/path-injection</c>).
///
/// Bewusst kein enges Whitelist-Muster: Im Bestand stehen BPMN-Ids (<c>Process_1</c>,
/// <c>Definitions_0abc</c>), sprechende Kennungen aus den Beispielen
/// (<c>flowzer-urlaubsantrag</c>), von der Konsole erzeugte Kennungen
/// (<c>definition_&lt;Guid&gt;</c>) und aelteres, von Hand Vergebenes mit Leerzeichen oder
/// Umlauten. Ein Muster wie „nur Buchstaben, Ziffern, Strich und Unterstrich“ machte solche
/// Kataloge unbenutzbar — und zwar nicht nur beim Anlegen, sondern auch beim Aendern und
/// Loeschen vorhandener Eintraege. Verboten ist deshalb genau das, was einen Dateinamen
/// verlaesst oder ihn unbrauchbar macht.
/// </summary>
public static class DefinitionIdRules
{
    /// <summary>
    /// Obergrenze fuer die Laenge. Sie liegt weit ueber jeder sinnvollen Kennung und weit unter
    /// der Namenslaenge, die ein Dateisystem noch traegt (ueblich sind 255 Bytes; eine Kennung
    /// bekommt in der Ablage noch Praefix, Instanzkennung und Endung dazu).
    /// </summary>
    public const int MaxLength = 200;

    /// <summary>
    /// Prueft eine Kennung. Abgelehnt werden: leer oder nur Leerraum, laenger als
    /// <see cref="MaxLength"/>, die Verzeichnistrenner <c>/</c> und <c>\</c>, die Kennungen
    /// <c>.</c> und <c>..</c> sowie Steuerzeichen (darunter das Nullzeichen, das einen
    /// Dateinamen auf Betriebssystemebene abschneidet).
    /// </summary>
    public static bool IsValid([NotNullWhen(true)] string? definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId) || definitionId.Length > MaxLength)
        {
            return false;
        }

        // Ohne Trennzeichen gibt es nur ein Segment; "." und ".." als ganze Kennung sind
        // deshalb die einzigen Pfadangaben, die noch zu pruefen bleiben.
        if (definitionId is "." or "..")
        {
            return false;
        }

        foreach (var character in definitionId)
        {
            if (character is '/' or '\\' || char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Die Meldung fuer eine abgelehnte Kennung. Sie nennt den abgelehnten Wert, damit in der
    /// Oberflaeche erkennbar ist, welche Kennung gemeint ist.
    /// </summary>
    public static string BuildErrorMessage(string? definitionId) =>
        $"\"{definitionId}\" is not a valid definition id. It must not be empty, must not be " +
        $"\".\" or \"..\", must not contain \"/\", \"\\\" or control characters and must not be " +
        $"longer than {MaxLength} characters.";

    /// <summary>
    /// Waechter fuer Stellen ohne eigene Antwort. Die <see cref="ArgumentException"/> beantwortet
    /// die API mit 400 — dieselbe Antwort wie bei der Pruefung im Controller.
    /// </summary>
    public static void EnsureValid(
        [NotNull] string? definitionId,
        [CallerArgumentExpression(nameof(definitionId))] string? parameterName = null)
    {
        if (!IsValid(definitionId))
        {
            throw new ArgumentException(BuildErrorMessage(definitionId), parameterName);
        }
    }
}
