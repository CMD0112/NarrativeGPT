using System.Text;
using System.Text.Json;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Repairs common ChatGPT utility JSON defects before parse/apply (unescaped dialogue quotes).
/// </summary>
internal static class UtilityJsonRepairService
{
    /// <summary>Returns valid JSON text, repairing unescaped quotes when needed.</summary>
    public static string? TryEnsureValidJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (CanParse(text))
            return text;

        var repaired = RepairUnescapedQuotesInStrings(text);
        return CanParse(repaired) ? repaired : null;
    }

    internal static string RepairUnescapedQuotesInStrings(string json)
    {
        var sb = new StringBuilder(json.Length + 32);
        var inString = false;
        var escaped = false;

        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];
            if (!inString)
            {
                sb.Append(c);
                if (c == '"')
                    inString = true;
                continue;
            }

            if (escaped)
            {
                sb.Append(c);
                escaped = false;
                continue;
            }

            if (c == '\\')
            {
                sb.Append(c);
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                if (IsStructuralStringEnd(json, i))
                {
                    sb.Append(c);
                    inString = false;
                }
                else
                {
                    sb.Append('\\');
                    sb.Append('"');
                }

                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool CanParse(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// A double quote ends a JSON string when the next non-whitespace character is structural.
    /// Inner dialogue quotes (e.g. "A child, clearly.") are escaped instead.
    /// </summary>
    private static bool IsStructuralStringEnd(string json, int quoteIndex)
    {
        var i = quoteIndex + 1;
        while (i < json.Length && char.IsWhiteSpace(json[i]))
            i++;

        if (i >= json.Length)
            return true;

        return json[i] is ',' or '}' or ']' or ':';
    }
}
