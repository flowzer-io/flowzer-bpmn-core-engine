using System.Globalization;
using System.Xml;

namespace FlowzerDmn.Evaluation;

/// <summary>
/// Setzt einen FEEL-Wert in den .NET-Typ um, den das <c>typeRef</c> der Spalte nennt.
/// </summary>
/// <remarks>
/// <para>
/// Die Umsetzung ist bewusst nachsichtig: Passt der Wert nicht zum Typ — oder kennt die
/// Bibliothek das <c>typeRef</c> nicht —, bleibt der Wert unveraendert. Eine
/// Entscheidungstabelle soll nicht daran scheitern, dass jemand ein Feld anders
/// deklariert hat, als die FEEL-Engine es liefert.
/// </para>
/// <para>
/// Nicht umgesetzt wird <c>yearMonthDuration</c>: .NET hat keinen Typ fuer Jahre und
/// Monate, und <see cref="TimeSpan"/> waere eine Luege. Der Wert bleibt roh.
/// </para>
/// </remarks>
public static class DmnTypeConverter
{
    /// <summary>
    /// Setzt <paramref name="value"/> gemaess <paramref name="typeRef"/> um.
    /// </summary>
    /// <param name="typeRef">Die Typangabe, mit oder ohne Praefix (<c>feel:string</c>, <c>xs:date</c>).</param>
    /// <param name="value">Der von FEEL gelieferte Wert.</param>
    /// <returns>Der umgesetzte Wert, oder der Wert selbst, wenn die Umsetzung nicht traegt.</returns>
    public static object? Convert(string? typeRef, object? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(typeRef))
        {
            return value;
        }

        var localName = typeRef[(typeRef.LastIndexOf(':') + 1)..].Trim().ToLowerInvariant();

        try
        {
            return localName switch
            {
                "string" => value as string ?? System.Convert.ToString(value, CultureInfo.InvariantCulture),
                "number" or "double" => System.Convert.ToDouble(value, CultureInfo.InvariantCulture),
                "integer" or "int" => System.Convert.ToInt32(value, CultureInfo.InvariantCulture),
                "long" => System.Convert.ToInt64(value, CultureInfo.InvariantCulture),
                "boolean" => ToBoolean(value),
                "date" => ToDateOnly(value),
                "time" => ToTimeOnly(value),
                "datetime" => ToDateTime(value),
                "daytimeduration" => ToDuration(value),
                _ => value
            };
        }
        catch (Exception exception) when (exception is FormatException
                                              or InvalidCastException
                                              or OverflowException
                                              or ArgumentException)
        {
            // Ein Wert, der nicht zum deklarierten Typ passt, bleibt roh. Der Aufrufer sieht
            // dann, was FEEL geliefert hat, statt eine Ausnahme fuer eine Nebensache.
            return value;
        }
    }

    private static object ToBoolean(object value) => value switch
    {
        bool boolean => boolean,
        string text => bool.Parse(text),
        _ => System.Convert.ToBoolean(value, CultureInfo.InvariantCulture)
    };

    private static object ToDateOnly(object value) => value switch
    {
        DateOnly date => date,
        DateTime dateTime => DateOnly.FromDateTime(dateTime),
        DateTimeOffset offset => DateOnly.FromDateTime(offset.DateTime),
        _ => DateOnly.Parse(Textual(value), CultureInfo.InvariantCulture)
    };

    private static object ToTimeOnly(object value) => value switch
    {
        TimeOnly time => time,
        DateTime dateTime => TimeOnly.FromDateTime(dateTime),
        DateTimeOffset offset => TimeOnly.FromDateTime(offset.DateTime),
        _ => TimeOnly.Parse(Textual(value), CultureInfo.InvariantCulture)
    };

    private static object ToDateTime(object value) => value switch
    {
        DateTime dateTime => dateTime,
        DateTimeOffset offset => offset.DateTime,
        DateOnly date => date.ToDateTime(TimeOnly.MinValue),
        _ => DateTime.Parse(Textual(value), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };

    private static object ToDuration(object value) => value switch
    {
        TimeSpan span => span,
        // FEEL schreibt Dauern als ISO-8601, zum Beispiel PT2H30M.
        _ => XmlConvert.ToTimeSpan(Textual(value))
    };

    private static string Textual(object value) =>
        System.Convert.ToString(value, CultureInfo.InvariantCulture)
        ?? throw new FormatException("Der Wert laesst sich nicht als Text lesen.");
}
