using Flowzer.Shared;

namespace core_engine_tests;

public class ExpandoHelperTest
{
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
