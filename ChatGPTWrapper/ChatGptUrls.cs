using System;
using System.Text.RegularExpressions;

namespace ChatGPTWrapper;

/// <summary>
/// Host and path rules for ChatGPT web (conversations, Projects, library shortcuts).
/// Paths change over time; keep heuristics generous and comment when expanding.
/// </summary>
internal static partial class ChatGptUrls
{
    private static readonly string[] SupportedHosts =
    [
        "chatgpt.com",
        "www.chatgpt.com",
    ];

    [GeneratedRegex(@"/g/(g-[^/]+)", RegexOptions.IgnoreCase)]
    private static partial Regex GizmoPathSegmentRegex();

    [GeneratedRegex(@"/g/g-p-([^/]+)", RegexOptions.IgnoreCase)]
    private static partial Regex GizmoPathLongRegex();

    [GeneratedRegex(@"/g/p-([^/]+)", RegexOptions.IgnoreCase)]
    private static partial Regex GizmoPathShortRegex();

    public static bool IsSupportedHost(Uri uri)
    {
        var h = uri.Host;
        foreach (var host in SupportedHosts)
        {
            if (string.Equals(h, host, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool IsTrustedChatGptTopLevelUri(Uri uri)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return false;

        return IsSupportedHost(uri);
    }

    public static bool TryCreateTrustedNavigationUri(string? rawUrl, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(rawUrl))
            return false;

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var parsed))
            return false;

        if (!IsTrustedChatGptTopLevelUri(parsed))
            return false;

        uri = parsed;
        return true;
    }

    public static bool IsSavableLibraryLocation(Uri uri)
    {
        if (!IsTrustedChatGptTopLevelUri(uri))
            return false;

        return LooksLikeConversationAny(uri)
               || IsProjectWorkspace(uri)
               || LooksLikeLibraryOrAuxiliaryPath(uri.AbsolutePath, uri.Fragment);
    }

    public static bool IsConversationThread(Uri uri) =>
        IsTrustedChatGptTopLevelUri(uri)
        && LooksLikeConversationAny(uri);

    public static bool IsProjectWorkspace(Uri uri) =>
        IsTrustedChatGptTopLevelUri(uri)
        && LooksLikeProjectWorkspace(uri.AbsolutePath, uri.Query, uri.Fragment);

    /// <summary>
    /// Canonical project home for DOM file uploads (<c>/g/g-p-…/project</c>), not a conversation thread.
    /// </summary>
    public static bool IsCanonicalProjectHome(Uri? uri, string? gizmoId = null)
    {
        if (uri is null || !IsTrustedChatGptTopLevelUri(uri))
            return false;

        if (IsConversationThread(uri))
            return false;

        if (!TryParseGizmoId(uri, out var parsed))
            return false;

        if (gizmoId is not null && !GizmoIdsEqual(parsed, gizmoId))
            return false;

        var path = uri.AbsolutePath.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        return path.EndsWith("/project", StringComparison.Ordinal);
    }

