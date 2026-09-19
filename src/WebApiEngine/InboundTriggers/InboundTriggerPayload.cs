using System.Dynamic;
using System.Globalization;
using System.Text.Json;
using Model;

namespace WebApiEngine.InboundTriggers;

/// <summary>
/// Was aus dem JSON-Körper eines Aufrufs wird: der Korrelationsschlüssel und die
/// Prozessvariablen.
///
/// Beides ist bewusst eng gefasst. Ein fremdes System schickt in der Regel seinen ganzen
/// Datensatz — Kundennummern, Freitexte, Anhänge als Text. Ohne Grenze läge das alles dauerhaft
/// in den Prozessvariablen und damit in jeder Sicht auf die Instanz.
/// </summary>
public static class InboundTriggerPayload
{
    /// <summary>Name der einen Variablen im Modus <c>body</c>.</summary>
    public const string BodyVariableName = "payload";

    /// <summary>Grenze für den Körper eines Aufrufs.</summary>
    public const int MaxBodyBytes = 256 * 1024;

    /// <summary>
    /// Liest den Wert eines Pfades in Punktnotation (<c>order.id</c>) als Zeichenkette.
    /// Liefert <c>null</c>, wenn der Pfad fehlt oder auf ein Objekt, ein Feld oder <c>null</c>
    /// zeigt: Ein Korrelationsschlüssel muss ein einzelner Wert sein.
    /// </summary>
    public static string? ReadPath(JsonElement root, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var current = root;
        foreach (var segment in path.Split('.'))
        {
            if (segment.Length == 0) return null;
            if (current.ValueKind != JsonValueKind.Object) return null;
            if (!current.TryGetProperty(segment, out var next)) return null;
            current = next;
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => bool.TrueString.ToLower(CultureInfo.InvariantCulture),
            JsonValueKind.False => bool.FalseString.ToLower(CultureInfo.InvariantCulture),
            _ => null
        };
    }

    /// <summary>
    /// Baut die Prozessvariablen nach dem Modus des Auslösers.
    ///
    /// Der Körper wird zuerst genauso in Prozesswerte übersetzt wie die Werte eines
    /// Startformulars; erst danach wird ausgewählt. Ein eigener Konverter würde dieselben Daten
    /// anders abbilden als ein Start über die Konsole — eine Zahl einmal als <c>long</c> und
    /// einmal als Text, und eine Bedingung im Modell träfe je nach Startweg anders zu.
    ///
    /// Im Modus <c>fields</c> ohne genannte Felder entsteht ein leeres Objekt, nicht
    /// <c>null</c>: Ein Workflow mit Startformular unterscheidet „keine Angabe gemacht" von
    /// „nichts angegeben".
    /// </summary>
    public static ExpandoObject Build(
        InboundTriggerVariablesMode mode,
        IReadOnlyCollection<string> allowedFields,
        string rawBody)
    {
        var body = Newtonsoft.Json.JsonConvert.DeserializeObject<ExpandoObject>(
                       rawBody, new Newtonsoft.Json.Converters.ExpandoObjectConverter())
                   ?? new ExpandoObject();

        if (mode == InboundTriggerVariablesMode.Body)
        {
            var wrapped = new ExpandoObject();
            ((IDictionary<string, object?>)wrapped)[BodyVariableName] = body;
            return wrapped;
        }

        var source = (IDictionary<string, object?>)body;
        var selected = new ExpandoObject();
        var target = (IDictionary<string, object?>)selected;
        foreach (var field in allowedFields)
        {
            if (source.TryGetValue(field, out var value)) target[field] = value;
        }

        return selected;
    }
}
