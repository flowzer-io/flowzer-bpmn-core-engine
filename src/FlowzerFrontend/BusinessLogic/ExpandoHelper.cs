using System.Dynamic;
using System.Text.Json;

namespace FlowzerFrontend.BusinessLogic;

public static class ExpandoHelper
{
    public static T? GetValue<T>(this ExpandoObject? expandoObject, string key, T? defaultValue)
    {
        if (expandoObject == null)
        {
            return (T?)defaultValue;
        }

        if (((IDictionary<string, object?>)expandoObject).TryGetValue(key, out var value))
        {
            if (value is T castedValue)
            {
                return castedValue;
            }
            else
            {
                throw new Exception("Value is not of type " + typeof(T).Name + " valuetype is " + value?.GetType().Name ?? "null");
            }
        }

        return (T?)defaultValue;
    }
}