    public static bool TryParseGizmoIdFromUserInput(string? input, out string gizmoId)
    {
        gizmoId = "";
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var s = input.Trim();
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) && TryParseGizmoId(uri, out gizmoId))
            return true;

        if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            && TryCreateTrustedNavigationUri(s, out var trusted)
            && TryParseGizmoId(trusted, out gizmoId))
            return true;

        s = s.Trim().TrimEnd('/');
        if (s.StartsWith("g-", StringComparison.OrdinalIgnoreCase) && s.Length > 10)
        {
            gizmoId = s;
            return true;
        }

        if (Guid.TryParse(s, out _))
        {
            gizmoId = s.StartsWith("g-", StringComparison.Ordinal) ? s : "g-" + s;
            return true;
        }

        return false;
    }

    public static bool TryParseGizmoId(Uri uri, out string gizmoId)
    {
        gizmoId = "";
        if (!IsTrustedChatGptTopLevelUri(uri))
            return false;

        if (TryParseGizmoIdFromQuery(uri.Query, out gizmoId))
            return true;

        if (TryParseGizmoIdFromPath(uri.AbsolutePath, out gizmoId))
            return true;

        var fragPath = StripLeadingHashPath(uri.Fragment);
        if (!string.IsNullOrEmpty(fragPath) && TryParseGizmoIdFromPath(fragPath, out gizmoId))
            return true;

        if (TryParseGizmoIdFromQuery(uri.Fragment, out gizmoId))
            return true;

        return false;
    }

    public static string BuildConversationUrl(string conversationId) =>
        $"https://chatgpt.com/c/{Uri.EscapeDataString(conversationId.Trim())}";

    public static bool UsesPathStyleProjectConversationUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        var path = uri.AbsolutePath.Replace('\\', '/');
        return path.Contains("/g/", StringComparison.OrdinalIgnoreCase)
               && path.Contains("/c/", StringComparison.OrdinalIgnoreCase)
               && !path.EndsWith("/project", StringComparison.OrdinalIgnoreCase);
    }

    public static string ResolveProjectConversationUrl(
        string conversationId,
        string gizmoId,
        params string?[] urlHints)
    {
        var id = NormalizeGizmoId(gizmoId);
        var conv = Uri.EscapeDataString(conversationId.Trim());
        var segment = id;
        foreach (var hint in urlHints)
        {
            if (string.IsNullOrWhiteSpace(hint) || !Uri.TryCreate(hint, UriKind.Absolute, out var uri))
                continue;

            if (!TryParseGizmoId(uri, out var fromPath) || !GizmoIdsMatch(fromPath, id))
                continue;

            segment = fromPath;
            break;
        }

        var preferPath = urlHints.Any(UsesPathStyleProjectConversationUrl)
                         || id.StartsWith("g-", StringComparison.Ordinal);

        if (preferPath)
            return $"https://chatgpt.com/g/{segment}/c/{conv}";

        return $"https://chatgpt.com/c/{conv}?project={Uri.EscapeDataString(id)}";
    }

    public static string BuildProjectConversationUrl(string conversationId, string gizmoId) =>
        ResolveProjectConversationUrl(conversationId, gizmoId);

    public static bool IsOnProjectConversationPage(string? source, string conversationId, string gizmoId)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return false;

        if (!IsTrustedChatGptTopLevelUri(uri))
            return false;

        if (!TryParseConversationId(uri, out var parsedConv)
            || !string.Equals(parsedConv, conversationId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryParseGizmoId(uri, out var parsedGizmo)
               && GizmoIdsMatch(parsedGizmo, gizmoId);
    }

    public static bool TryParseConversationId(Uri uri, out string conversationId)
    {
        conversationId = "";
        if (!IsTrustedChatGptTopLevelUri(uri))
            return false;

        if (TryParseConversationIdFromPath(uri.AbsolutePath, out conversationId))
            return true;

        var fragPath = StripLeadingHashPath(uri.Fragment);
        return !string.IsNullOrEmpty(fragPath)
               && TryParseConversationIdFromPath(fragPath, out conversationId);
    }

    public static string BuildProjectUrl(string gizmoId)
    {
        var id = gizmoId.Trim();
        if (id.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return id;

        if (id.StartsWith("g-", StringComparison.Ordinal))
            return $"https://chatgpt.com/g/{id}/project";

        return $"https://chatgpt.com/?project={Uri.EscapeDataString(id)}";
    }

    public static string DescribeLocationKind(Uri uri)
    {
        if (!IsTrustedChatGptTopLevelUri(uri))
            return "Link";

        if (IsConversationThread(uri))
            return "Chat";

        if (IsProjectWorkspace(uri))
            return "Project";

        if (LooksLikeLibraryOrAuxiliaryPath(uri.AbsolutePath, uri.Fragment))
            return "Shortcut";

        return "Link";
    }

    private static bool TryParseGizmoIdFromQuery(string queryOrFragment, out string gizmoId)
    {
        gizmoId = "";
        if (string.IsNullOrEmpty(queryOrFragment))
            return false;

        var q = queryOrFragment.TrimStart('?', '#');
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2)
                continue;

            if (kv[0].Equals("project", StringComparison.OrdinalIgnoreCase)
                || kv[0].Equals("projectId", StringComparison.OrdinalIgnoreCase))
            {
                gizmoId = Uri.UnescapeDataString(kv[1]).Trim();
                return !string.IsNullOrEmpty(gizmoId);
            }
        }

        return false;
    }

    public static bool GizmoIdsEqual(string? left, string? right)
    {
        var a = NormalizeGizmoId(left);
        var b = NormalizeGizmoId(right);
        return !string.IsNullOrEmpty(a)
               && !string.IsNullOrEmpty(b)
               && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when ids match exactly or when one is a titled slug extension of the other
    /// (e.g. <c>g-p-abc-the-king-in-red-black</c> matches <c>g-p-abc</c>).
    /// </summary>
    public static bool GizmoIdsMatch(string? left, string? right)
    {
        if (GizmoIdsEqual(left, right))
            return true;

        var a = NormalizeGizmoId(left);
        var b = NormalizeGizmoId(right);
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;

        return a.StartsWith(b + "-", StringComparison.OrdinalIgnoreCase)
               || b.StartsWith(a + "-", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeGizmoId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "";

        var s = id.Trim().TrimEnd('/');
        if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(s, UriKind.Absolute, out var uri)
            && TryParseGizmoId(uri, out var fromUri))
        {
            return fromUri;
        }

        return s.StartsWith("g-", StringComparison.OrdinalIgnoreCase) ? s : "g-" + s;
    }

    private static bool TryParseGizmoIdFromPath(string absolutePath, out string gizmoId)
    {
        gizmoId = "";
        var p = absolutePath.Replace('\\', '/');

        var m = GizmoPathSegmentRegex().Match(p);
        if (m.Success)
        {
            var seg = m.Groups[1].Value.Trim().TrimEnd('/');
            if (seg.StartsWith("g-", StringComparison.OrdinalIgnoreCase))
            {
                gizmoId = seg;
                return true;
            }
        }

        m = GizmoPathShortRegex().Match(p);
        if (m.Success)
        {
            var hex = m.Groups[1].Value.Trim().TrimEnd('/');
            gizmoId = hex.StartsWith("g-", StringComparison.OrdinalIgnoreCase) ? hex : "g-p-" + hex;
            return !string.IsNullOrEmpty(gizmoId);
        }

        m = GizmoPathLongRegex().Match(p);
        if (m.Success)
        {
            var hex = m.Groups[1].Value.Trim().TrimEnd('/');
            gizmoId = hex.StartsWith("g-", StringComparison.OrdinalIgnoreCase) ? hex : "g-p-" + hex;
            return !string.IsNullOrEmpty(gizmoId);
        }

        return false;
    }

    private static string NormalizeGizmoSegment(string segment)
    {
        var s = segment.Trim().TrimEnd('/');
        var dash = s.IndexOf('-');
        if (dash > 0 && !s.StartsWith("g-", StringComparison.Ordinal))
        {
            var prefix = s[..dash];
            if (prefix.Length >= 8)
                return prefix.StartsWith("g-", StringComparison.Ordinal) ? prefix : "g-" + prefix;
        }

        return s.StartsWith("g-", StringComparison.Ordinal) ? s : "g-" + s;
    }

    private static bool LooksLikeConversationAny(Uri uri)
    {
        if (LooksLikeConversationPath(uri.AbsolutePath))
            return true;

        var fragPath = StripLeadingHashPath(uri.Fragment);
        return !string.IsNullOrEmpty(fragPath) && LooksLikeConversationPath(fragPath);
    }

    private static bool LooksLikeConversationPath(string absolutePath)
    {
        var p = absolutePath.Replace('\\', '/').TrimEnd('/');
        return p.Contains("/c/", StringComparison.OrdinalIgnoreCase)
               || p.EndsWith("/c", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseConversationIdFromPath(string absolutePath, out string conversationId)
    {
        conversationId = "";
        var p = absolutePath.Replace('\\', '/').TrimEnd('/');
        var marker = "/c/";
        var idx = p.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return false;

        var tail = p[(idx + marker.Length)..];
        var slash = tail.IndexOf('/');
        if (slash >= 0)
            tail = tail[..slash];

        conversationId = Uri.UnescapeDataString(tail.Trim());
        return !string.IsNullOrWhiteSpace(conversationId);
    }

    private static bool LooksLikeProjectWorkspace(string absolutePath, string query, string fragment)
    {
        if (ContainsProjectQueryParam(query) || ContainsProjectQueryParam(fragment))
            return true;

        if (PathLooksLikeProjectRoute(absolutePath))
            return true;

        var fragPath = StripLeadingHashPath(fragment);
        return !string.IsNullOrEmpty(fragPath) && PathLooksLikeProjectRoute(fragPath);
    }

    private static bool ContainsProjectQueryParam(string queryOrFragment)
    {
        if (string.IsNullOrEmpty(queryOrFragment))
            return false;

        return queryOrFragment.Contains("project=", StringComparison.OrdinalIgnoreCase)
               || queryOrFragment.Contains("projectId=", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathLooksLikeProjectRoute(string absolutePath)
    {
        var p = absolutePath.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        if (p.Length <= 1)
            return false;

        if (p.Contains("/g/p-", StringComparison.Ordinal)
            || p.Contains("/g/p/", StringComparison.Ordinal))
            return true;

        if (p.Contains("/g/g-p-", StringComparison.Ordinal))
            return true;

        if (p.StartsWith("/g/", StringComparison.Ordinal)
            && p.EndsWith("/project", StringComparison.Ordinal))
            return true;

        if (p.StartsWith("/project", StringComparison.Ordinal))
            return true;

        return false;
    }

    private static bool LooksLikeLibraryOrAuxiliaryPath(string absolutePath, string fragment)
    {
        if (PathLooksLikeLibraryRoute(absolutePath))
            return true;

        var fragPath = StripLeadingHashPath(fragment);
        return !string.IsNullOrEmpty(fragPath) && PathLooksLikeLibraryRoute(fragPath);
    }

    private static bool PathLooksLikeLibraryRoute(string absolutePath)
    {
        var p = absolutePath.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
        return p.EndsWith("/library", StringComparison.Ordinal)
               || p.Equals("/library", StringComparison.Ordinal)
               || p.StartsWith("/library/", StringComparison.Ordinal)
               || p.StartsWith("/gpts", StringComparison.Ordinal);
    }

    private static string StripLeadingHashPath(string fragment)
    {
        if (string.IsNullOrEmpty(fragment))
            return "";

        var s = fragment.StartsWith('#') ? fragment[1..] : fragment;
        if (string.IsNullOrEmpty(s))
            return "";

        return s.StartsWith('/') ? s : "/" + s;
    }
}
