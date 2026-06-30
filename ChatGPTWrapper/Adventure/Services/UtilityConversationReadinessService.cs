using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal enum UtilityConversationReadinessLevel
{
    Unready,
    DomOnly,
    Registered,
}

internal sealed class UtilityConversationReadinessResult
{
    public UtilityConversationReadinessLevel Level { get; init; }

    public string? Error { get; init; }

    public bool ApiVisible { get; init; }

    public string? PageHref { get; init; }

    public string? DomOnlyReason { get; init; }

    public string? Hint { get; init; }

    public bool ComposerFound { get; init; }

    public bool SubmitFound { get; init; }
}

internal static class UtilityConversationReadinessService
{
    private const string PinUtilityTabHint =
        "Pin a utility Project tab for more reliable jobs.";

    private static readonly TimeSpan[] RateLimitFetchBackoff = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    public static async Task<UtilityConversationReadinessResult> ProbeAsync(
        CoreWebView2 core,
        string conversationId,
        string gizmoId,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService? turnService,
        AdventureBundle? bundle,
        CancellationToken cancellationToken = default,
        bool skipNavigation = false)
    {
        if (bundle is not null && UtilityWorkerSession.For(bundle.Metadata.Id).IsRateLimited)
        {
            return new UtilityConversationReadinessResult
            {
                Level = UtilityConversationReadinessLevel.Unready,
                Error = "rate_limited",
                PageHref = await UtilityConversationPageService.GetPageHrefAsync(core),
            };
        }

        if (!skipNavigation)
        {
            var nav = await UtilityConversationPageService.EnsureOnProjectConversationStrictAsync(
                core,
                conversationId,
                gizmoId,
                cancellationToken);
            if (!nav.Success)
            {
                return new UtilityConversationReadinessResult
                {
                    Level = UtilityConversationReadinessLevel.Unready,
                    Error = nav.Error ?? "utility_page_not_ready",
                    PageHref = await UtilityConversationPageService.GetPageHrefAsync(core),
                };
            }
        }

        var pageHref = await UtilityConversationPageService.GetPageHrefAsync(core);
        if (UtilityConversationPageService.IsProjectHomePage(pageHref) && bundle is not null)
        {
            var settled = await UtilityConversationPageService.WaitForStableOnConversationPageAsync(
                core,
                conversationId,
                gizmoId,
                cancellationToken,
                maxWaitSeconds: 8);
            pageHref = await UtilityConversationPageService.GetPageHrefAsync(core);
            if (!settled.Success || UtilityConversationPageService.IsProjectHomePage(pageHref))
            {
                return new UtilityConversationReadinessResult
                {
                    Level = UtilityConversationReadinessLevel.Unready,
                    Error = "utility_page_not_ready",
                    PageHref = pageHref,
                };
            }
        }
        else if (UtilityConversationPageService.IsProjectHomePage(pageHref))
        {
            return new UtilityConversationReadinessResult
            {
                Level = UtilityConversationReadinessLevel.Unready,
                Error = "utility_page_not_ready",
                PageHref = pageHref,
            };
        }

        if (turnService is null || !await turnService.EnsureUtilityBridgeReadyAsync(core, cancellationToken))
        {
            return new UtilityConversationReadinessResult
            {
                Level = UtilityConversationReadinessLevel.Unready,
                Error = "bridge_not_ready",
                PageHref = pageHref,
            };
        }

        var fetch = await FetchConversationWithRateLimitRetryAsync(
            conversationSend,
            core,
            conversationId,
            cancellationToken);
        if (fetch.Success)
        {
            return new UtilityConversationReadinessResult
            {
                Level = UtilityConversationReadinessLevel.Registered,
                ApiVisible = true,
                PageHref = pageHref,
                ComposerFound = true,
            };
        }

        var domOnlyReason = fetch.Error ?? "conversation_fetch_failed";
        var isDomCapable = IsDomCapableFetchError(domOnlyReason) || IsUnregisteredFetchError(domOnlyReason);

        if (!isDomCapable)
        {
            return new UtilityConversationReadinessResult
            {
                Level = UtilityConversationReadinessLevel.Unready,
                Error = "conversation_unregistered",
                PageHref = pageHref,
                DomOnlyReason = domOnlyReason,
            };
        }

        if (IsRateLimitFetchError(domOnlyReason) && bundle is not null)
            UtilityWorkerSession.For(bundle.Metadata.Id).ApplyRateLimit(TimeSpan.FromSeconds(15));

        await turnService.EnsureUtilityComposerReadyAsync(
            core,
            cancellationToken,
            maxWaitSeconds: 30,
            conversationId,
            gizmoId);
        var health = await turnService.GetAdventureComposerHealthAsync(core, cancellationToken);
        if (!health.ComposerFound)
        {
            var error = IsRateLimitFetchError(domOnlyReason) ? "rate_limited" : "utility_page_not_ready";
            return new UtilityConversationReadinessResult
            {
                Level = UtilityConversationReadinessLevel.Unready,
                Error = error,
                PageHref = pageHref,
                DomOnlyReason = domOnlyReason,
                ComposerFound = health.ComposerFound,
                SubmitFound = health.SubmitFound,
                Hint = ShouldShowUtilityPinHint(bundle),
            };
        }

        return new UtilityConversationReadinessResult
        {
            Level = UtilityConversationReadinessLevel.DomOnly,
            ApiVisible = false,
            PageHref = pageHref,
            DomOnlyReason = domOnlyReason,
            ComposerFound = health.ComposerFound,
            SubmitFound = health.SubmitFound,
            Hint = ShouldShowUtilityPinHint(bundle),
        };
    }

