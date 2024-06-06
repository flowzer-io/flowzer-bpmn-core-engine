using System.Reflection;

namespace core_engine.Extensions;

public static class RecordExtensions
{
    public static T ApplyResolveExpression<T>(this object? record, Func<object, string, string> resolveExpression,
        object variables)
    {
        if (record == null)
            return default!;

        var properties = record.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).ToArray();

        var newObject = Activator.CreateInstance(record.GetType());

        foreach (var propertyInfo in properties)
        {
            var propertyValue = propertyInfo.GetValue(record);

            if (propertyValue is string stringValue)
                propertyValue = resolveExpression(variables, stringValue);

            if (IsRecordType(propertyInfo.PropertyType))
            {
                propertyValue = typeof(RecordExtensions)
                    .GetMethod(nameof(ApplyResolveExpression), BindingFlags.Static | BindingFlags.Public)!
                    .MakeGenericMethod(propertyInfo.PropertyType)
                    .Invoke(null, [propertyValue, resolveExpression, variables]);
            }

            propertyInfo.SetValue(newObject, propertyValue);
        }

        return (T)newObject!;
    }

    private static bool IsRecordType(Type type)
    {
        return type.IsClass && type.GetMethod("<Clone>$") != null;
    }
}