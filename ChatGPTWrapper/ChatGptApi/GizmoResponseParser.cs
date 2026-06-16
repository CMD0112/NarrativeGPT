using System.Text.Json;

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// Parses gizmo/project payloads from sidebar, detail, bootstrap, and upsert responses.
/// </summary>
internal static class GizmoResponseParser
{
    internal static IReadOnlyList<GizmoSummary> ParseSidebarItems(JsonElement items)
    {
        var list = new List<GizmoSummary>();
        foreach (var item in items.EnumerateArray())
        {
            try
            {
                var summary = TryParseSidebarItem(item);
                if (summary is not null)
                    list.Add(summary);
            }
            catch (Exception ex)
            {
                ProjectLinkDiagnostics.Log($"Sidebar item parse skipped: {ex.Message}");
            }
        }

        return list;
    }

    internal static GizmoSummary? TryParseSidebarItem(JsonElement item)
    {
        JsonElement node;
        JsonElement filesContext;

        if (item.TryGetProperty("gizmo", out var gizmoWrap))
        {
            filesContext = gizmoWrap;
            if (gizmoWrap.TryGetProperty("gizmo", out var inner))
                node = inner.TryGetProperty("id", out _) ? inner : gizmoWrap;
            else if (gizmoWrap.TryGetProperty("id", out _))
                node = gizmoWrap;
            else
                return null;
        }
        else if (item.TryGetProperty("id", out _))
        {
            node = item;
            filesContext = item;
        }
        else
        {
            return null;
        }

        return ParseGizmoNode(node, filesContext);
    }

    internal static GizmoSummary? ParseGizmoNode(JsonElement node, JsonElement filesContext)
    {
        var id = JsonElementParsing.GetStringOrNull(node, "id");
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var title = ResolveGizmoTitle(node, filesContext);
        var instructions = JsonElementParsing.GetStringOrNull(node, "instructions");
        var files = ParseFilesFromGizmoContext(node, filesContext);

        return new GizmoSummary
        {
            Id = id,
            Title = title,
            Instructions = instructions,
            Files = files,
        };
    }

    internal static List<GizmoFileRef> ParseFilesFromJson(JsonElement json)
    {
        if (json.TryGetProperty("gizmo", out var gizmoWrap))
        {
            if (gizmoWrap.TryGetProperty("gizmo", out var inner))
                return ParseFilesFromGizmoContext(inner, gizmoWrap);
            return ParseFilesFromGizmoContext(gizmoWrap, gizmoWrap);
        }

        return ParseFilesFromGizmoContext(json, json);
    }

    internal static List<GizmoFileRef> ParseFilesFromGizmoContext(JsonElement node, JsonElement filesContext)
    {
        var files = new List<GizmoFileRef>();

        foreach (var source in new[] { filesContext, node })
            AppendFileRefsFromNode(files, source);

        return files;
    }

    internal static void AppendFileRefsFromNode(List<GizmoFileRef> files, JsonElement source)
    {
        if (source.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in source.EnumerateArray())
                AddFileRef(files, f);
            return;
        }

        if (source.ValueKind != JsonValueKind.Object)
            return;

        foreach (var propertyName in new[]
                 {
                     "files",
                     "training_files",
                     "knowledge_files",
                     "file_requirements",
                     "resources",
                 })
        {
            if (source.TryGetProperty(propertyName, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in arr.EnumerateArray())
                    AddFileRef(files, f);
            }
        }

        if (source.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Object)
            AppendFileRefsFromNode(files, version);