    private static async Task<ConversationFetchResult> FetchConversationWithRateLimitRetryAsync(
        ChatGptConversationSendService conversationSend,
        CoreWebView2 core,
        string conversationId,
        CancellationToken cancellationToken)
    {
        ConversationFetchResult? last = null;
        for (var attempt = 0; attempt <= RateLimitFetchBackoff.Length; attempt++)
        {
            last = await conversationSend.FetchConversationAsync(core, conversationId, cancellationToken);
            if (last.Success || !IsRateLimitFetchError(last.Error))
                return last;

            if (attempt < RateLimitFetchBackoff.Length)
                await Task.Delay(RateLimitFetchBackoff[attempt], cancellationToken);
        }

        return last ?? new ConversationFetchResult { Success = false, Error = "conversation_fetch_failed" };
    }

    internal static bool IsDomOnlyFetchError(string? fetchError) =>
        string.Equals(fetchError, "http_404", StringComparison.OrdinalIgnoreCase)
        || (fetchError?.Contains("404", StringComparison.OrdinalIgnoreCase) ?? false);

    internal static bool IsUnregisteredFetchError(string? fetchError) =>
        IsDomOnlyFetchError(fetchError)
        || string.Equals(fetchError, "http_403", StringComparison.OrdinalIgnoreCase)
        || (fetchError?.Contains("403", StringComparison.OrdinalIgnoreCase) ?? false);

    internal static bool IsRateLimitFetchError(string? fetchError) =>
        string.Equals(fetchError, "http_429", StringComparison.OrdinalIgnoreCase)
        || (fetchError?.Contains("429", StringComparison.OrdinalIgnoreCase) ?? false);

    internal static bool IsDomCapableFetchError(string? fetchError) =>
        IsDomOnlyFetchError(fetchError) || IsRateLimitFetchError(fetchError);

    /// <summary>
    /// Unregistered conversations (404/403 on GET) can be registered via ping push when composer is ready.
    /// </summary>
    internal static bool CanRegisterViaPingPush(UtilityConversationReadinessResult readiness) =>
        readiness.ComposerFound
        && readiness.Level != UtilityConversationReadinessLevel.Unready
        && IsUnregisteredFetchError(readiness.DomOnlyReason);

    /// <summary>Alias kept for call sites.</summary>
    internal static bool CanRegisterViaDomPing(UtilityConversationReadinessResult readiness) =>
        CanRegisterViaPingPush(readiness);

    private static string? ShouldShowUtilityPinHint(AdventureBundle? bundle) => null;
}
