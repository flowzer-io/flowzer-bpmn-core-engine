namespace core_engine_tests;

public class ExpandoHelperTest
{
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
    
    [Test]
    public void GetSetTest()
    {
        var order = new Order();
        var dynamicOrder = order.ToDynamic();
        dynamicOrder.SetValue("Address.Firstname", "Berlin");
        Assert.That(dynamicOrder.GetValue("Address.Firstname"), Is.EqualTo("Berlin"));
    }
    
    [Test]
    public void AddPropertyTest()
    {
        var order = new Order();
        var dynamicOrder = order.ToDynamic();
        dynamicOrder.SetValue("Address.FirstnameNew", "Berlin");
        Assert.That(dynamicOrder.GetValue("Address.FirstnameNew"), Is.EqualTo("Berlin"));
    }
}

public class Order
{
    public Address Address { get; } = new();

}

public class Address
{
    public string Firstname { get; set; }
}