using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Opens a project composer for ephemeral utility worker chats (background-safe; no tab switch).</summary>
internal static class UtilityEphemeralUiCreateService
{
    public static async Task<string?> TryOpenComposerAsync(
        AdventureBundle bundle,
        CoreWebView2 core,
        ChatGptProjectApiService projectApi,
        AdventureTurnService turnService,
        CancellationToken cancellationToken = default,
        Func<string, CancellationToken, Task>? waitForNavigationAsync = null)
    {
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return null;

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);

        await projectApi.EnsureProjectPageAsync(core, gizmoId, cancellationToken);
        if (!AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle))
        {
            var projectUrl = ChatGptUrls.BuildProjectUrl(gizmoId);
            core.Navigate(projectUrl);
            if (waitForNavigationAsync is not null)
                await waitForNavigationAsync(projectUrl, cancellationToken);
            else
                await WaitForLinkedProjectPageAsync(core, bundle, cancellationToken);
        }

        if (!await turnService.EnsureUtilityBridgeReadyAsync(core, cancellationToken))
            return null;

        for (var warmup = 0; warmup < 12; warmup++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var health = await turnService.GetAdventureComposerHealthAsync(core, cancellationToken);
            if (health.ComposerFound)
                break;

            await Task.Delay(500, cancellationToken);
        }

        const int maxUiAttempts = 2;
        for (var attempt = 0; attempt < maxUiAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var ui = await turnService.StartProjectChatAsync(core, cancellationToken);
            var conversationId = ui.ConversationId ?? await turnService.GetConversationIdAsync(core);

            if (!string.IsNullOrWhiteSpace(conversationId)
                && PlayTabPinService.IsAcceptableUtilityConversationId(bundle, conversationId))
            {
                var targetUrl = ChatGptUrls.ResolveProjectConversationUrl(conversationId, gizmoId, core.Source);
                if (!AdventurePlayContextService.IsOnPlayConversationPage(core.Source, conversationId, gizmoId))
                {
                    core.Navigate(targetUrl);
                    if (waitForNavigationAsync is not null)
                        await waitForNavigationAsync(targetUrl, cancellationToken);
                    else
                        await WaitForConversationPageAsync(core, conversationId, gizmoId, cancellationToken);
                }

                ProjectLinkDiagnostics.Log($"Ephemeral UI create succeeded: {conversationId}");
                return conversationId;
            }

            if (string.Equals(ui.Error, "project_new_chat_not_found", StringComparison.OrdinalIgnoreCase))
            {
                ProjectLinkDiagnostics.Log("Ephemeral UI New chat button not found");
                break;
            }

            if (attempt + 1 >= maxUiAttempts
                || string.Equals(ui.Error, "project_chat_not_ready", StringComparison.OrdinalIgnoreCase))
            {
                ProjectLinkDiagnostics.Log(
                    $"Ephemeral UI create gave up ({ui.Error ?? "no_conversation_id"})");
                break;
            }

            await Task.Delay(1000, cancellationToken);
        }

        return null;
    }

    private static async Task WaitForLinkedProjectPageAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle))
                return;

            await Task.Delay(500, cancellationToken);
        }
    }

    private static async Task WaitForConversationPageAsync(
        CoreWebView2 core,
        string conversationId,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (UtilityConversationPageService.MatchesTargetConversation(core.Source, conversationId, gizmoId))
                return;

            await Task.Delay(500, cancellationToken);
        }
    }
}
