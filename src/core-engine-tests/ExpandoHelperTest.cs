using Flowzer.Shared;
using FluentAssertions;

namespace core_engine_tests;

public class ExpandoHelperTest
{
    // Testzweck: Normalisierte JSON-/Directory-Objekte werden nach ihren Datenkeys
    // umgesetzt, nicht nach CLR-Eigenschaften wie Count, Keys oder Values.
    [Test]
    public void DictionaryToDynamic_ShouldPreserveOnlyItsActualDataProperties()
    {
        var source = new Dictionary<string, object?> { ["kind"] = "user", ["id"] = "synthetic-id" };
        var result = (IDictionary<string, object?>)source.ToDynamic()!;
        result.Should().HaveCount(2);
        result.Keys.Should().BeEquivalentTo("kind", "id");
        result["kind"].Should().Be("user");
        result["id"].Should().Be("synthetic-id");
    }

    // Testzweck: Verschachtelte Objekte und Listen behalten jedes wirkliche Feld,
    // einschließlich unerlaubter Metadaten; Konvertierung ist keine Sicherheitsbereinigung.
    [Test]
    public void DictionaryToDynamic_ShouldPreserveNestedValuesAndLiteralMetadata()
    {
        var subject = new Dictionary<string, object?> { ["kind"] = "user", ["id"] = "synthetic-id", ["$type"] = "literal-data" };
        var source = new Dictionary<string, object?> { ["values"] = new List<object?> { subject, 3, null } };
        var result = (IDictionary<string, object?>)source.ToDynamic()!;
        var values = (List<object?>)result["values"]!;
        values.Should().HaveCount(3);
        ((IDictionary<string, object?>)values[0]!).Should().BeEquivalentTo(subject);
        values[1].Should().Be(3);
        values[2].Should().BeNull();
    }

    // Testzweck: Prüft die Umwandlung eines verschachtelten Objekts in eine dynamische Struktur.
    [Test]
    public void ToDynamicTest()
    {
        var order = new Order
        {
            Address =
            {
                Firstname = "Lukas"
            }
        };
        var dynamicOrder = order.ToDynamic();
        Assert.That(dynamicOrder.GetValue("Address.Firstname"), Is.EqualTo("Lukas"));
    }
    
    // Testzweck: Prüft das Lesen und Schreiben verschachtelter Werte in dynamischen Variablenstrukturen.
    [Test]
    public void GetSetTest()
    {
        var order = new Order();
        var dynamicOrder = order.ToDynamic();
        dynamicOrder.SetValue("Address.Firstname", "Berlin");
        Assert.That(dynamicOrder.GetValue("Address.Firstname"), Is.EqualTo("Berlin"));
    }
    
    // Testzweck: Prüft, dass neue Eigenschaften in einer dynamischen Struktur angelegt werden können.
    [Test]
    public void AddPropertyTest()
    {
        var order = new Order();
        var dynamicOrder = order.ToDynamic();
        dynamicOrder.SetValue("Address.FirstnameNew", "Berlin");
        Assert.That(dynamicOrder.GetValue("Address.FirstnameNew"), Is.EqualTo("Berlin"));
    }

    // Testzweck: Prüft, dass HasProperty verschachtelte Property-Pfade weiterhin bewusst ablehnt.
    [Test]
    public void HasProperty_ShouldThrowNotSupportedException_WhenNestedPropertyIsRequested()
    {
        var order = new Order();
        var dynamicOrder = order.ToDynamic();

        var action = () => dynamicOrder.HasProperty("Address.Firstname");

        Assert.That(action, Throws.TypeOf<NotSupportedException>());
    }
}

public class Order
{
    public Address Address { get; } = new();

}

public class Address
{
    public string Firstname { get; set; } = string.Empty;
}
