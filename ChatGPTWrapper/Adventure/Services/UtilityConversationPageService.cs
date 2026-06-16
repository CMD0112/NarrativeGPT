using System.Text.Json;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class UtilityConversationPageResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }
}

internal sealed class UtilityPageVerifyResult
{
    public bool Matches { get; init; }

    public string? PageHref { get; init; }

    public string? CoreSource { get; init; }
}

internal static class UtilityConversationPageService
{
    private static string? _lastUtilityConversationUrl;

    public static string? LastUtilityConversationUrl => _lastUtilityConversationUrl;

    public static bool MatchesTargetConversation(string? source, string conversationId, string gizmoId) =>
        AdventurePlayContextService.IsOnPlayConversationPage(source, conversationId, gizmoId);

    public static async Task<UtilityPageVerifyResult> VerifyOnTargetPageAsync(
        CoreWebView2 core,
        string conversationId,
        string gizmoId)
    {
        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        var href = await GetPageHrefAsync(core);
        return new UtilityPageVerifyResult
        {
            Matches = MatchesTargetConversation(href, conversationId, gizmoId),
            PageHref = href,
            CoreSource = core.Source,
        };
    }

    public static async Task<string?> GetPageHrefAsync(CoreWebView2 core)
    {
        try
        {
            var raw = await core.ExecuteScriptAsync("(function(){return location.href;})()");
            if (string.IsNullOrWhiteSpace(raw) || raw == "null")
                return core.Source;

            return JsonSerializer.Deserialize<string>(raw) ?? core.Source;
        }
        catch
        {
            return core.Source;
        }
    }

    public static async Task EnsureOnProjectConversationAsync(
        CoreWebView2 core,
        string conversationId,
        string gizmoId,
        CancellationToken cancellationToken = default)
    {
        var result = await EnsureOnProjectConversationStrictAsync(
            core,
            conversationId,
            gizmoId,
            cancellationToken);
        if (!result.Success)
        {
            ProjectLinkDiagnostics.Log(
                $"Utility conversation navigation best-effort failed: {result.Error ?? "unknown"}");
        }
    }

    public static async Task<UtilityConversationPageResult> EnsureOnProjectConversationStrictAsync(
        CoreWebView2 core,
        string conversationId,
        string gizmoId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(gizmoId))
        {
            return new UtilityConversationPageResult
            {
                Success = false,
                Error = "missing_conversation_or_gizmo",
            };
        }

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        if (await IsOnTargetConversationPageAsync(core, conversationId, gizmoId))
        {
            var href = await GetPageHrefAsync(core);
            _lastUtilityConversationUrl = href ?? core.Source;
            return new UtilityConversationPageResult { Success = true };
        }

        var targetUrl = ChatGptUrls.ResolveProjectConversationUrl(conversationId, gizmoId, core.Source);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!await IsOnTargetConversationPageAsync(core, conversationId, gizmoId))
            {
                ProjectLinkDiagnostics.Log($"Navigating utility WebView to conversation {targetUrl}");
                core.Navigate(targetUrl);
                await WaitForNavigationAsync(core, conversationId, gizmoId, cancellationToken);
                await PollSourceUntilOnPageAsync(core, conversationId, gizmoId, cancellationToken);
            }

            if (await IsOnTargetConversationPageAsync(core, conversationId, gizmoId))
            {
                var href = await GetPageHrefAsync(core);
                _lastUtilityConversationUrl = href ?? core.Source;
                return new UtilityConversationPageResult { Success = true };
            }

            await Task.Delay(400, cancellationToken);
        }

        return new UtilityConversationPageResult
        {
            Success = false,
            Error = "utility_page_not_ready",
        };
    }

    private static async Task<bool> IsOnTargetConversationPageAsync(
        CoreWebView2 core,
        string conversationId,
        string gizmoId)
    {
        var href = await GetPageHrefAsync(core);
        return MatchesTargetConversation(href, conversationId, gizmoId);
    }

    private static async Task PollSourceUntilOnPageAsync(
        CoreWebView2 core,
        string conversationId,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsOnTargetConversationPageAsync(core, conversationId, gizmoId))
                return;

            await Task.Delay(150, cancellationToken);
        }
    }

    private static async Task WaitForNavigationAsync(
        CoreWebView2 core,
        string conversationId,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        if (await IsOnTargetConversationPageAsync(core, conversationId, gizmoId))
            return;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess && MatchesTargetConversation(core.Source, conversationId, gizmoId))
                tcs.TrySetResult(true);
        }

        core.NavigationCompleted += Handler;
        try
        {
            using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (TimeoutException)
        {
            /* caller verifies source */
        }
        finally
        {
            core.NavigationCompleted -= Handler;
        }
    }
}
