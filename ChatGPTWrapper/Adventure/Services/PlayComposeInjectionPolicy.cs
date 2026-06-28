namespace ChatGPTWrapper.Adventure.Services;

using ChatGPTWrapper.Adventure.Models;

/// <summary>
/// Pure policy for play compose intercept registration and injection lookup.
/// Mirrors <c>MainWindow.RegisterPlayComposeInjection</c> and
/// <c>MainWindow.GetActivePlayComposeInjection</c> without WPF/WebView references.
/// </summary>
internal static class PlayComposeInjectionPolicy
{
    /// <summary>
    /// Whether the host should register native compose intercept hooks on a candidate tab.
    /// </summary>
    public static bool ShouldRegisterIntercept(PlayComposeRegistrationContext ctx)
    {
        if (!ctx.IsPlayMode || string.IsNullOrWhiteSpace(ctx.CandidateTabKey))
            return false;

        if (ctx.SuppressPlayAutomation)
            return false;

        var isPlayRef = TabKeysEqual(ctx.CandidateTabKey, ctx.PlayWebViewTabKey);
        var isPinned = ctx.Bundle is not null
                       && PlayTabPinService.IsTabKeyPlayPin(ctx.Bundle, ctx.CandidateTabKey);
        var isActive = TabKeysEqual(ctx.CandidateTabKey, ctx.ActiveWebViewTabKey);

        if (!isPlayRef && !isPinned && !isActive)
            return false;

        if (!isPlayRef && !isPinned && isActive && ctx.SuppressPlayAutomationOnActiveOnly)
            return false;

        return true;
    }

    /// <summary>
    /// Resolves which tab key supplies the active compose injection after play webview resolution.
    /// <paramref name="resolvedPlayWebViewTabKey"/> is the tab key for
    /// <c>ResolvePlayWebView</c> (registry pin, conversation target, etc.).
    /// </summary>
    public static string? ResolveInjectionTabKey(
        AdventureBundle? bundle,
        string? stalePlayWebViewTabKey,
        string? resolvedPlayWebViewTabKey,
        IReadOnlyCollection<string> registeredTabKeys)
    {
        if (registeredTabKeys.Count == 0)
            return null;

        var resolved = resolvedPlayWebViewTabKey;
        if (string.IsNullOrWhiteSpace(resolved) && bundle is not null)
            resolved = PlayTabPinService.GetPlayPinKey(bundle);

        if (!string.IsNullOrWhiteSpace(resolved)
            && ContainsTabKey(registeredTabKeys, resolved))
        {
            return resolved;
        }

        if (!string.IsNullOrWhiteSpace(stalePlayWebViewTabKey)
            && ContainsTabKey(registeredTabKeys, stalePlayWebViewTabKey))
        {
            return stalePlayWebViewTabKey;
        }

        return null;
    }

    /// <summary>
    /// Legacy metadata-only pin check — reproduces the schema-6 regression when metadata was cleared.
    /// </summary>
    public static bool WouldLegacyMetadataPinMatch(AdventureBundle bundle, string? candidateTabKey)
    {
        if (string.IsNullOrWhiteSpace(candidateTabKey))
            return false;

        var legacyKey = bundle.Metadata.PinnedPlayTabKey;
        return !string.IsNullOrWhiteSpace(legacyKey)
               && string.Equals(candidateTabKey, legacyKey, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsTabKey(IReadOnlyCollection<string> keys, string tabKey) =>
        keys.Any(k => TabKeysEqual(k, tabKey));

    private static bool TabKeysEqual(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a)
        && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

internal readonly record struct PlayComposeRegistrationContext(
    bool IsPlayMode,
    AdventureBundle? Bundle,
    string? CandidateTabKey,
    string? PlayWebViewTabKey,
    string? ActiveWebViewTabKey,
    bool SuppressPlayAutomation,
    bool SuppressPlayAutomationOnActiveOnly = false);
