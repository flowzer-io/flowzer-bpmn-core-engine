using System.Text.Json;
using FluentAssertions;
using WebApiEngine.Shared;

namespace WebApiEngine.Tests;

/// <summary>
/// Was ein Worker zurueckmeldet, muss als gewoehnlicher Wert in den Prozessvariablen landen.
/// Ohne den Konverter stehen dort <c>JsonElement</c>-Huellen: Die Ablage speichert davon nur
/// noch <c>{"ValueKind": 3}</c>, der eigentliche Wert ist fort, und jede spaetere Bedingung
/// auf diese Variable ist falsch — der Prozess nimmt still den Standardfluss.
/// </summary>
public class CompleteJobRequestDtoTest
{
    // Testzweck: Prueft, dass Zeichenketten, Zahlen und Wahrheitswerte aus der Rueckmeldung als
    // gewoehnliche CLR-Werte ankommen und nicht als JsonElement.
    [Test]
    public void Variables_ShouldBeDeserialisedAsPlainValues()
    {
        const string json = """
            {
              "workerId": "urlaub-worker-1",
              "variables": {
                "vertretungFrei": "ja",
                "geprueftePersonen": 3,
                "istVollstaendig": true
              }
            }
            """;

        var request = JsonSerializer.Deserialize<CompleteJobRequestDto>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        request.Should().NotBeNull();
        var variables = (IDictionary<string, object?>)request!.Variables!;

        variables["vertretungFrei"].Should().Be("ja");
        variables["istVollstaendig"].Should().Be(true);
        variables["geprueftePersonen"].Should().NotBeNull();
        variables["geprueftePersonen"]!.ToString().Should().Be("3");

        variables.Values.Should().NotContain(value => value is JsonElement,
            "JsonElement-Werte ueberleben die Ablage nicht");
    }

    // Testzweck: Prueft, dass eine Rueckmeldung ohne Variablen zulaessig bleibt — nicht jeder
    // Service-Task liefert ein Ergebnis zurueck.
    [Test]
    public void Variables_MayBeOmitted()
    {
        var request = JsonSerializer.Deserialize<CompleteJobRequestDto>(
            """{ "workerId": "urlaub-worker-1" }""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        request.Should().NotBeNull();
        request!.Variables.Should().BeNull();
    }
}
