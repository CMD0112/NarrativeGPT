using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// Observes backend-api traffic and records endpoint capability outcomes for drift debugging.
/// </summary>
internal static class ChatGptApiDiscovery
{
    private static readonly object Gate = new();
    private static readonly HashSet<CoreWebView2> RegisteredCores = new();

    public static string CapabilitiesPath => Path.Combine(AppDirectories.Root, "api-capabilities.json");

    public static string DiscoveryLogPath => Path.Combine(AppDirectories.Root, "api-discovery-log.jsonl");

    public static void Register(CoreWebView2 core)
    {
        lock (Gate)
        {
            if (RegisteredCores.Contains(core))
                return;

            try
            {
                core.AddWebResourceRequestedFilter(
                    "https://chatgpt.com/backend-api/*",
                    CoreWebView2WebResourceContext.All);

                core.WebResourceRequested += OnWebResourceRequested;
                RegisteredCores.Add(core);
                ChatGptApiSendSampleCapture.Register(core);
                ChatGptWebViewFileDiagnostics.Register(core);
            }
            catch
            {
                /* filter may already exist */
            }
        }
    }

    private static void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            var uri = e.Request.Uri;
            if (!uri.Contains("backend-api", StringComparison.OrdinalIgnoreCase))
                return;

            var method = e.Request.Method ?? "GET";
            var path = ExtractPath(uri);
            if (string.IsNullOrEmpty(path))
                return;

            CaptureRequestHeaders(e.Request, path);

            if (IsInteresting(path))
                AppendDiscoveryLog(method, path);

