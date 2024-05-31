using System.Dynamic;
using System.Reflection.Metadata;
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
            var ret = new List<object>();
            foreach (var o in obj.ToEnumerable())
            {
                if (IsComlexValue(o))
                    ret.Add(ToDynamic(o));
                else
                    ret.Add(o);
            }
            return ret;
        }
        else
        {
            var ret = new ExpandoObject();
            foreach (var objPropertyName in obj.PropertyNames)
            {
                var value = obj.GetProperty(objPropertyName);
                if (IsComlexValue(value))
                    ret.TryAdd(objPropertyName, value.ToDynamic());
                else
                    ret.TryAdd(objPropertyName, value);
            }

            return ret;
        }
    }
    
    public static object? ToDynamic(this object? obj)
    {
        if (obj == null)
        {
            return null;
        }
        
        if (obj is IJavaScriptObject jsObj)
        {
            return jsObj.ToDynamic();
        }
        
        if (obj is ExpandoObject)
        {
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
        if (vars is null)
            return null;
        return GetValue((IDictionary<string,object?>) vars, propertyName);
    }
    public static object? GetValue(this IDictionary<string, object?>? dict, string propertyName)
    {
        if (dict is null)
                return null;
          
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
    
    public static void SetValue(this object? obj, string propertyName, object? value)
    {
        if (!(obj is ExpandoObject))
            throw new NotSupportedException($"cannot set property {propertyName} on none expando-objects.");
        
        var dict = (IDictionary<string, object?>)obj;
        if (propertyName.Contains("["))
            throw new NotSupportedException("setting values to object arrays is not implemented yet.");
        
        if (propertyName.Contains("."))
        {
            var firstPart = propertyName.Substring(0, propertyName.IndexOf('.'));
            object? subObject = null;
            if (dict.TryGetValue(firstPart, out var outValue))
                subObject = outValue;
                
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