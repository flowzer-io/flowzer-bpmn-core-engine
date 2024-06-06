using System.Diagnostics;
using System.Dynamic;
using System.Reflection;
using Microsoft.ClearScript.V8;
using Newtonsoft.Json.Linq;

MethodInfo cloneMethod = typeof(Person).GetMethod("<Clone>$");


Person p1 = new Person()
{
    FirstName = "John",
    LastName ="Doe"
};
Person p2 = (Person)Activator.CreateInstance(typeof(Person));
var ret = cloneMethod.Invoke(p1, null);

typeof(Person).GetProperties(BindingFlags.Public).First().SetValue(ret, "Jane");
p1 = p1;

public record Person
{
    public string FirstName { get; init; }
    public string LastName { get; init; }
}