            if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                && path.Contains("/files", StringComparison.OrdinalIgnoreCase)
                && path.Contains("/projects/", StringComparison.OrdinalIgnoreCase))
            {
                SetPreferredUploadAttachPath(path);
            }
        }
        catch
        {
            /* ignore */
        }
    }

    public static void RecordSuccess(string path, string method)
    {
        UpdateCapability(path, method, success: true);
    }

    public static void RecordFailure(string path, string method, int? status)
    {
        UpdateCapability(path, method, success: false, status);
    }

    public static string? GetPreferredFileDeleteTemplate()
    {
        lock (Gate)
        {
            var doc = LoadCapabilities();
            return string.IsNullOrWhiteSpace(doc.PreferredFileDeleteTemplate)
                ? null
                : doc.PreferredFileDeleteTemplate;
        }
    }

    public static void SetPreferredFileDeleteTemplate(string template)
    {
        try
        {
            AppDirectories.EnsureCreated();
            lock (Gate)
            {
                var doc = LoadCapabilities();
                doc.PreferredFileDeleteTemplate = template;
                doc.UpdatedAt = DateTimeOffset.UtcNow;
                File.WriteAllText(CapabilitiesPath, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch
        {
            /* ignore */
        }
    }

    public static string? GetPreferredUploadAttachPath()
    {
        lock (Gate)
        {
            var doc = LoadCapabilities();
            var path = doc.PreferredUploadAttachPath;
            if (string.IsNullOrWhiteSpace(path))
                return null;

            // Legacy discovery recorded /gizmos/{id}/files which 404s for snorlax projects.
            if (path.Contains("/gizmos/", StringComparison.OrdinalIgnoreCase)
                && path.Contains("/files", StringComparison.OrdinalIgnoreCase))
                return null;

            return path;
        }
    }

    public static void SetPreferredUploadAttachPath(string path)
    {
        try
        {
            AppDirectories.EnsureCreated();
            lock (Gate)
            {
                var doc = LoadCapabilities();
                doc.PreferredUploadAttachPath = path;
                doc.UpdatedAt = DateTimeOffset.UtcNow;
                File.WriteAllText(CapabilitiesPath, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch
        {
            /* ignore */
        }
    }

    private static void CaptureRequestHeaders(CoreWebView2WebResourceRequest request, string path)
    {
        try
        {
            var headers = request.Headers;
            var fullCapture = path.Contains("snorlax/sidebar", StringComparison.OrdinalIgnoreCase)
                              || (path.Contains("/gizmos/", StringComparison.OrdinalIgnoreCase)
                                  && path.Contains("/files", StringComparison.OrdinalIgnoreCase))
                              || path.Contains("gizmos/bootstrap", StringComparison.OrdinalIgnoreCase);

            foreach (var name in GetHeaderNamesToCapture(fullCapture))
            {
                if (headers.Contains(name))
                    ChatGptApiClientProfile.SaveHeader(name, headers.GetHeader(name));
            }
        }
        catch
        {
            /* ignore */
        }
    }

    private static IEnumerable<string> GetHeaderNamesToCapture(bool fullCapture)
    {
        yield return "oai-device-id";
        yield return "oai-language";
        yield return "ChatGPT-Account-Id";
        yield return "User-Agent";

        if (!fullCapture)
            yield break;

        yield return "oai-client-version";
        yield return "oai-client-build";
        yield return "oai-client-app";
        yield return "oai-client-name";
        yield return "Accept";
        yield return "Accept-Language";
        yield return "sec-ch-ua";
        yield return "sec-ch-ua-mobile";
        yield return "sec-ch-ua-platform";
        yield return "Referer";
        yield return "Origin";
        yield return "X-Requested-With";
    }

    private static bool IsInteresting(string path) =>
        path.Contains("gizmo", StringComparison.OrdinalIgnoreCase)
        || path.Contains("snorlax", StringComparison.OrdinalIgnoreCase)
        || path.Contains("/files", StringComparison.OrdinalIgnoreCase)
        || path.Contains("conversation", StringComparison.OrdinalIgnoreCase);

    private static string ExtractPath(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u))
            return "";

        return u.AbsolutePath;
    }

    private static void AppendDiscoveryLog(string method, string path)
    {
        try
        {
            AppDirectories.EnsureCreated();
            var line = JsonSerializer.Serialize(new
            {
                at = DateTimeOffset.UtcNow,
                method,
                path,
            });

            lock (Gate)
            {
                File.AppendAllText(DiscoveryLogPath, line + Environment.NewLine);
            }
        }
        catch
        {
            /* ignore */
        }
    }

    private static void UpdateCapability(string path, string method, bool success, int? status = null)
    {
        try
        {
            AppDirectories.EnsureCreated();
            var key = $"{method} {path}";

            lock (Gate)
            {
                var doc = LoadCapabilities();
                if (!doc.Endpoints.TryGetValue(key, out var entry))
                {
                    entry = new CapabilityEntry();
                    doc.Endpoints[key] = entry;
                }

                entry.LastAttempt = DateTimeOffset.UtcNow;
                entry.LastSuccess = success;
                entry.LastStatus = status;
                if (success)
                    entry.SuccessCount++;
                else
                    entry.FailureCount++;

                doc.UpdatedAt = DateTimeOffset.UtcNow;
                File.WriteAllText(CapabilitiesPath, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch
        {
            /* ignore */
        }
    }

    private static CapabilitiesDocument LoadCapabilities()
    {
        try
        {
            if (!File.Exists(CapabilitiesPath))
                return new CapabilitiesDocument();

            var json = File.ReadAllText(CapabilitiesPath);
            return JsonSerializer.Deserialize<CapabilitiesDocument>(json) ?? new CapabilitiesDocument();
        }
        catch
        {
            return new CapabilitiesDocument();
        }
    }

    private sealed class CapabilitiesDocument
    {
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

        public string? PreferredFileDeleteTemplate { get; set; }

        public string? PreferredUploadAttachPath { get; set; }

        public Dictionary<string, CapabilityEntry> Endpoints { get; set; } = new();
    }

    public static class FileDeleteTemplates
    {
        public const string FilePath = "file_path";
        public const string GizmoFilePath = "gizmo_file_path";
        public const string GizmoFilesBody = "gizmo_files_body";
        public const string ProjectsFilePath = "projects_file_path";
        public const string ProjectsFilesBody = "projects_files_body";
    }

    private sealed class CapabilityEntry
    {
        public DateTimeOffset LastAttempt { get; set; }

        public bool LastSuccess { get; set; }

        public int? LastStatus { get; set; }

        public int SuccessCount { get; set; }

        public int FailureCount { get; set; }
    }
}
