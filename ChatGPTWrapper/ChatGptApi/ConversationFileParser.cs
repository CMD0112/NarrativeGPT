using System.Text.Json;
using System.Text.RegularExpressions;

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// Extracts file references from ChatGPT conversation JSON (mapping tree).
/// </summary>
internal static partial class ConversationFileParser
{
    private static readonly HashSet<string> FileIdPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "file_id",
        "fileId",
        "id",
    };

    public static IReadOnlyList<ConversationFileRef> ExtractFiles(JsonElement conversationJson)
    {
        if (!conversationJson.TryGetProperty("mapping", out var mapping)
            || mapping.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var results = new List<ConversationFileRef>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var nodeProp in mapping.EnumerateObject())
        {
            if (!nodeProp.Value.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var messageId = JsonElementParsing.GetStringOrNull(message, "id") ?? nodeProp.Name;
            var role = message.TryGetProperty("author", out var author)
                       && author.TryGetProperty("role", out var roleEl)
                ? roleEl.GetString()
                : null;

            WalkMessage(message, messageId, role, results, seen);
        }

        return results;
    }

    private static void WalkMessage(
        JsonElement message,
        string messageId,
        string? role,
        List<ConversationFileRef> results,
        HashSet<string> seen)
    {
        if (message.TryGetProperty("metadata", out var metadata)
            && metadata.TryGetProperty("attachments", out var metadataAttachments))
        {
            WalkAttachmentsArray(metadataAttachments, "metadata.attachments", messageId, role, results, seen);
        }

        if (message.TryGetProperty("content", out var content))
        {
            if (content.TryGetProperty("attachments", out var contentAttachments))
                WalkAttachmentsArray(contentAttachments, "content.attachments", messageId, role, results, seen);

            WalkContentParts(content, messageId, role, results, seen);
        }

        WalkObjectForFileRefs(message, "message", messageId, role, results, seen, depth: 0);
    }

    private static void WalkContentParts(
        JsonElement content,
        string messageId,
        string? role,
        List<ConversationFileRef> results,
        HashSet<string> seen)
    {
        if (!content.TryGetProperty("parts", out var parts) || parts.ValueKind != JsonValueKind.Array)
            return;

        var index = 0;
        foreach (var part in parts.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                var text = part.GetString();
                ExtractFileCitesFromText(text, messageId, role, results, seen);
                ExtractSandboxPathsFromText(text, messageId, role, results, seen);
            }
            else if (part.ValueKind == JsonValueKind.Object)
            {
                TryAddFileRef(
                    part,
                    $"content.parts[{index}]",
                    messageId,
                    role,
                    results,
                    seen);
            }

            index++;
        }
    }

    private static void WalkAttachmentsArray(
        JsonElement attachments,
        string source,
        string messageId,
        string? role,
        List<ConversationFileRef> results,
        HashSet<string> seen)
    {
        if (attachments.ValueKind != JsonValueKind.Array)
            return;

        var index = 0;
        foreach (var attachment in attachments.EnumerateArray())
        {
            if (attachment.ValueKind == JsonValueKind.Object)
            {
                TryAddFileRef(
                    attachment,
                    $"{source}[{index}]",
                    messageId,
                    role,
                    results,
                    seen);
            }

            index++;
        }
    }

    private static void WalkObjectForFileRefs(
        JsonElement node,
        string source,
        string messageId,
        string? role,
        List<ConversationFileRef> results,
        HashSet<string> seen,
        int depth)
    {
        if (depth > 8 || node.ValueKind != JsonValueKind.Object)
            return;

        TryAddFileRef(node, source, messageId, role, results, seen);

        foreach (var prop in node.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object)
                WalkObjectForFileRefs(prop.Value, $"{source}.{prop.Name}", messageId, role, results, seen, depth + 1);
            else if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                var i = 0;
                foreach (var item in prop.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        WalkObjectForFileRefs(
                            item,
                            $"{source}.{prop.Name}[{i}]",
                            messageId,
                            role,
                            results,
                            seen,
                            depth + 1);
                    }

                    i++;
                }
            }
        }
    }

    private static void TryAddFileRef(
        JsonElement node,
        string source,
        string messageId,
        string? role,
        List<ConversationFileRef> results,
        HashSet<string> seen)
    {
        var fileId = ResolveFileId(node);
        if (string.IsNullOrWhiteSpace(fileId) || !seen.Add(fileId))
            return;

        var assetPointer = JsonElementParsing.GetStringOrNull(node, "asset_pointer")
                           ?? JsonElementParsing.GetStringOrNull(node, "assetPointer");
        var name = JsonElementParsing.GetStringOrNull(node, "name")
                   ?? JsonElementParsing.GetStringOrNull(node, "file_name")
                   ?? JsonElementParsing.GetStringOrNull(node, "filename");
        var mime = JsonElementParsing.GetStringOrNull(node, "mime_type")
                   ?? JsonElementParsing.GetStringOrNull(node, "mimeType")
                   ?? JsonElementParsing.GetStringOrNull(node, "content_type");
        var location = JsonElementParsing.GetStringOrNull(node, "location");
        var sandboxPath = JsonElementParsing.GetStringOrNull(node, "sandbox_path")
                          ?? JsonElementParsing.GetStringOrNull(node, "sandboxPath");

        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(assetPointer))
            name = ExtractNameFromAssetPointer(assetPointer);

        if (string.IsNullOrWhiteSpace(sandboxPath) && fileId.StartsWith("/mnt/data/", StringComparison.Ordinal))
            sandboxPath = fileId;

        results.Add(new ConversationFileRef
        {
            FileId = fileId,
            Name = name ?? ExtractNameFromSandboxPath(sandboxPath),
            MimeType = mime,
            Location = location,
            AssetPointer = assetPointer,
            MessageId = messageId,
            AuthorRole = role,
            SandboxPath = sandboxPath,
            Source = source,
        });
    }

    private static string? ResolveFileId(JsonElement node)
    {
        foreach (var name in FileIdPropertyNames)
        {
            var value = JsonElementParsing.GetStringOrNull(node, name);
            if (IsPlausibleFileId(value))
                return value;
        }

        var pointer = JsonElementParsing.GetStringOrNull(node, "asset_pointer")
                      ?? JsonElementParsing.GetStringOrNull(node, "assetPointer");
        return ExtractFileIdFromAssetPointer(pointer);
    }

    internal static bool IsPlausibleFileId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (value.StartsWith("file-", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("file_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return value.Contains("file", StringComparison.OrdinalIgnoreCase)
               && value.Length >= 8
               && !Guid.TryParse(value, out _);
    }

    internal static string? ExtractFileIdFromAssetPointer(string? pointer)
    {
        if (string.IsNullOrWhiteSpace(pointer))
            return null;

        var match = AssetPointerFileIdRegex().Match(pointer);
        return match.Success ? match.Groups["id"].Value : null;
    }

    private static string? ExtractNameFromAssetPointer(string pointer)
    {
        var slash = pointer.LastIndexOf('/');
        return slash >= 0 && slash < pointer.Length - 1
            ? pointer[(slash + 1)..]
            : null;
    }

    private static void ExtractSandboxPathsFromText(
        string? text,
        string messageId,
        string? role,
        List<ConversationFileRef> results,
        HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        foreach (Match match in SandboxPathRegex().Matches(text))
        {
            var sandboxPath = match.Groups["path"].Value;
            if (!seen.Add($"sandbox:{sandboxPath}"))
                continue;

            results.Add(new ConversationFileRef
            {
                FileId = sandboxPath,
                Name = ExtractNameFromSandboxPath(sandboxPath),
                SandboxPath = sandboxPath,
                MessageId = messageId,
                AuthorRole = role,
                Source = "content.parts.sandbox_path",
            });
        }
    }

    private static string? ExtractNameFromSandboxPath(string? sandboxPath)
    {
        if (string.IsNullOrWhiteSpace(sandboxPath))
            return null;

        var slash = sandboxPath.LastIndexOf('/');
        return slash >= 0 && slash < sandboxPath.Length - 1
            ? sandboxPath[(slash + 1)..]
            : sandboxPath;
    }

    private static void ExtractFileCitesFromText(
        string? text,
        string messageId,
        string? role,
        List<ConversationFileRef> results,
        HashSet<string> seen)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        foreach (Match match in FileCiteTokenRegex().Matches(text))
        {
            var token = match.Value;
            if (!seen.Add($"filecite:{token}"))
                continue;

            results.Add(new ConversationFileRef
            {
                FileId = token,
                Name = token,
                MessageId = messageId,
                AuthorRole = role,
                Source = "content.parts.filecite",
            });
        }
    }

    [GeneratedRegex(@"file-service://(?<id>[^\s""']+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AssetPointerFileIdRegex();

    [GeneratedRegex(@"filecite[\w-]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FileCiteTokenRegex();

    [GeneratedRegex(@"(?<path>/mnt/data/[^\s""'<>]+)", RegexOptions.CultureInvariant)]
    private static partial Regex SandboxPathRegex();
}
