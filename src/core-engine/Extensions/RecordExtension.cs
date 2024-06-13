using System.Reflection;
using BPMN;

namespace core_engine.Extensions;

public static class RecordExtensions
{
    public static T ApplyResolveExpression<T>(this object? record, Func<object, string, object?> resolveExpression,
        object variables)
    {
        if (record == null)
            return default!;

        var properties = record.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).ToArray();

        var newObject = Activator.CreateInstance(record.GetType());

        foreach (var propertyInfo in properties)
        {
            
            var propertyValue = propertyInfo.GetValue(record);
            if (propertyInfo.GetCustomAttribute<DoNotTranslateAttribute>() != null)
            {
                propertyInfo.SetValue(newObject, propertyValue);
                continue;
            }
            
            if (propertyValue is string stringValue)
                propertyValue = resolveExpression(variables, stringValue);

            if (IsRecordType(propertyInfo.PropertyType))
            {
                propertyValue = typeof(RecordExtensions)
                    .GetMethod(nameof(ApplyResolveExpression), BindingFlags.Static | BindingFlags.Public)!
                    .MakeGenericMethod(propertyInfo.PropertyType)
                    .Invoke(null, [propertyValue, resolveExpression, variables]);
            }

            if (propertyValue != null && propertyInfo.PropertyType == typeof(string) && propertyValue.GetType() != typeof(string)) //to string if types does not match
                propertyInfo.SetValue(newObject, propertyValue.ToString());
            else
                propertyInfo.SetValue(newObject, propertyValue);
        }

        return (T)newObject!;
    }

    private static bool IsRecordType(Type type)
    {
        return type.IsClass && type.GetMethod("<Clone>$") != null;
    }
    
   
}