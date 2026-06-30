using System.Text.Json;

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// Extracts structured messages from the active branch of a ChatGPT conversation mapping tree.
/// </summary>
public static class ConversationBranchExtractor
{
    public static IReadOnlyList<ConversationBranchMessage> ExtractActiveBranch(JsonElement conversationJson)
    {
        if (!conversationJson.TryGetProperty("mapping", out var mapping)
            || mapping.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var currentNode = ResolveCurrentNode(conversationJson, mapping);
        if (string.IsNullOrWhiteSpace(currentNode))
            return [];

        var path = ResolveActivePath(mapping, currentNode);
        if (path.Count == 0)
            return [];

        var messages = BuildMessagesFromPath(mapping, path);
        if (messages.Count <= 1)
        {
            var fallback = BuildMessagesSortedByTime(mapping);
            if (fallback.Count > messages.Count)
                messages = fallback;
        }

        return messages;
    }

    private static List<ConversationBranchMessage> BuildMessagesFromPath(
        JsonElement mapping,
        IReadOnlyList<string> path)
    {
        var messages = new List<ConversationBranchMessage>();
        var branchIndex = 0;
        foreach (var nodeId in path)
        {
            if (!mapping.TryGetProperty(nodeId, out var nodeEl))
                continue;

            var message = TryBuildBranchMessage(mapping, nodeId, nodeEl, branchIndex);
            if (message is null)
                continue;

            messages.Add(message);
            branchIndex++;
        }

        return messages;
    }

    private static List<ConversationBranchMessage> BuildMessagesSortedByTime(JsonElement mapping)
    {
        var ordered = new List<(double SortKey, int Index, ConversationBranchMessage Message)>();
        var index = 0;
        foreach (var prop in mapping.EnumerateObject())
        {
            var message = TryBuildBranchMessage(mapping, prop.Name, prop.Value, index);
            if (message is null)
                continue;

            var sortKey = prop.Value.TryGetProperty("message", out var msgEl)
                          && msgEl.TryGetProperty("create_time", out var timeEl)
                          && timeEl.TryGetDouble(out var t)
                ? t
                : double.PositiveInfinity;
            ordered.Add((sortKey, index++, message));
        }

        var sorted = ordered
            .OrderBy(item => item.SortKey)
            .ThenBy(item => item.Index)
            .ToList();

        var messages = new List<ConversationBranchMessage>();
        for (var i = 0; i < sorted.Count; i++)
        {
            var item = sorted[i].Message;
            messages.Add(new ConversationBranchMessage
            {
                NodeId = item.NodeId,
                MessageId = item.MessageId,
                Role = item.Role,
                RawText = item.RawText,
                DisplayText = item.DisplayText,
                ParentNodeId = item.ParentNodeId,
                BranchIndex = i,
                CreateTime = item.CreateTime,
                IsUtility = item.IsUtility,
                IsInjectedContext = item.IsInjectedContext,
            });
        }

        return messages;
    }

    private static ConversationBranchMessage? TryBuildBranchMessage(
        JsonElement mapping,
        string nodeId,
        JsonElement nodeEl,
        int branchIndex)
    {
        if (!JsonElementParsing.TryGetObjectProperty(nodeEl, "message", out var message))
            return null;

        var role = GetMessageRole(message);
        if (role is not ("user" or "assistant"))
            return null;

        var rawText = ExtractRawMessageText(message);
        if (string.IsNullOrWhiteSpace(rawText))
            return null;

        var displayText = role == "user"
            ? ConversationStreamParser.ExtractTranscriptPlayerText(rawText) ?? rawText
            : rawText;

        string? parentNodeId = null;
        if (nodeEl.TryGetProperty("parent", out var parentEl) && parentEl.ValueKind == JsonValueKind.String)
            parentNodeId = parentEl.GetString();

        double? createTime = null;
        if (message.TryGetProperty("create_time", out var timeEl) && timeEl.TryGetDouble(out var t))
            createTime = t;

        var messageId = JsonElementParsing.GetStringOrNull(message, "id");

        return new ConversationBranchMessage
        {
            NodeId = nodeId,
            MessageId = messageId,
            Role = role,
            RawText = rawText,
            DisplayText = displayText,
            ParentNodeId = parentNodeId,
            BranchIndex = branchIndex,
            CreateTime = createTime,
            IsUtility = role == "user"
                ? ConversationStreamParser.IsUtilityUserMessage(rawText)
                : ConversationStreamParser.IsUtilityAssistantMessage(rawText),
            IsInjectedContext = role == "user"
                && ConversationStreamParser.IsInjectedContextUserMessage(rawText),
        };
    }

    private static List<string> ResolveActivePath(JsonElement mapping, string currentNode)
    {
        var parentPath = BuildPathToNode(mapping, currentNode);
        if (parentPath.Count > 1)
            return parentPath;

        var root = FindRootNode(mapping);
        if (!string.IsNullOrWhiteSpace(root) && !string.Equals(root, currentNode, StringComparison.Ordinal))
        {
            var childPath = new List<string>();
            if (TryBuildChildPath(mapping, root, currentNode, childPath) && childPath.Count > 1)
                return childPath;
        }

        return parentPath;
    }

    private static string? ExtractRawMessageText(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object)
            return null;

        if (!message.TryGetProperty("content", out var content))
            return null;

        var parts = ExtractParts(content);
        if (parts is null)
            return null;

        var text = string.Concat(parts);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static List<string>? ExtractParts(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Object)
            return null;

        if (!content.TryGetProperty("parts", out var partsEl) || partsEl.ValueKind != JsonValueKind.Array)
            return null;

        var parts = new List<string>();
        foreach (var part in partsEl.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
                parts.Add(part.GetString() ?? "");
        }

        return parts;
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
}
