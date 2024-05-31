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
        
        if (!IsComlexValue(obj))
        {
            throw new InvalidOperationException($"could not convert {obj.GetType()} to dynamic object.");
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

    public static object? GetValue(this Variables? vars, string propertyName)
    {
        if (vars is null)
            return null;
        return GetValue((IDictionary<string,object?>) vars, propertyName);
    }
    public static object? GetValue(this IDictionary<string, object?> expandoObject, string propertyName)
    {
        var dict = (IDictionary<string, object?>)expandoObject;

        if (propertyName.Contains("["))
            throw new NotSupportedException("getting values of object arrays is not implemented yet.");

        
        if (propertyName.Contains("."))
        {
            var firstPart = propertyName.Substring(0, propertyName.IndexOf('.'));
            var rest = propertyName.Substring(propertyName.IndexOf('.') + 1);
            var varContent = (Variables?)dict[firstPart];
            if (varContent == null)
                return null;
            return GetValue(varContent, rest);
        }
        else
        {
            if (!dict.ContainsKey(propertyName))
                return null;
            return dict[propertyName];
        }
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
                if (!IsComlexValue(subObject))
                    throw new InvalidOperationException(
                        $"could not set property {propertyName} to not complex object.");
                
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