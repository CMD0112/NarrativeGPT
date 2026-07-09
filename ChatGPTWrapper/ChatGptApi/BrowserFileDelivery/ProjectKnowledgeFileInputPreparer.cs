using System.Text.Json;
using ChatGPTWrapper.ChatGptApi.ProjectSource;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.BrowserFileDelivery;

internal static class ProjectKnowledgeFileInputPreparer
{
    public const string MarkAttribute = "data-cgw-project-file-input";

    public static async Task<bool> PrepareUiAsync(
        ChatGptApiBridgeInjection bridge,
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var msg = await bridge.SendAsync(
                core,
                new { action = "prepareProjectKnowledgeUpload", gizmoId },
                timeoutMs: 30_000,
                cancellationToken: cancellationToken,
                skipReadyWait: bridge.IsWarm(core));

            if (msg.Ok)
            {
                ProjectLinkDiagnostics.Log(
                    $"Project DOM prepare ok attempt={attempt + 1} for {gizmoId}");
                return true;
            }

            ProjectLinkDiagnostics.Log(
                $"Project DOM prepare failed attempt={attempt + 1} for {gizmoId}: "
                + $"{msg.Error ?? msg.Message}");
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        return false;
    }

    public static async Task<bool> ConfirmUploadAsync(
        ChatGptApiBridgeInjection bridge,
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        var msg = await bridge.SendAsync(
            core,
            new { action = "confirmProjectKnowledgeUpload" },
            timeoutMs: 15_000,
            cancellationToken: cancellationToken,
            skipReadyWait: bridge.IsWarm(core));

        if (!msg.Ok)
        {
            ProjectLinkDiagnostics.Log(
                $"Project DOM confirm failed: {msg.Error ?? msg.Message}");
            return false;
        }

        var clicked = "";
        if (msg.Json is { } json && json.TryGetProperty("clicked", out var clickedEl)
            && clickedEl.ValueKind == JsonValueKind.Array)
        {
            clicked = string.Join(", ", clickedEl.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        var href = msg.Json?.TryGetProperty("href", out var hrefEl) == true
            ? hrefEl.GetString()
            : core.Source;
        ProjectLinkDiagnostics.Log(
            $"Project DOM confirm ok href={href}"
            + (string.IsNullOrWhiteSpace(clicked) ? "" : $" clicked=[{clicked}]"));
        return true;
    }

    public static async Task LogDomDiagnosticsAsync(
        ChatGptApiBridgeInjection bridge,
        CoreWebView2 core,
        string context,
        CancellationToken cancellationToken)
    {
        try
        {
            var msg = await DomFileInputProbe.ListProjectFileUiAsync(bridge, core, cancellationToken);
            if (!msg.Ok || msg.Json is not { } json)
            {
                ProjectLinkDiagnostics.Log(
                    $"Project DOM diagnostics ({context}): probe failed {msg.Error ?? msg.Message}");
                return;
            }

            var href = json.TryGetProperty("href", out var hrefEl) ? hrefEl.GetString() : core.Source;
            var inputCount = json.TryGetProperty("fileInputs", out var inputsEl)
                               && inputsEl.ValueKind == JsonValueKind.Array
                ? inputsEl.GetArrayLength()
                : 0;
            ProjectLinkDiagnostics.Log(
                $"Project DOM diagnostics ({context}): href={href} fileInputs={inputCount} source={core.Source}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ProjectLinkDiagnostics.Log($"Project DOM diagnostics ({context}) failed: {ex.Message}");
        }
    }

    public static Task<bool> HasMarkedInputAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken) =>
        HasMarkedInputAsync(core, MarkAttribute, cancellationToken);

    public static Task<bool> HasMarkedInputAsync(
        CoreWebView2 core,
        string markAttribute,
        CancellationToken cancellationToken) =>
        DomFileStagingCore.HasMarkedInputAsync(core, markAttribute, cancellationToken);
}
