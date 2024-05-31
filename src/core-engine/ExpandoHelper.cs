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
            var value = prob.GetValue(obj);
            if (IsComlexValue(value))
            {
                value = ToDynamic(value);
            }
            expandoDictionary.Add(prob.Name, value);
        }

        return expandoObject;
    }

    private static bool IsComlexValue(object? value)
    {
        return value != null && 
               !value.GetType().IsPrimitive &&
               value.GetType() != typeof(string);
    }

    public static object? GetValue(this Variables expandoObject, string propertyName)
    {
        var dict = (IDictionary<string, object?>)expandoObject;
        return dict[propertyName];
    }
    
    public static void SetValue(this Variables expandoObject, string propertyName, object? value)
    {
        var dict = (IDictionary<string, object?>)expandoObject;
        if (propertyName.Contains("["))
            throw new NotSupportedException("setting values to object arrays is not implemented yet.");
        
        if (propertyName.Contains("."))
        {
            var firstPart = propertyName.Substring(0, propertyName.IndexOf('.'));
            object? subObject = dict[firstPart];
            if (subObject == null) // create new object if property value not exists
            {
                subObject = new Variables();
                dict[firstPart] = subObject;
            }
            if (!(subObject is Variables)) // convert to dynamic object if property value is not dynamic
            {
                subObject = subObject.ToDynamic();
                dict[firstPart] = subObject;
            }
            //call recursively to set value
            var rest = propertyName.Substring(propertyName.IndexOf('.') + 1);
            SetValue((Variables)subObject, rest, value);
        }
        else
        {
            dict[propertyName] = value;
        }
        
    }
}