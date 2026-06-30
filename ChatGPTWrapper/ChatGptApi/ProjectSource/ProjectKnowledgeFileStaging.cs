using System.IO;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.ProjectSource;

/// <summary>
/// Stages project knowledge files on ChatGPT's project file input via CDP (WebView2).
/// Targets inputs outside the chat composer — see bridge <c>prepareProjectKnowledgeUpload</c>.
/// </summary>
internal static class ProjectKnowledgeFileStaging
{
    private const string MarkAttribute = "data-cgw-project-file-input";
    private static readonly object StagingGate = new();
    private static List<string> ActiveStagingPaths = [];

    public static async Task<(bool Success, string? Error)> StageAsync(
        CoreWebView2 core,
        string fileName,
        byte[] content,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (content.Length == 0)
            return (false, "empty_content");

        CleanupStagedFiles();
        var stagingDir = GetStagingDirectory();
        var sanitized = SanitizeFileName(fileName);
        if (string.IsNullOrWhiteSpace(Path.GetExtension(sanitized)))
            sanitized += GuessExtension(mimeType);

        var path = Path.Combine(stagingDir, $"cgw-proj-{Guid.NewGuid():N}-{sanitized}");
        try
        {
            await File.WriteAllBytesAsync(path, content, cancellationToken);
            var fullPath = Path.GetFullPath(path);

            lock (StagingGate)
                ActiveStagingPaths = [fullPath];

            if (!await HasMarkedProjectFileInputAsync(core, cancellationToken))
            {
                CleanupStagedFiles();
                return (false, "project_file_input_not_marked");
            }

            await core.CallDevToolsProtocolMethodAsync("DOM.enable", "{}")
                .WaitAsync(cancellationToken);
            var rootNodeId = await GetDocumentNodeIdAsync(core, cancellationToken);
            var inputNodeId = await QueryMarkedFileInputNodeIdAsync(core, rootNodeId, cancellationToken);
            if (inputNodeId is null)
            {
                CleanupStagedFiles();
                return (false, "project_file_input_node_not_found");
            }

            await core.CallDevToolsProtocolMethodAsync(
                "DOM.setFileInputFiles",
                JsonSerializer.Serialize(new
                {
                    nodeId = inputNodeId.Value,
                    files = new[] { fullPath },
                }))
                .WaitAsync(cancellationToken);

            if (!await DispatchInputChangeAsync(core, cancellationToken))
            {
                CleanupStagedFiles();
                return (false, "project_file_input_change_dispatch_failed");
            }

            ProjectLinkDiagnostics.Log(
                $"Project DOM CDP staged file={sanitized} bytes={content.Length}");
            return (true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            CleanupStagedFiles();
            return (false, ex.Message);
        }
    }

    public static void CleanupStagedFiles()
    {
        List<string> paths;
        lock (StagingGate)
        {
            paths = ActiveStagingPaths;
            ActiveStagingPaths = [];
        }

        foreach (var stagedPath in paths)
        {
            try { File.Delete(stagedPath); }
            catch { /* best-effort */ }
        }
    }

    private static async Task<bool> HasMarkedProjectFileInputAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken) =>
        await EvaluateBoolAsync(
            core,
            $$"""
             (function(){
               return !!document.querySelector('input[{{MarkAttribute}}="1"]');
             })()
             """,
            cancellationToken);

    private static async Task<bool> DispatchInputChangeAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken) =>
        await EvaluateBoolAsync(
            core,
            $$"""
             (function(){
               var el = document.querySelector('input[{{MarkAttribute}}="1"]');
               if (!el) return false;
               el.dispatchEvent(new Event('change', { bubbles: true }));
               el.dispatchEvent(new Event('input', { bubbles: true }));
               el.removeAttribute('{{MarkAttribute}}');
               return true;
             })()
             """,
            cancellationToken);

    private static async Task<int> GetDocumentNodeIdAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        var raw = await core.CallDevToolsProtocolMethodAsync("DOM.getDocument", "{}")
            .WaitAsync(cancellationToken);
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.GetProperty("root").GetProperty("nodeId").GetInt32();
    }

    private static async Task<int?> QueryMarkedFileInputNodeIdAsync(
        CoreWebView2 core,
        int rootNodeId,
        CancellationToken cancellationToken)
    {
        var raw = await core.CallDevToolsProtocolMethodAsync(
            "DOM.querySelector",
            JsonSerializer.Serialize(new
            {
                nodeId = rootNodeId,
                selector = $"input[{MarkAttribute}=\"1\"]",
            }))
            .WaitAsync(cancellationToken);

        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("nodeId", out var nodeId))
            return null;

        var id = nodeId.GetInt32();
        return id > 0 ? id : null;
    }

    private static async Task<bool> EvaluateBoolAsync(
        CoreWebView2 core,
        string script,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var raw = await core.ExecuteScriptAsync(script);
        return raw.Contains("true", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetStagingDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTWrapper",
            "cdp-staging",
            "project-knowledge");
        Directory.CreateDirectory(dir);
        return dir;
    }

    internal static string SanitizeFileName(string? name)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? "source.md" : Path.GetFileName(name.Replace('\\', '/'));
        foreach (var invalid in Path.GetInvalidFileNameChars())
            baseName = baseName.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(baseName) ? "source.md" : baseName;
    }

    internal static string Basename(string remoteFileName) =>
        SanitizeFileName(remoteFileName);

    internal static bool RemoteFileMatchesName(GizmoFileRef file, string remoteFileName)
    {
        if (string.IsNullOrWhiteSpace(file.Name))
            return false;

        var expected = Basename(remoteFileName);
        var actual = Basename(file.Name);
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string GuessExtension(string mimeType) =>
        mimeType.ToLowerInvariant() switch
        {
            "text/markdown" => ".md",
            "text/plain" => ".txt",
            "application/json" => ".json",
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            _ => ".bin",
        };
}
