using System.Dynamic;
using Microsoft.ClearScript.JavaScript;

namespace core_engine;

public static class ExpandoHelper
{
    public static object? ToDynamic(this IJavaScriptObject? obj)
    {
        if (obj == null)
            return null;
        
        if (obj.Kind == JavaScriptObjectKind.Array)
        {
            return obj.ToEnumerable().Select(o => IsComlexValue(o) ? ToDynamic(o) : o).ToList();
        }

        var ret = new Variables();
        foreach (var objPropertyName in obj.PropertyNames)
        {
            var value = obj.GetProperty(objPropertyName);
            ret.TryAdd(objPropertyName, IsComlexValue(value) ? value.ToDynamic() : value);
        }

        return ret;
    }
    
    public static object? ToDynamic(this object? obj)
    {
        switch (obj)
        {
            case null:
                return null;
            case IJavaScriptObject jsObj:
                return jsObj.ToDynamic();
            case Variables:
                return obj;
        }

        if (!IsComlexValue(obj))
        {
            throw new InvalidOperationException($"could not convert {obj.GetType()} to dynamic object.");
        }
        var expandoObject = new ExpandoObject();
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

    internal static bool IsComlexValue(object? value)
    {
        return value != null && 
               !value.GetType().IsPrimitive &&
               value.GetType() != typeof(string);
    }

    public static object? GetValue(this object? vars, string propertyName)
    {
        return vars is null ? null : GetValue((IDictionary<string,object?>) vars, propertyName);
    }

    public static object? GetValue(this IDictionary<string, object?>? dict, string propertyName)
    {
        while (true)
        {
            if (dict is null) return null;

            if (propertyName.Contains('[')) throw new NotSupportedException("getting values of object arrays is not implemented yet.");


            if (!propertyName.Contains('.')) return !dict.TryGetValue(propertyName, out var value) ? null : value;

            var firstPart = propertyName[..propertyName.IndexOf('.')];
            var rest = propertyName[(propertyName.IndexOf('.') + 1)..];
            var varContent = (Variables?)dict[firstPart];
            if (varContent == null) return null;
            dict = varContent;
            propertyName = rest;
        }
    }

    public static void SetValue(this object? obj, string propertyName, object? value)
    {
        while (true)
        {
            if (obj is not Variables) 
                throw new NotSupportedException($"cannot set property {propertyName} on none expando-objects.");

            var dict = (IDictionary<string, object?>)obj;
            if (propertyName.Contains('[')) 
                throw new NotSupportedException("setting values to object arrays is not implemented yet.");

            if (propertyName.Contains('.'))
            {
                var firstPart = propertyName[..propertyName.IndexOf('.')];

                dict.TryGetValue(firstPart, out var subObject);
  
                
                if (subObject == null) // create new object if property value not exists
                {
                    subObject = new Variables();
                    dict[firstPart] = subObject;
                }

                if (subObject is not Variables) // convert to dynamic object if property value is not dynamic
                {
                    if (!IsComlexValue(subObject)) throw new InvalidOperationException($"could not set property {propertyName} to not complex object.");

                    subObject = subObject.ToDynamic();
                    dict[firstPart] = subObject;
                }

                //call recursively to set value
                var rest = propertyName[(propertyName.IndexOf('.') + 1)..];
                obj = (Variables)subObject!;
                propertyName = rest;
                continue;
            }
            else
            {
                dict[propertyName] = value;
            }

            break;
        }
    }
}