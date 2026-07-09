using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.BrowserFileDelivery;

/// <summary>
/// Shared CDP file-input staging for composer and project knowledge targets.
/// </summary>
public static class DomFileStagingCore
{
    private static readonly object StagingGate = new();
    private static List<string> ActiveStagingPaths = [];

    public static IReadOnlyList<string> ActivePaths
    {
        get
        {
            lock (StagingGate)
                return ActiveStagingPaths.ToList();
        }
    }

    public static async Task<(bool Success, string? Error)> StageMarkedInputAsync(
        CoreWebView2 core,
        string markAttribute,
        IReadOnlyList<string> absoluteFilePaths,
        CancellationToken cancellationToken = default)
    {
        if (absoluteFilePaths.Count == 0)
            return (true, null);

        try
        {
            await core.CallDevToolsProtocolMethodAsync("DOM.enable", "{}")
                .WaitAsync(cancellationToken);
            var rootNodeId = await GetDocumentNodeIdAsync(core, cancellationToken);
            var inputNodeId = await QueryMarkedFileInputNodeIdAsync(
                core,
                rootNodeId,
                markAttribute,
                cancellationToken);
            if (inputNodeId is null)
                return (false, "file_input_node_not_found");

            await core.CallDevToolsProtocolMethodAsync(
                "DOM.setFileInputFiles",
                JsonSerializer.Serialize(new
                {
                    nodeId = inputNodeId.Value,
                    files = absoluteFilePaths,
                }))
                .WaitAsync(cancellationToken);

            if (!await DispatchInputChangeAsync(core, markAttribute, cancellationToken))
                return (false, "file_input_change_dispatch_failed");

            return (true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, ex.Message);
        }
    }

    public static void TrackStagingPaths(IReadOnlyList<string> paths)
    {
        lock (StagingGate)
            ActiveStagingPaths = paths.ToList();
    }

    public static void CleanupStagedFiles()
    {
        List<string> paths;
        lock (StagingGate)
        {
            paths = ActiveStagingPaths;
            ActiveStagingPaths = [];
        }

        foreach (var path in paths)
        {
            try { File.Delete(path); }
            catch { /* best-effort */ }
        }
    }

    public static async Task<bool> HasMarkedInputAsync(
        CoreWebView2 core,
        string markAttribute,
        CancellationToken cancellationToken) =>
        await EvaluateBoolAsync(
            core,
            $$"""
             (function(){
               return !!document.querySelector('input[{{markAttribute}}="1"]');
             })()
             """,
            cancellationToken);

    private static async Task<bool> DispatchInputChangeAsync(
        CoreWebView2 core,
        string markAttribute,
        CancellationToken cancellationToken) =>
        await EvaluateBoolAsync(
            core,
            $$"""
             (function(){
               var el = document.querySelector('input[{{markAttribute}}="1"]');
               if (!el) return false;
               el.dispatchEvent(new Event('change', { bubbles: true }));
               el.dispatchEvent(new Event('input', { bubbles: true }));
               el.removeAttribute('{{markAttribute}}');
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
        string markAttribute,
        CancellationToken cancellationToken)
    {
        var raw = await core.CallDevToolsProtocolMethodAsync(
            "DOM.querySelector",
            JsonSerializer.Serialize(new
            {
                nodeId = rootNodeId,
                selector = $"input[{markAttribute}=\"1\"]",
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
}
