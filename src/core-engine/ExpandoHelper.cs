using System.Dynamic;

namespace core_engine;

public static class ExpandoHelper
{
    public static Variables ToDynamic(this object? obj)
    {
        var expandoObject = new ExpandoObject();
        if (obj == null)
        {
            return expandoObject;
        }

        var expandoDictionary = expandoObject as IDictionary<string, object?>;
        foreach (var prob in obj.GetType().GetProperties())
        {
            expandoDictionary.Add(prob.Name, prob.GetValue(obj));
        }

        return expandoObject;
    }

    public static void SetValue(this Variables expandoObject, string propertyName,  object? value)
    {
        var dict = (IDictionary<string, object?>)expandoObject;
        dict[propertyName] = value;
    }
}