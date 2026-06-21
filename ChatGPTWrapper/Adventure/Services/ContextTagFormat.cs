using System.Text;
using System.Text.RegularExpressions;

namespace ChatGPTWrapper.Adventure.Services;

internal static partial class ContextTagFormat
{
    public const string TagPrefix = "[[cgw:";

    private static readonly Regex BlockRegex = new(
        @"\[\[cgw:(?<name>[^\]/\]]+)(?<attrs>[^\]]*)\]\](?<body>.*?)\[\[/cgw:\k<name>\]\]",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static string NormalizeLineBreaks(string content)
    {
        if (string.IsNullOrEmpty(content))
            return "";

        return content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    public static string WrapBlock(string tagName, string content, IReadOnlyDictionary<string, string>? attrs = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            return "";

        var attrText = attrs is null || attrs.Count == 0
            ? ""
            : " " + string.Join(" ", attrs.Select(kv => $"{kv.Key}=\"{EscapeAttr(kv.Value)}\""));
        return $"{TagPrefix}{tagName}{attrText}]]{NormalizeLineBreaks(content)}[[/cgw:{tagName}]]";
    }

    public static string WrapMeta(
        PacketMode mode,
        int? turnIndex = null,
        bool continuation = false,
        int? adventureTurn = null)
    {
        var attrs = new Dictionary<string, string>
        {
            ["mode"] = mode == PacketMode.Thin ? "thin" : "fat",
            ["turn"] = turnIndex?.ToString() ?? "",
        };

        if (continuation)
            attrs["continuation"] = "true";

        if (adventureTurn is > 0)
            attrs["adventureTurn"] = adventureTurn.Value.ToString();

        var attrText = " " + string.Join(" ", attrs.Select(kv => $"{kv.Key}=\"{EscapeAttr(kv.Value)}\""));
        return $"{TagPrefix}meta{attrText}]] [[/cgw:meta]]";
    }

    public static string StripTaggedBlocks(string text, bool removeAll = true)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        return BlockRegex.Replace(text, removeAll ? "" : "[…]");
    }

    public static string? ExtractBlock(string text, string tagName)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        foreach (Match match in BlockRegex.Matches(text))
        {
            if (string.Equals(match.Groups["name"].Value, tagName, StringComparison.OrdinalIgnoreCase))
                return NormalizeLineBreaks(match.Groups["body"].Value);
        }

        return null;
    }

    public static IReadOnlyDictionary<string, string> ExtractAllBlocks(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(text))
            return map;

        foreach (Match match in BlockRegex.Matches(text))
        {
            var name = match.Groups["name"].Value;
            map[name] = NormalizeLineBreaks(match.Groups["body"].Value);
        }

