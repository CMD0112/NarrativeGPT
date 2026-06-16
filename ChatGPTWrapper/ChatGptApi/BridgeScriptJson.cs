using System.Text.Json;

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// Normalizes ExecuteScriptAsync JSON results from the in-page API bridge.
/// </summary>
internal static class BridgeScriptJson
{
    public static bool IsBridgeSuccess(ApiBridgeMessage msg)
    {
        if (msg.Root.ValueKind != JsonValueKind.Object)
            return false;

        if (msg.Root.TryGetProperty("ok", out var ok)
            && (ok.ValueKind == JsonValueKind.True
                || (ok.ValueKind == JsonValueKind.String
                    && string.Equals(ok.GetString(), "true", StringComparison.OrdinalIgnoreCase))))
            return true;

        if (msg.Root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String)
        {
            var t = type.GetString();
            if (string.Equals(t, "pong", StringComparison.OrdinalIgnoreCase)
                || string.Equals(t, "apiResult", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return msg.Ok;
    }

    public static string Normalize(string raw)
    {
        var s = raw.Trim();
        for (var depth = 0; depth < 3; depth++)
        {
            if (string.IsNullOrWhiteSpace(s))
                return "";

            try
            {
                using var doc = JsonDocument.Parse(s);
                if (doc.RootElement.ValueKind == JsonValueKind.Null)
                    return "";

                if (doc.RootElement.ValueKind == JsonValueKind.String)
                {
                    var inner = doc.RootElement.GetString();
                    if (string.IsNullOrWhiteSpace(inner))
                        return "";
                    s = inner.Trim();
                    continue;
                }

                return s;
            }
            catch
            {
                var unquoted = UnquoteJsonString(s);
                if (string.Equals(unquoted, s, StringComparison.Ordinal))
                    return s;
                s = unquoted;
            }
        }

        return s;
    }

    public static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value ?? "" : value[..max];

    private static string UnquoteJsonString(string raw)
    {
        var s = raw.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
        {
            try
            {
                return JsonSerializer.Deserialize<string>(s) ?? s;
            }
            catch
            {
                return s[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
            }
        }

        return s;
    }
}
