using System.Collections;
using System.Reflection;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services.Canon;

internal static class CanonEntityPropertyGraph
{
    private static readonly Dictionary<(Type Type, string JsonKey), PropertyInfo?> PropertyCache = new();

    public static bool HasProperty(object entity, string jsonKey) =>
        ResolveProperty(entity.GetType(), jsonKey) is not null;

    public static bool TryGetValue(object entity, string jsonKey, out object? value)
    {
        var property = ResolveProperty(entity.GetType(), jsonKey);
        if (property is null)
        {
            value = null;
            return false;
        }

        value = property.GetValue(entity);
        return true;
    }

    public static bool TrySetValue(object entity, string jsonKey, string rawValue)
    {
        var property = ResolveProperty(entity.GetType(), jsonKey);
        if (property is null || !property.CanWrite)
            return false;

        if (property.PropertyType == typeof(string))
        {
            property.SetValue(entity, rawValue);
            return true;
        }

        if (property.PropertyType == typeof(bool)
            && bool.TryParse(rawValue, out var boolValue))
        {
            property.SetValue(entity, boolValue);
            return true;
        }

        if (property.PropertyType == typeof(QuestStatus)
            && Enum.TryParse<QuestStatus>(rawValue, ignoreCase: true, out var status))
        {
            property.SetValue(entity, status);
            return true;
        }

        if (typeof(IList).IsAssignableFrom(property.PropertyType)
            && property.PropertyType.IsGenericType
            && property.PropertyType.GetGenericArguments()[0] == typeof(string))
        {
            var list = rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            property.SetValue(entity, list);
            return true;
        }

        return false;
    }

    private static PropertyInfo? ResolveProperty(Type type, string jsonKey)
    {
        var key = (type, jsonKey);
        if (PropertyCache.TryGetValue(key, out var cached))
            return cached;

        var property = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(p => string.Equals(p.Name, jsonKey, StringComparison.OrdinalIgnoreCase));

        PropertyCache[key] = property;
        return property;
    }
}