        return map;
    }

    public static string? ExtractUntaggedSuffix(string packetText)
    {
        if (string.IsNullOrEmpty(packetText))
            return null;

        var remainder = BlockRegex.Replace(packetText, "").Trim();
        return string.IsNullOrWhiteSpace(remainder) ? null : remainder;
    }

    public static IReadOnlyDictionary<string, string> ExtractTagAttributes(string text, string tagName)
    {
        if (string.IsNullOrEmpty(text))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in BlockRegex.Matches(text))
        {
            if (!string.Equals(match.Groups["name"].Value, tagName, StringComparison.OrdinalIgnoreCase))
                continue;

            return ParseAttributes(match.Groups["attrs"].Value);
        }

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static string FormatTagAttributePreview(string tagName, IReadOnlyDictionary<string, string> attrs)
    {
        if (attrs.Count == 0)
            return "";

        return string.Join(" ", attrs.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    public static string FormatStructuredPreview(string packetText)
    {
        var blocks = ExtractAllBlocks(packetText);
        var suffix = ExtractUntaggedSuffix(packetText);
        var metaAttrs = ExtractTagAttributes(packetText, "meta");

        if (blocks.Count == 0 && metaAttrs.Count == 0 && string.IsNullOrWhiteSpace(suffix))
            return packetText;

        var sb = new StringBuilder();
        var preferredOrder = new[] { "meta", "sources", "instructions", "summary", "state", "cards", "memory", "transcript" };
        var ordered = blocks
            .OrderBy(kv =>
            {
                var idx = Array.IndexOf(preferredOrder, kv.Key);
                return idx >= 0 ? idx : preferredOrder.Length + 1;
            })
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase);

        var wroteMeta = false;
        foreach (var (name, body) in ordered)
        {
            if (string.Equals(name, "meta", StringComparison.OrdinalIgnoreCase))
            {
                wroteMeta = true;
                sb.Append("[meta]");
                var attrPreview = FormatTagAttributePreview(name, metaAttrs);
                if (!string.IsNullOrWhiteSpace(attrPreview))
                    sb.Append(' ').Append(attrPreview);
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    sb.AppendLine(NormalizeLineBreaks(body));
                    sb.AppendLine();
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(body))
                continue;

            sb.Append('[').Append(name).AppendLine("]");
            sb.AppendLine(NormalizeLineBreaks(body));
            sb.AppendLine();
        }

        if (!wroteMeta && metaAttrs.Count > 0)
        {
            sb.Append("[meta] ");
            sb.AppendLine(FormatTagAttributePreview("meta", metaAttrs));
            sb.AppendLine();
        }

        var player = ExtractBlock(packetText, "player");
        if (!string.IsNullOrWhiteSpace(player))
        {
            sb.AppendLine("[player]");
            sb.AppendLine(player);
        }
        else if (!string.IsNullOrWhiteSpace(suffix))
        {
            sb.AppendLine("[user]");
            sb.AppendLine(suffix);
        }

        return sb.ToString().TrimEnd();
    }

    private static Dictionary<string, string> ParseAttributes(string attrsText)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(attrsText))
            return map;

        foreach (Match match in Regex.Matches(attrsText, @"\b([a-zA-Z_][\w-]*)=""([^""]*)""", RegexOptions.CultureInvariant))
            map[match.Groups[1].Value] = match.Groups[2].Value;

        return map;
    }

    private static string EscapeAttr(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    [GeneratedRegex(@"\[\[cgw:", RegexOptions.CultureInvariant)]
    private static partial Regex TagMarkerRegex();

    public static bool ContainsTags(string text) =>
        !string.IsNullOrEmpty(text) && text.Contains(TagPrefix, StringComparison.Ordinal);

    public const int UtilityTagSchemaVersion = 1;

    public const string UtilityTagName = "utility";

    public const string UtilityResponseTagName = "utility-response";

    public static string WrapUtilityJob(string jobId, string body) =>
        WrapBlock(UtilityTagName, body, new Dictionary<string, string>
        {
            ["job"] = jobId,
            ["v"] = UtilityTagSchemaVersion.ToString(),
        });

    public static string WrapUtilityResponse(string jobId, string body) =>
        WrapBlock(UtilityResponseTagName, body, new Dictionary<string, string>
        {
            ["job"] = jobId,
            ["v"] = UtilityTagSchemaVersion.ToString(),
        });

    public static bool IsUtilityTagged(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.TrimStart().Contains($"{TagPrefix}{UtilityTagName}", StringComparison.Ordinal);

    public static bool IsUtilityResponseTagged(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.TrimStart().Contains($"{TagPrefix}{UtilityResponseTagName}", StringComparison.Ordinal);

    /// <summary>
    /// Pulls job output from an inline utility-response wrapper, or returns stripped plain text.
    /// </summary>
    public static string UnwrapUtilityJobResponse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var body = ExtractBlock(text, UtilityResponseTagName);
        if (!string.IsNullOrWhiteSpace(body))
            return NormalizeLineBreaks(body);

        return NormalizeLineBreaks(StripTaggedBlocks(text));
    }

    public static string AppendInlineUtilityResponseContract(string jobBody, string jobId, bool expectsJsonArray) =>
        AppendInlineUtilityResponseContract(jobBody, jobId, expectsJsonArray, expectsJsonObject: false);

    public static string AppendInlineUtilityResponseContract(
        string jobBody,
        string jobId,
        bool expectsJsonArray,
        bool expectsJsonObject)
    {
        var formatHint = expectsJsonObject
            ? "valid JSON only (single object with the required keys)"
            : expectsJsonArray
                ? "valid JSON only (array or object as specified above)"
                : "plain text or JSON exactly as required by the job instructions above";

        var example = WrapUtilityResponse(jobId, "...");

        return $"""
            {jobBody}

            === INLINE UTILITY RESPONSE (required) ===
            Reply with your job output ONLY. Wrap the entire reply in this tag pair — no roleplay, preamble, or text outside it:
            {example}

            Replace ... with {formatHint}. Use job="{jobId}" and v="{UtilityTagSchemaVersion}".
            """;
    }

    public static string? ExtractUtilityJobId(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        foreach (Match match in BlockRegex.Matches(text))
        {
            if (!string.Equals(match.Groups["name"].Value, UtilityTagName, StringComparison.OrdinalIgnoreCase))
                continue;

            var attrs = match.Groups["attrs"].Value;
            var jobMatch = Regex.Match(attrs, @"\bjob=""([^""]*)""", RegexOptions.CultureInvariant);
            if (jobMatch.Success)
                return jobMatch.Groups[1].Value;

            return null;
        }

        return null;
    }
}
