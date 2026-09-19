using FlowzerDmn.Evaluation;
using FluentAssertions;

namespace FlowzerDmn.Tests;

/// <summary>
/// Die Umsetzung der FEEL-Werte in .NET-Typen. Sie wird hier ohne FEEL geprueft, weil es
/// genau darum geht, was die Bibliothek aus einem gegebenen Wert macht.
/// </summary>
public class DmnTypeConverterTest
{
    // Testzweck: Die Zahlentypen von DMN landen in den erwarteten .NET-Typen. V8 liefert je
    // nach Zahl Int32 oder Single; erst das typeRef macht daraus etwas Verlaessliches.
    [Test]
    public void Convert_ShouldMapTheNumericTypes()
    {
        DmnTypeConverter.Convert("number", 3).Should().BeOfType<double>().And.Be(3d);
        DmnTypeConverter.Convert("double", 1.5f).Should().BeOfType<double>().And.Be(1.5d);
        DmnTypeConverter.Convert("integer", 3.0).Should().BeOfType<int>().And.Be(3);
        DmnTypeConverter.Convert("long", 3).Should().BeOfType<long>().And.Be(3L);
    }

    // Testzweck: Text und Wahrheitswert werden ebenfalls umgesetzt, auch wenn FEEL sie
    // anders verpackt zurueckgibt.
    [Test]
    public void Convert_ShouldMapStringsAndBooleans()
    {
        DmnTypeConverter.Convert("string", 42).Should().Be("42");
        DmnTypeConverter.Convert("string", "schon Text").Should().Be("schon Text");
        DmnTypeConverter.Convert("boolean", true).Should().Be(true);
        DmnTypeConverter.Convert("boolean", "true").Should().Be(true);
    }

    // Testzweck: Die Datums- und Zeittypen werden aus der ISO-Schreibweise gelesen, in der
    // FEEL sie ausgibt.
    [Test]
    public void Convert_ShouldMapTheDateAndTimeTypes()
    {
        DmnTypeConverter.Convert("date", "2026-09-19").Should().Be(new DateOnly(2026, 9, 19));
        DmnTypeConverter.Convert("time", "14:30:00").Should().Be(new TimeOnly(14, 30));
        DmnTypeConverter.Convert("dateTime", "2026-09-19T14:30:00")
            .Should().Be(new DateTime(2026, 9, 19, 14, 30, 0, DateTimeKind.Unspecified));
        DmnTypeConverter.Convert("dayTimeDuration", "PT2H30M").Should().Be(TimeSpan.FromMinutes(150));
    }

    // Testzweck: Ein Praefix am typeRef stoert nicht — Editoren schreiben mal "string",
    // mal "feel:string", mal "xs:string".
    [Test]
    public void Convert_ShouldIgnoreAPrefixOnTheTypeRef()
    {
        DmnTypeConverter.Convert("feel:number", 3).Should().Be(3d);
        DmnTypeConverter.Convert("xs:string", 3).Should().Be("3");
    }

    // Testzweck: yearMonthDuration bleibt roh. .NET hat keinen Typ fuer Jahre und Monate,
    // und ein TimeSpan waere eine Luege ueber die Laenge eines Monats.
    [Test]
    public void Convert_ShouldLeaveYearMonthDurationAsItIs()
    {
        DmnTypeConverter.Convert("yearMonthDuration", "P1Y2M").Should().Be("P1Y2M");
    }

    // Testzweck: Ein unbekanntes typeRef und ein fehlendes typeRef lassen den Wert unberuehrt.
    [Test]
    public void Convert_ShouldLeaveUnknownOrMissingTypeRefsAlone()
    {
        DmnTypeConverter.Convert("meinTyp", "roh").Should().Be("roh");
        DmnTypeConverter.Convert(null, "roh").Should().Be("roh");
        DmnTypeConverter.Convert("", "roh").Should().Be("roh");
    }

    // Testzweck: Passt der Wert nicht zum deklarierten Typ, bleibt er roh, statt die ganze
    // Auswertung an einer Nebensache scheitern zu lassen.
    [Test]
    public void Convert_ShouldLeaveAValueThatDoesNotFitTheTypeAlone()
    {
        DmnTypeConverter.Convert("number", "keine Zahl").Should().Be("keine Zahl");
        DmnTypeConverter.Convert("date", "kein Datum").Should().Be("kein Datum");
        DmnTypeConverter.Convert("dayTimeDuration", "keine Dauer").Should().Be("keine Dauer");
    }

    // Testzweck: null bleibt null, unabhaengig vom typeRef. Eine leere Ausgabespalte darf
    // nicht zu 0 oder "" werden.
    [Test]
    public void Convert_ShouldLeaveNullAlone()
    {
        DmnTypeConverter.Convert("number", null).Should().BeNull();
        DmnTypeConverter.Convert("string", null).Should().BeNull();
    }
}