        if (source.TryGetProperty("latest_version", out var latestVersion)
            && latestVersion.ValueKind == JsonValueKind.Object)
        {
            AppendFileRefsFromNode(files, latestVersion);
        }
    }

    internal static List<GizmoFileRef> CollectFileRefsDeep(JsonElement root, int maxDepth = 12)
    {
        var files = new List<GizmoFileRef>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Walk(root, 0);

        return files;

        void Walk(JsonElement element, int depth)
        {
            if (depth > maxDepth)
                return;

            if (element.ValueKind == JsonValueKind.Object)
            {
                if (TryAddFileObject(element))
                {
                    foreach (var prop in element.EnumerateObject())
                        Walk(prop.Value, depth + 1);
                    return;
                }

                foreach (var prop in element.EnumerateObject())
                    Walk(prop.Value, depth + 1);
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    Walk(item, depth + 1);
            }
        }

        bool TryAddFileObject(JsonElement obj)
        {
            var fileId = JsonElementParsing.GetStringOrNull(obj, "file_id");
            if (string.IsNullOrWhiteSpace(fileId))
            {
                var id = JsonElementParsing.GetStringOrNull(obj, "id");
                if (string.IsNullOrWhiteSpace(id) || !LooksLikeUploadFileId(id))
                    return false;

                fileId = id;
            }

            if (!seen.Add(fileId))
                return true;

            AddFileRef(files, obj);
            return true;
        }

        static bool LooksLikeUploadFileId(string id) =>
            id.StartsWith("file_", StringComparison.Ordinal)
            || id.StartsWith("file-", StringComparison.Ordinal);
    }

    internal static void AddFileRef(List<GizmoFileRef> files, JsonElement f)
    {
        if (f.ValueKind != JsonValueKind.Object)
            return;

        var fileId = JsonElementParsing.GetStringOrNull(f, "file_id")
                       ?? JsonElementParsing.GetStringOrNull(f, "id");
        if (string.IsNullOrWhiteSpace(fileId))
            return;

        var name = JsonElementParsing.GetStringOrNull(f, "name") ?? fileId;
        var size = JsonElementParsing.GetInt64OrNull(f, "size", "bytes", "size_bytes");

        files.Add(new GizmoFileRef
        {
            FileId = fileId,
            Name = name,
            Location = ResolveFileLocationFromJson(f, fileId),
            Size = size,
        });
    }

    internal static byte[]? TryExtractInlineFileContent(JsonElement root, string fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            return null;

        byte[]? found = null;
        WalkForInlineFileContent(root, fileId, ref found);
        return found;
    }

    internal static string? TryExtractInlineDownloadPath(JsonElement root, string fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
            return null;

        string? found = null;
        WalkForInlineDownloadPath(root, fileId, ref found);
        return found;
    }

    private static void WalkForInlineFileContent(JsonElement node, string fileId, ref byte[]? found)
    {
        if (found is not null)
            return;

        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
            {
                if (TryReadInlineBytesFromFileNode(node, fileId, out var bytes))
                {
                    found = bytes;
                    return;
                }

                foreach (var propertyName in new[]
                         {
                             "files",
                             "training_files",
                             "knowledge_files",
                             "file_requirements",
                             "resources",
                         })
                {
                    if (node.TryGetProperty(propertyName, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in arr.EnumerateArray())
                            WalkForInlineFileContent(item, fileId, ref found);
                    }
                }

                if (node.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Object)
                    WalkForInlineFileContent(version, fileId, ref found);

                if (node.TryGetProperty("latest_version", out var latestVersion)
                    && latestVersion.ValueKind == JsonValueKind.Object)
                {
                    WalkForInlineFileContent(latestVersion, fileId, ref found);
                }

                foreach (var prop in node.EnumerateObject())
                    WalkForInlineFileContent(prop.Value, fileId, ref found);
                break;
            }
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                    WalkForInlineFileContent(item, fileId, ref found);
                break;
        }
    }

    private static void WalkForInlineDownloadPath(JsonElement node, string fileId, ref string? found)
    {
        if (found is not null)
            return;

        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
            {
                if (TryReadInlineDownloadPathFromFileNode(node, fileId, out var path))
                {
                    found = path;
                    return;
                }

                foreach (var propertyName in new[]
                         {
                             "files",
                             "training_files",
                             "knowledge_files",
                             "file_requirements",
                             "resources",
                         })
                {
                    if (node.TryGetProperty(propertyName, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in arr.EnumerateArray())
                            WalkForInlineDownloadPath(item, fileId, ref found);
                    }
                }

                if (node.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Object)
                    WalkForInlineDownloadPath(version, fileId, ref found);

                if (node.TryGetProperty("latest_version", out var latestVersion)
                    && latestVersion.ValueKind == JsonValueKind.Object)
                {
                    WalkForInlineDownloadPath(latestVersion, fileId, ref found);
                }

                foreach (var prop in node.EnumerateObject())
                    WalkForInlineDownloadPath(prop.Value, fileId, ref found);
                break;
            }
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                    WalkForInlineDownloadPath(item, fileId, ref found);
                break;
        }
    }

    private static bool TryReadInlineBytesFromFileNode(JsonElement node, string fileId, out byte[]? bytes)
    {
        bytes = null;
        if (node.ValueKind != JsonValueKind.Object)
            return false;

        var id = JsonElementParsing.GetStringOrNull(node, "file_id")
                 ?? JsonElementParsing.GetStringOrNull(node, "id");
        if (!string.IsNullOrWhiteSpace(id) && string.Equals(id, fileId, StringComparison.Ordinal))
        {
            if (TryDecodeInlinePayload(node, out bytes))
                return true;
        }

        foreach (var nestedName in new[] { "file", "resource" })
        {
            if (!node.TryGetProperty(nestedName, out var nested) || nested.ValueKind != JsonValueKind.Object)
                continue;

            var nestedId = JsonElementParsing.GetStringOrNull(nested, "file_id")
                           ?? JsonElementParsing.GetStringOrNull(nested, "id");
            if (!string.IsNullOrWhiteSpace(nestedId)
                && string.Equals(nestedId, fileId, StringComparison.Ordinal)
                && TryDecodeInlinePayload(nested, out bytes))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadInlineDownloadPathFromFileNode(JsonElement node, string fileId, out string? path)
    {
        path = null;
        if (node.ValueKind != JsonValueKind.Object)
            return false;

        var id = JsonElementParsing.GetStringOrNull(node, "file_id")
                 ?? JsonElementParsing.GetStringOrNull(node, "id");
        if (string.IsNullOrWhiteSpace(id) || !string.Equals(id, fileId, StringComparison.Ordinal))
            return false;

        foreach (var propertyName in new[] { "download_url", "url", "href" })
        {
            if (!node.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
                continue;

            var candidate = NormalizeSameOriginDownloadPath(value.GetString());
            if (candidate is not null)
            {
                path = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryDecodeInlinePayload(JsonElement node, out byte[]? bytes)
    {
        bytes = null;
        foreach (var propertyName in new[] { "text", "content", "source", "body" })
        {
            if (!node.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
                continue;

            var text = value.GetString();
            if (!string.IsNullOrEmpty(text))
            {
                bytes = System.Text.Encoding.UTF8.GetBytes(text);
                return true;
            }
        }

        foreach (var propertyName in new[] { "base64", "data" })
        {
            if (!node.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
                continue;

            var encoded = value.GetString();
            if (string.IsNullOrEmpty(encoded))
                continue;

            try
            {
                bytes = Convert.FromBase64String(encoded);
                return true;
            }
            catch
            {
                /* ignore invalid base64 */
            }
        }

        return false;
    }

    internal static string? NormalizeSameOriginDownloadPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (raw.StartsWith("/backend-api/", StringComparison.Ordinal))
            return raw;

        if (Uri.TryCreate(raw, UriKind.Absolute, out var absolute)
            && absolute.Host.Equals("chatgpt.com", StringComparison.OrdinalIgnoreCase)
            && absolute.AbsolutePath.StartsWith("/backend-api/", StringComparison.Ordinal))
        {
            return absolute.PathAndQuery;
        }

        return null;
    }

    internal static string? TryExtractUpsertGizmoId(JsonElement json)
    {
        if (json.TryGetProperty("resource", out var resource)
            && resource.TryGetProperty("gizmo", out var resourceGizmo))
        {
            if (resourceGizmo.TryGetProperty("gizmo", out var inner)
                && inner.TryGetProperty("id", out var innerId)
                && innerId.ValueKind == JsonValueKind.String)
            {
                return innerId.GetString();
            }

            if (resourceGizmo.TryGetProperty("id", out var resourceId)
                && resourceId.ValueKind == JsonValueKind.String)
            {
                return resourceId.GetString();
            }
        }

        return TryParseGizmoFromUpsert(json)?.Id;
    }

    internal static GizmoSummary? TryParseGizmoFromUpsert(JsonElement json)
    {
        if (json.TryGetProperty("resource", out var resource)
            && resource.TryGetProperty("gizmo", out var resourceGizmo))
        {
            if (resourceGizmo.TryGetProperty("gizmo", out var inner))
                return ParseGizmoNode(inner, resourceGizmo);
            return ParseGizmoNode(resourceGizmo, resourceGizmo);
        }

        if (json.TryGetProperty("gizmo", out var wrap))
        {
            if (wrap.TryGetProperty("gizmo", out var inner))
                return ParseGizmoNode(inner, wrap);
            return ParseGizmoNode(wrap, wrap);
        }

        if (json.TryGetProperty("id", out _))
            return ParseGizmoNode(json, json);

        return null;
    }

    internal static IReadOnlyList<GizmoSummary> ParseBootstrapGizmos(JsonElement root)
    {
        var byId = new Dictionary<string, GizmoSummary>(StringComparer.Ordinal);
        foreach (var prop in new[] { "gizmos", "items", "resources" })
        {
            if (!root.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in arr.EnumerateArray())
            {
                GizmoSummary? summary;
                try
                {
                    summary = TryParseSidebarItem(item)
                              ?? (item.TryGetProperty("gizmo", out var wrap)
                                  ? TryParseSidebarItem(wrap)
                                  : null)
                              ?? (item.TryGetProperty("id", out _)
                                  ? ParseGizmoNode(
                                      item.TryGetProperty("gizmo", out var inner) ? inner : item,
                                      item)
                                  : null);
                }
                catch (Exception ex)
                {
                    ProjectLinkDiagnostics.Log($"Bootstrap gizmo parse skipped: {ex.Message}");
                    continue;
                }

                if (summary is not null)
                    byId[summary.Id] = summary;
            }
        }

        WalkBootstrapTree(root, byId, depth: 0);
        return byId.Values.ToList();
    }

    private static string ResolveGizmoTitle(JsonElement node, JsonElement fallback)
    {
        foreach (var el in new[] { node, fallback })
        {
            if (el.TryGetProperty("display", out var disp)
                && disp.TryGetProperty("name", out var nameEl))
            {
                var n = JsonElementParsing.GetStringOrNull(nameEl);
                if (!string.IsNullOrWhiteSpace(n))
                    return n;
            }

            var direct = JsonElementParsing.GetStringOrNull(el, "name");
            if (!string.IsNullOrWhiteSpace(direct))
                return direct;
        }

        return "Project";
    }

    private static string? ResolveFileLocationFromJson(JsonElement fileNode, string fileId)
    {
        if (fileNode.TryGetProperty("location", out var location)
            && location.ValueKind == JsonValueKind.String)
        {
            return location.GetString();
        }

        var uri = JsonElementParsing.GetStringOrNull(fileNode, "uri");
        if (!string.IsNullOrWhiteSpace(uri))
            return uri;

        var pointer = JsonElementParsing.GetStringOrNull(fileNode, "asset_pointer");
        if (!string.IsNullOrWhiteSpace(pointer))
            return pointer;

        return ChatGptProjectApiService.DefaultUpsertFileLocation;
    }

    private static void WalkBootstrapTree(JsonElement el, Dictionary<string, GizmoSummary> byId, int depth)
    {
        if (depth > 12)
            return;

        if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty("id", out _))
            {
                try
                {
                    var summary = TryParseSidebarItem(el) ?? ParseGizmoNode(el, el);
                    if (summary is not null)
                        byId[summary.Id] = summary;
                }
                catch (Exception ex)
                {
                    ProjectLinkDiagnostics.Log($"Bootstrap tree parse skipped: {ex.Message}");
                }
            }

            foreach (var prop in el.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    WalkBootstrapTree(prop.Value, byId, depth + 1);
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
                WalkBootstrapTree(item, byId, depth + 1);
        }
    }
}
