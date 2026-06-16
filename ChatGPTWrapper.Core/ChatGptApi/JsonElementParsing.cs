using System.Text.Json;

namespace ChatGPTWrapper.ChatGptApi;

public static class JsonElementParsing
{
    public static bool TryGetObjectProperty(
        JsonElement element,
        string propertyName,
        out JsonElement objectValue)
    {
        objectValue = default;
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
            return false;

        objectValue = value;
        return true;
    }

    public static IEnumerable<JsonElement> EnumerateObjectElements(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object)
                yield return element;
        }
    }

    public static string? GetStringProperty(JsonElement element, string propertyName) =>
        GetStringOrNull(element, propertyName);

    public static string? GetStringOrNull(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    public static string? GetStringOrNull(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    public static long? GetInt64OrNull(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (!element.TryGetProperty(name, out var value))
                continue;

            var parsed = GetInt64OrNull(value);
            if (parsed is not null)
                return parsed;
        }

        return null;
    }

    public static long? GetInt64OrNull(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return null;

        if (value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), out var parsedFromString))
        {
            return parsedFromString;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n))
            return n;

        return null;
    }

    public static string? GetCursorOrNull(JsonElement root)
    {
        foreach (var name in new[] { "cursor", "next_cursor" })
        {
            if (!root.TryGetProperty(name, out var value))
                continue;

            var cursor = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number when value.TryGetInt64(out var n) => n.ToString(),
                JsonValueKind.Null => null,
                _ => null,
            };

            if (string.IsNullOrWhiteSpace(cursor) || cursor == "0")
                continue;

            return cursor;
        }

        return null;
    }
}
