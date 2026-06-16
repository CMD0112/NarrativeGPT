using System.Text.Json;

namespace ChatGPTWrapper.ChatGptApi;

public sealed class ConversationStreamParseResult
{
    public string? AssistantText { get; init; }

    public string? AssistantMessageId { get; init; }

    public string? ConversationId { get; init; }

    public bool StreamComplete { get; init; }
}

/// <summary>
/// Parses ChatGPT f/conversation SSE text into assistant message content (mirrors cgw-conversation-stream.js).
/// </summary>
public static class ConversationStreamParser
{
    public static ConversationStreamParseResult Parse(string? sseText)
    {
        var state = new ParseState();
        if (!string.IsNullOrWhiteSpace(sseText))
            ApplyChunk(state, sseText);

        return Finalize(state);
    }

    public static ConversationStreamParseResult ParseChunks(IEnumerable<string> chunks)
    {
        var state = new ParseState();
        foreach (var chunk in chunks)
            ApplyChunk(state, chunk);

        return Finalize(state);
    }

    private sealed class ParseState
    {
        public List<string> Parts { get; } = [""];

        public string? AssistantMessageId { get; set; }

        public string? ConversationId { get; set; }

        public bool StreamComplete { get; set; }
    }

    private static void ApplyChunk(ParseState state, string chunkText)
    {
        var blocks = chunkText.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.None);
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block))
                continue;

            foreach (var line in block.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                    continue;

                var payload = line[5..].Trim();
                if (payload.Length == 0)
                    continue;

                if (payload == "[DONE]")
                {
                    state.StreamComplete = true;
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(payload);
                    ApplyEventObject(state, doc.RootElement);
                }
                catch (JsonException)
                {
                    /* ignore malformed SSE lines */
                }
            }
        }
    }

    private static void ApplyEventObject(ParseState state, JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return;

        if (obj.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
        {
            var type = typeEl.GetString();
            if (type is "message_stream_complete" or "conversation_stream_complete" or "stream_end")
                state.StreamComplete = true;
        }

        if (obj.TryGetProperty("conversation_id", out var convEl) && convEl.ValueKind == JsonValueKind.String)
            state.ConversationId ??= convEl.GetString();

        if (obj.TryGetProperty("message", out var messageEl))
            ApplySnapshot(state, messageEl);

        if (obj.TryGetProperty("v", out var valueEl))
        {
            if (valueEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in valueEl.EnumerateArray())
                    ApplyPatch(state, obj, item);
            }
            else if (valueEl.ValueKind == JsonValueKind.Object)
            {
                if (valueEl.TryGetProperty("message", out var nestedMessage))
                    ApplySnapshot(state, nestedMessage);
                else
                    ApplyPatch(state, obj, valueEl);
            }
            else if (valueEl.ValueKind == JsonValueKind.String
                     && obj.TryGetProperty("o", out var opEl)
                     && opEl.GetString() == "append")
            {
                ApplyPatch(state, obj, valueEl);
            }
        }
        else
        {
            ApplyPatch(state, obj, default);
        }
    }

    private static void ApplySnapshot(ParseState state, JsonElement message)
    {
        if (!IsAssistantMessage(message))
            return;

        if (message.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
            state.AssistantMessageId = idEl.GetString();

        if (!message.TryGetProperty("content", out var contentEl))
            return;

        var parts = ExtractParts(contentEl);
        if (parts is { Count: > 0 })
        {
            state.Parts.Clear();
            state.Parts.AddRange(parts);
        }
    }

    private static void ApplyPatch(ParseState state, JsonElement patchRoot, JsonElement valueEl)
    {
        if (patchRoot.ValueKind == JsonValueKind.Object
            && patchRoot.TryGetProperty("v", out var vObj)
            && vObj.ValueKind == JsonValueKind.Object
            && vObj.TryGetProperty("message", out var messageEl))
        {
            ApplySnapshot(state, messageEl);
            return;
        }

        var op = patchRoot.TryGetProperty("o", out var opEl) ? opEl.GetString() : null;
        var path = patchRoot.TryGetProperty("p", out var pathEl) ? pathEl.GetString() : "";

        if (op == "append"
            && path?.Contains("/message/content/parts/0", StringComparison.Ordinal) == true
            && valueEl.ValueKind == JsonValueKind.String)
        {
            var existing = TextFromParts(state.Parts);
            state.Parts.Clear();
            state.Parts.Add(existing + valueEl.GetString());
            return;
        }

        if (op == "replace"
            && path?.Contains("/message/content/parts/0", StringComparison.Ordinal) == true
            && valueEl.ValueKind == JsonValueKind.String)
        {
            state.Parts.Clear();
            state.Parts.Add(valueEl.GetString() ?? "");
            return;
        }

        if (op == "add"
            && valueEl.ValueKind == JsonValueKind.Object
            && valueEl.TryGetProperty("message", out var addMessage))
        {
            ApplySnapshot(state, addMessage);
        }
    }

    private static bool IsAssistantMessage(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object)
            return false;

        if (!message.TryGetProperty("author", out var author) || author.ValueKind != JsonValueKind.Object)
            return false;

        return author.TryGetProperty("role", out var roleEl)
               && roleEl.ValueKind == JsonValueKind.String
               && string.Equals(roleEl.GetString(), "assistant", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string>? ExtractParts(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object
            || !content.TryGetProperty("parts", out var partsEl)
            || partsEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var part in partsEl.EnumerateArray())
        {
            parts.Add(part.ValueKind == JsonValueKind.String ? part.GetString() ?? "" : "");
        }

        return parts.Count > 0 ? parts : null;
    }

    private static string TextFromParts(IReadOnlyList<string> parts) =>
        string.Concat(parts);

    private static ConversationStreamParseResult Finalize(ParseState state)
    {
        var text = TextFromParts(state.Parts).Trim();
        return new ConversationStreamParseResult
        {
            AssistantText = string.IsNullOrWhiteSpace(text) ? null : text,
            AssistantMessageId = state.AssistantMessageId,
            ConversationId = state.ConversationId,
            StreamComplete = state.StreamComplete,
        };
    }

    public static IReadOnlyList<TranscriptTurnPair> ExtractTranscriptTurns(JsonElement json)
    {
        if (!json.TryGetProperty("mapping", out var mapping) || mapping.ValueKind != JsonValueKind.Object)
            return [];

        var currentNode = ResolveCurrentNode(json, mapping);
        if (string.IsNullOrWhiteSpace(currentNode))
            return [];

        var messages = CollectMessagesOnActiveBranch(mapping, currentNode);
        return PairAlternatingMessages(messages);
    }

    private const string UtilityTagMarker = "[[cgw:utility";
    private const string UtilityResponseTagMarker = "[[cgw:utility-response";

    public static bool IsUtilityUserMessage(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.TrimStart().Contains(UtilityTagMarker, StringComparison.Ordinal);

    public static bool IsUtilityAssistantMessage(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && text.TrimStart().Contains(UtilityResponseTagMarker, StringComparison.Ordinal);

    public static string? ExtractUtilityJobId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        const string jobAttr = "job=\"";
        var markerIdx = text.IndexOf(UtilityTagMarker, StringComparison.Ordinal);
        if (markerIdx < 0)
            return null;

        var jobIdx = text.IndexOf(jobAttr, markerIdx, StringComparison.Ordinal);
        if (jobIdx < 0)
            return null;

        var start = jobIdx + jobAttr.Length;
        var end = text.IndexOf('"', start);
        if (end <= start)
            return null;

        return text[start..end];
    }

    public static bool IsInjectedContextUserMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("[[cgw:", StringComparison.Ordinal))
            return true;

        return trimmed.Contains("=== PROJECT SOURCES", StringComparison.Ordinal)
               || trimmed.Contains("=== PLOT ESSENTIALS", StringComparison.Ordinal)
               || trimmed.Contains("=== PLAYER TURN ===", StringComparison.Ordinal)
               || trimmed.Contains("=== STORY SO FAR", StringComparison.Ordinal)
               || trimmed.Contains("=== ROLLING SUMMARY ===", StringComparison.Ordinal)
               || trimmed.Contains("=== STATE ===", StringComparison.Ordinal)
               || trimmed.Contains("=== CURRENT STATE ===", StringComparison.Ordinal);
    }

    /// <summary>
    /// Pulls the player's line out of a full context packet (cgw tags or === PLAYER TURN ===).
    /// Returns null when the message is context-only with no player input.
    /// </summary>
    public static string? ExtractTranscriptPlayerText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (IsUtilityUserMessage(text))
            return null;

        if (!IsInjectedContextUserMessage(text))
        {
            var plain = TranscriptTextSanitizer.Sanitize(text);
            return string.IsNullOrWhiteSpace(plain) ? null : plain;
        }

        const string playerMarker = "=== PLAYER TURN ===";
        var markerIdx = text.IndexOf(playerMarker, StringComparison.Ordinal);
        if (markerIdx >= 0)
        {
            var afterMarker = text[(markerIdx + playerMarker.Length)..].TrimStart('\r', '\n');
            var fromMarker = TranscriptTextSanitizer.Sanitize(afterMarker);
            return string.IsNullOrWhiteSpace(fromMarker) ? null : fromMarker;
        }

        var fromTags = TranscriptTextSanitizer.Sanitize(TranscriptTextSanitizer.StripContextTags(text));
        return string.IsNullOrWhiteSpace(fromTags) ? null : fromTags;
    }

    private static List<(string Role, string Text)> CollectMessagesOnActiveBranch(
        JsonElement mapping,
        string currentNode)
    {
        var parentPath = BuildPathToNode(mapping, currentNode);
        if (parentPath.Count > 1)
        {
            var fromParents = CollectMessagesFromPath(mapping, parentPath);
            if (fromParents.Count > 0)
                return fromParents;
        }

        var root = FindRootNode(mapping);
        if (!string.IsNullOrWhiteSpace(root) && !string.Equals(root, currentNode, StringComparison.Ordinal))
        {
            var childPath = new List<string>();
            if (TryBuildChildPath(mapping, root, currentNode, childPath) && childPath.Count > 1)
            {
                var fromChildren = CollectMessagesFromPath(mapping, childPath);
                if (fromChildren.Count > 0)
                    return fromChildren;
            }
        }

        return CollectAllMessagesSortedByTime(mapping);
    }

    private static List<(string Role, string Text)> CollectMessagesFromPath(
        JsonElement mapping,
        IReadOnlyList<string> path)
    {
        var ordered = new List<(string Role, string Text)>();
        foreach (var nodeId in path)
        {
            if (!mapping.TryGetProperty(nodeId, out var nodeEl)
                || !JsonElementParsing.TryGetObjectProperty(nodeEl, "message", out var message))
            {
                continue;
            }

            var role = GetMessageRole(message);
            if (role is not ("user" or "assistant"))
                continue;

            var text = role == "user"
                ? ExtractTranscriptPlayerText(ExtractMessageText(message))
                : ExtractMessageText(message);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            ordered.Add((role, text));
        }

        return ordered;
    }

    private static List<(string Role, string Text)> CollectAllMessagesSortedByTime(JsonElement mapping)
    {
        var ordered = new List<(double SortKey, int Index, string Role, string Text)>();
        var index = 0;
        foreach (var prop in mapping.EnumerateObject())
        {
            if (!JsonElementParsing.TryGetObjectProperty(prop.Value, "message", out var message))
                continue;

            var role = GetMessageRole(message);
            if (role is not ("user" or "assistant"))
                continue;

            var text = role == "user"
                ? ExtractTranscriptPlayerText(ExtractMessageText(message))
                : ExtractMessageText(message);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var sortKey = message.TryGetProperty("create_time", out var timeEl) && timeEl.TryGetDouble(out var t)
                ? t
                : double.PositiveInfinity;
            ordered.Add((sortKey, index++, role, text));
        }

        return ordered
            .OrderBy(item => item.SortKey)
            .ThenBy(item => item.Index)
            .Select(item => (item.Role, item.Text))
            .ToList();
    }

    private static string? FindRootNode(JsonElement mapping)
    {
        foreach (var prop in mapping.EnumerateObject())
        {
            if (!prop.Value.TryGetProperty("parent", out var parentEl))
                return prop.Name;

            if (parentEl.ValueKind == JsonValueKind.Null)
                return prop.Name;

            if (parentEl.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(parentEl.GetString()))
                return prop.Name;
        }

        return null;
    }

    private static bool TryBuildChildPath(
        JsonElement mapping,
        string nodeId,
        string targetId,
        List<string> path)
    {
        path.Add(nodeId);
        if (string.Equals(nodeId, targetId, StringComparison.Ordinal))
            return true;

        if (!mapping.TryGetProperty(nodeId, out var nodeEl)
            || !nodeEl.TryGetProperty("children", out var childrenEl)
            || childrenEl.ValueKind != JsonValueKind.Array)
        {
            path.RemoveAt(path.Count - 1);
            return false;
        }

        foreach (var child in childrenEl.EnumerateArray())
        {
            if (child.ValueKind != JsonValueKind.String)
                continue;

            var childId = child.GetString();
            if (string.IsNullOrWhiteSpace(childId))
                continue;

            if (TryBuildChildPath(mapping, childId, targetId, path))
                return true;
        }

        path.RemoveAt(path.Count - 1);
        return false;
    }

    private static List<TranscriptTurnPair> PairAlternatingMessages(IReadOnlyList<(string Role, string Text)> ordered)
    {
        var pairs = new List<TranscriptTurnPair>();
        string? pendingUser = null;
        foreach (var (role, text) in ordered)
        {
            if (role == "user")
            {
                pendingUser = text;
                continue;
            }

            pairs.Add(new TranscriptTurnPair
            {
                PlayerText = pendingUser ?? "",
                NarratorText = text,
            });
            pendingUser = null;
        }

        return pairs;
    }

    private static List<string> BuildPathToNode(JsonElement mapping, string currentNode)
    {
        var path = new List<string>();
        var node = currentNode;
        while (!string.IsNullOrWhiteSpace(node))
        {
            path.Add(node);
            if (!mapping.TryGetProperty(node, out var nodeEl)
                || !nodeEl.TryGetProperty("parent", out var parentEl))
            {
                break;
            }

            if (parentEl.ValueKind == JsonValueKind.Null)
                break;

            node = parentEl.GetString();
        }

        path.Reverse();
        return path;
    }

    private static string? ResolveCurrentNode(JsonElement json, JsonElement mapping)
    {
        var currentNode = JsonElementParsing.GetStringOrNull(json, "current_node")
                          ?? JsonElementParsing.GetStringOrNull(json, "currentNode");
        if (!string.IsNullOrWhiteSpace(currentNode))
            return currentNode;

        var nodesWithChildren = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in mapping.EnumerateObject())
        {
            if (prop.Value.TryGetProperty("parent", out var parentEl)
                && parentEl.ValueKind == JsonValueKind.String)
            {
                var parentId = parentEl.GetString();
                if (!string.IsNullOrWhiteSpace(parentId))
                    nodesWithChildren.Add(parentId);
            }
        }

        string? leaf = null;
        foreach (var prop in mapping.EnumerateObject())
        {
            if (!nodesWithChildren.Contains(prop.Name))
                leaf = prop.Name;
        }

        return leaf;
    }

    private static string GetMessageRole(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object)
            return "";

        if (!message.TryGetProperty("author", out var author) || author.ValueKind != JsonValueKind.Object)
            return "";

        return author.TryGetProperty("role", out var roleEl) && roleEl.ValueKind == JsonValueKind.String
            ? roleEl.GetString() ?? ""
            : "";
    }

    private static string? ExtractMessageText(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object)
            return null;

        if (!message.TryGetProperty("content", out var content))
            return null;

        var parts = ExtractParts(content);
        if (parts is null)
            return null;

        var text = TranscriptTextSanitizer.Sanitize(TextFromParts(parts));
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public static string? ExtractAssistantChildOfUserMessage(JsonElement json, string? userMessageId)
    {
        if (!json.TryGetProperty("mapping", out var mapping) || mapping.ValueKind != JsonValueKind.Object)
            return null;

        if (!string.IsNullOrWhiteSpace(userMessageId))
        {
            var currentNode = JsonElementParsing.GetStringOrNull(json, "current_node")
                              ?? JsonElementParsing.GetStringOrNull(json, "currentNode");
            var childId = !string.IsNullOrWhiteSpace(currentNode)
                ? FindDirectChildOnPathTo(mapping, userMessageId, currentNode)
                : null;
            childId ??= PickLatestAssistantChildOfUser(mapping, userMessageId);

            if (!string.IsNullOrWhiteSpace(childId)
                && mapping.TryGetProperty(childId, out var childNode))
            {
                var fromChild = ExtractAssistantTextFromNode(childNode);
                if (!string.IsNullOrWhiteSpace(fromChild))
                    return fromChild;
            }

            // User message is known but its assistant reply is not ready yet — do not fall back
            // to the previous turn's text (that mis-attributes narration to the wrong player line).
            return null;
        }

        return ExtractLastAssistantFromConversation(json);
    }

    public static string? ExtractLastAssistantFromConversation(JsonElement json)
    {
        if (!json.TryGetProperty("mapping", out var mapping) || mapping.ValueKind != JsonValueKind.Object)
            return null;

        string? currentNode = JsonElementParsing.GetStringOrNull(json, "current_node")
                              ?? JsonElementParsing.GetStringOrNull(json, "currentNode");

        if (!string.IsNullOrWhiteSpace(currentNode)
            && mapping.TryGetProperty(currentNode, out var currentEl))
        {
            var fromCurrent = ExtractAssistantTextFromNode(currentEl);
            if (!string.IsNullOrWhiteSpace(fromCurrent))
                return fromCurrent;
        }

        string? bestText = null;
        var bestTime = double.NegativeInfinity;
        foreach (var prop in mapping.EnumerateObject())
        {
            if (!JsonElementParsing.TryGetObjectProperty(prop.Value, "message", out var message))
                continue;

            if (!IsAssistantMessage(message))
                continue;

            var text = ExtractAssistantTextFromNode(prop.Value);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var sortKey = message.TryGetProperty("create_time", out var timeEl) && timeEl.TryGetDouble(out var t)
                ? t
                : double.NegativeInfinity;
            if (sortKey >= bestTime)
            {
                bestTime = sortKey;
                bestText = text;
            }
        }

        return bestText;
    }

    private static string? FindDirectChildOnPathTo(JsonElement mapping, string ancestorId, string targetId)
    {
        var path = BuildPathToNode(mapping, targetId);
        for (var i = 0; i < path.Count - 1; i++)
        {
            if (string.Equals(path[i], ancestorId, StringComparison.Ordinal))
                return path[i + 1];
        }

        return null;
    }

    private static string? PickLatestAssistantChildOfUser(JsonElement mapping, string userMessageId)
    {
        if (!mapping.TryGetProperty(userMessageId, out var userNode)
            || !userNode.TryGetProperty("children", out var childrenEl)
            || childrenEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? bestChild = null;
        var bestTime = double.NegativeInfinity;
        foreach (var childEl in childrenEl.EnumerateArray())
        {
            if (childEl.ValueKind != JsonValueKind.String)
                continue;

            var childId = childEl.GetString();
            if (string.IsNullOrWhiteSpace(childId)
                || !mapping.TryGetProperty(childId, out var childNode)
                || !JsonElementParsing.TryGetObjectProperty(childNode, "message", out var message)
                || !IsAssistantMessage(message))
            {
                continue;
            }

            var sortKey = message.TryGetProperty("create_time", out var timeEl) && timeEl.TryGetDouble(out var t)
                ? t
                : double.NegativeInfinity;
            if (sortKey >= bestTime)
            {
                bestTime = sortKey;
                bestChild = childId;
            }
        }

        return bestChild;
    }

    private static string? ExtractAssistantTextFromNode(JsonElement node)
    {
        if (!JsonElementParsing.TryGetObjectProperty(node, "message", out var message))
            return null;

        if (!IsAssistantMessage(message))
            return null;

        if (!message.TryGetProperty("content", out var content))
            return null;

        var parts = ExtractParts(content);
        if (parts is null)
            return null;

        var text = TextFromParts(parts).Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
