using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.PageIntegration;

/// <summary>
/// Clears wrapper injection packets left in ChatGPT's native composer (draft restore / failed send).
/// Only removes content that matches injected packet markers; ordinary user drafts are kept.
/// </summary>
internal static class StaleInjectionComposerCleanup
{
    public static bool ShouldRun(
        bool browseOrAdventuresMode,
        Guid? activeAdventureId,
        bool playDraftPendingPaste)
    {
        if (!browseOrAdventuresMode || activeAdventureId is not null || playDraftPendingPaste)
            return false;

        return true;
    }

    public static async Task TryClearAsync(
        CoreWebView2 core,
        ChatGptAdventureBridgeInjection bridge,
        CancellationToken cancellationToken = default)
    {
        if (!ChatGptPageGate.IsInjectable(core.Source))
            return;

        try
        {
            await Task.Delay(400, cancellationToken);
            await bridge.InjectAsync(core);
            if (!await bridge.EnsureBridgeReadyAsync(core, cancellationToken))
                return;

            bridge.SendClearStaleInjectionComposerCommand(core);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            /* best-effort */
        }
    }
}
