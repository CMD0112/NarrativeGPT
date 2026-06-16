using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// Captures sanitized request/response samples for conversation send endpoints (manual ChatGPT sends).
/// </summary>
internal static class ChatGptApiSendSampleCapture
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> CapturedKeys = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, CachedSample> SampleCache = new(StringComparer.OrdinalIgnoreCase);

    private sealed class CachedSample
    {
        public required string Json { get; init; }

        public required DateTime LastWriteTimeUtc { get; init; }
    }

    public static string SamplesDirectory => Path.Combine(AppDirectories.Root, "api-send-samples");

    public static void Register(CoreWebView2 core)
    {
        core.WebResourceResponseReceived -= OnWebResourceResponseReceived;
        core.WebResourceResponseReceived += OnWebResourceResponseReceived;
    }

    internal static void ClearCacheForTests() => SampleCache.Clear();

    public static bool TryLoadSample(string sampleKey, out JsonElement root)
    {
        root = default;
        if (!TryLoadSampleCore(sampleKey, out root))
            return false;

        if (root.TryGetProperty("status", out var statusEl)
            && statusEl.TryGetInt32(out var status)
            && status is >= 400)
        {
            root = default;
            return false;
        }

        return true;
    }

    public static bool TryLoadSuccessfulRequestTemplate(string sampleKey, out JsonElement requestBody)
    {
        requestBody = default;
        if (!TryLoadSampleCore(sampleKey, out var root))
            return false;

        if (root.TryGetProperty("status", out var statusEl)
            && statusEl.TryGetInt32(out var status)
            && status is >= 400)
        {
            return false;
        }

        return root.TryGetProperty("requestBody", out requestBody)
               && requestBody.ValueKind == JsonValueKind.Object;
    }

    private static bool TryLoadSampleCore(string sampleKey, out JsonElement root)
    {
        root = default;
        try
        {
            var path = Path.Combine(SamplesDirectory, SanitizeFileName(sampleKey) + ".json");
            if (!File.Exists(path))
                return false;

            var lastWrite = File.GetLastWriteTimeUtc(path);
            lock (Gate)
            {
                if (SampleCache.TryGetValue(sampleKey, out var cached)
                    && cached.LastWriteTimeUtc == lastWrite)
                {
                    using var cachedDoc = JsonDocument.Parse(cached.Json);
                    root = cachedDoc.RootElement.Clone();
                    return true;
                }
            }

            var json = File.ReadAllText(path);
            lock (Gate)
            {
                SampleCache[sampleKey] = new CachedSample
                {
                    Json = json,
                    LastWriteTimeUtc = lastWrite,
                };
            }

            using var doc = JsonDocument.Parse(json);
            root = doc.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async void OnWebResourceResponseReceived(
        object? sender,
        CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            var uri = e.Request.Uri;
            if (!uri.Contains("backend-api", StringComparison.OrdinalIgnoreCase))
                return;

            var method = e.Request.Method ?? "GET";
            var path = ExtractPath(uri);
            if (!ShouldCapture(method, path))
                return;

            var statusCode = e.Response.StatusCode;
            string? requestBody = null;
            try
            {
                using var requestStream = e.Request.Content;
                if (requestStream is not null)
                {
                    using var reader = new StreamReader(requestStream);
                    requestBody = await reader.ReadToEndAsync();
                }
            }
            catch
            {
                /* ignore */
            }

            if (!ShouldPersistSample(method, path, statusCode, requestBody))
                return;

            var sampleKey = BuildSampleKey(method, path, requestBody);

            lock (Gate)
            {
                if (CapturedKeys.Contains(sampleKey) && !HasFailedSampleOnDisk(sampleKey))
                    return;
            }

            string? responsePreview = null;
            try
            {
                using var responseStream = await e.Response.GetContentAsync();
                if (responseStream is not null)
                {
                    using var reader = new StreamReader(responseStream);
                    var text = await reader.ReadToEndAsync();
                    responsePreview = Truncate(SanitizeText(text), 8000);
                }
            }
            catch
            {
                /* ignore */
            }

            TrySeedParentCache(method, path, statusCode, responsePreview);
            TrySeedConduitCache(method, path, statusCode, requestBody, responsePreview);

            var sample = new
            {
                capturedAt = DateTimeOffset.UtcNow,
                method,
                path,
                status = statusCode,
                requestBody = ParseJsonOrText(SanitizeText(requestBody)),
                responseBodyPreview = responsePreview,
            };

            SaveSample(sampleKey, sample);

            lock (Gate)
            {
                CapturedKeys.Add(sampleKey);
            }
        }
        catch
        {
            /* ignore */
        }
    }

    private static void TrySeedParentCache(string method, string path, int status, string? responsePreview)
    {
        if (status != 200
            || !method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(responsePreview))
        {
            return;
        }

        if (!path.StartsWith("/backend-api/conversation/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/stream_status", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/textdocs", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var conversationId = path["/backend-api/conversation/".Length..];
        var slash = conversationId.IndexOf('/');
        if (slash >= 0)
            conversationId = conversationId[..slash];

        if (string.IsNullOrWhiteSpace(conversationId))
            return;

        try
        {
            using var doc = JsonDocument.Parse(responsePreview);
            var node = ChatGptConversationSendService.ExtractCurrentNode(doc.RootElement);
            if (!string.IsNullOrWhiteSpace(node))
                ConversationParentCache.Set(conversationId, node);
        }
        catch
        {
            /* ignore malformed preview */
        }
    }

    private static void TrySeedConduitCache(
        string method,
        string path,
        int status,
        string? requestBody,
        string? responsePreview)
    {
        if (status != 200
            || !method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            || !path.Equals(ChatGptApiEndpoints.ConversationPrepare, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(responsePreview))
        {
            return;
        }

        try
        {
            using var responseDoc = JsonDocument.Parse(responsePreview);
            var token = ChatGptConversationSendService.ExtractConduitToken(responseDoc.RootElement);
            if (string.IsNullOrWhiteSpace(token))
                return;

            string? conversationId = null;
            if (!string.IsNullOrWhiteSpace(requestBody))
            {
                using var requestDoc = JsonDocument.Parse(requestBody);
                conversationId = JsonElementParsing.GetStringOrNull(requestDoc.RootElement, "conversation_id");
            }

            if (string.IsNullOrWhiteSpace(conversationId))
                return;

            ConversationConduitCache.Set(conversationId, token);
        }
        catch
        {
            /* ignore malformed preview */
        }
    }

    private static bool ShouldPersistSample(string method, string path, int status, string? requestBody = null)
    {
        if (status is >= 200 and < 300)
            return true;

        if (!method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.Equals(ChatGptApiEndpoints.ConversationSend, StringComparison.OrdinalIgnoreCase)
            && ContainsAttachmentPayload(requestBody))
        {
            return true;
        }

        return !path.Equals(ChatGptApiEndpoints.ConversationSend, StringComparison.OrdinalIgnoreCase)
               && !path.Equals(ChatGptApiEndpoints.ConversationPrepare, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasFailedSampleOnDisk(string sampleKey)
    {
        try
        {
            var path = Path.Combine(SamplesDirectory, SanitizeFileName(sampleKey) + ".json");
            if (!File.Exists(path))
                return false;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("status", out var statusEl)
                   && statusEl.TryGetInt32(out var status)
                   && status is >= 400;
        }
        catch
        {
            return false;
        }
    }

    private static bool ShouldCapture(string method, string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.StartsWith("/backend-api/conversation/", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("/stream_status", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("/textdocs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && (path.Equals(ChatGptApiEndpoints.ConversationPrepare, StringComparison.OrdinalIgnoreCase)
                || path.Equals(ChatGptApiEndpoints.ConversationSend, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && (path.Equals(ChatGptApiEndpoints.FilesUpload, StringComparison.OrdinalIgnoreCase)
                || path.Equals(ChatGptApiEndpoints.FilesLibraryUpload, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static string BuildSampleKey(string method, string path, string? requestBody = null)
    {
        if (method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            && path.StartsWith("/backend-api/conversation/", StringComparison.OrdinalIgnoreCase))
        {
            return "GET_conversation";
        }

        if (method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && path.Equals(ChatGptApiEndpoints.ConversationSend, StringComparison.OrdinalIgnoreCase)
            && ContainsAttachmentPayload(requestBody))
        {
            return "POST_backend-api_f_conversation_attachments";
        }

        return method.ToUpperInvariant() + path.Replace('/', '_');
    }

    private static bool ContainsAttachmentPayload(string? requestBody)
    {
        if (string.IsNullOrWhiteSpace(requestBody))
            return false;

        return requestBody.Contains("\"attachments\"", StringComparison.OrdinalIgnoreCase)
               || requestBody.Contains("asset_pointer", StringComparison.OrdinalIgnoreCase)
               || requestBody.Contains("multimodal_text", StringComparison.OrdinalIgnoreCase);
    }

    private static void SaveSample(string sampleKey, object sample)
    {
        try
        {
            AppDirectories.EnsureCreated();
            Directory.CreateDirectory(SamplesDirectory);
            var path = Path.Combine(SamplesDirectory, SanitizeFileName(sampleKey) + ".json");
            File.WriteAllText(
                path,
                JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true }));

            lock (Gate)
            {
                SampleCache.Remove(sampleKey);
            }
        }
        catch
        {
            /* ignore */
        }
    }

    private static object? ParseJsonOrText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(text);
            return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText());
        }
        catch
        {
            return text;
        }
    }

    private static string SanitizeText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        text = Regex.Replace(text, "Bearer\\s+[A-Za-z0-9._\\-]+", "Bearer [REDACTED]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "\"accessToken\"\\s*:\\s*\"[^\"]+\"", "\"accessToken\":\"[REDACTED]\"", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "\"authorization\"\\s*:\\s*\"[^\"]+\"", "\"authorization\":\"[REDACTED]\"", RegexOptions.IgnoreCase);
        return text;
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    private static string SanitizeFileName(string key)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            key = key.Replace(c, '_');
        return key;
    }

    private static string ExtractPath(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u))
            return "";

        return u.AbsolutePath;
    }
}
