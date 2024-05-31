using System.Net.Sockets;
using System.Threading.Channels;
using core_engine;

namespace core_enginge_tests;

public class ExpandoHelperTest
{
    [Test]
    public void ToDynamicTest()
    {
        var order = new Order();
        order.Address.Firstname = "Lukas";
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
    public void AddPropertxTest()
    {
        var order = new Order();
        var dynamicOrder = order.ToDynamic();
        dynamicOrder.SetValue("Address.FirstnameNew", "Berlin");
        Assert.That(dynamicOrder.GetValue("Address.FirstnameNew"), Is.EqualTo("Berlin"));
    }
}

public class Order
{
    public Address Address { get; set; } = new Address();

}

public class Address
{
    public string Firstname { get; set; }
}