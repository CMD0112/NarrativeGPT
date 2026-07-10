using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

public enum StoryContextSourceUsed
{
    None,
    LiveApi,
    LiveDom,
    LocalLog,
}

internal sealed class StoryContextCaptureResult
{
    public StoryContextSourceUsed SourceUsed { get; init; }

    public IReadOnlyList<TranscriptTurnPair> TurnPairs { get; init; } = [];

    public string? Error { get; init; }
}

internal sealed class PlayThreadTranscriptService(
    ChatGptConversationSendService conversationSend,
    AdventureTurnService? turnService = null)
{
    public async Task<StoryContextCaptureResult> CaptureAsync(
        AdventureBundle bundle,
        UtilityStoryContextSettings settings,
        CoreWebView2? playCore,
        CancellationToken cancellationToken = default,
        bool domOnlyCapture = false)
    {
        var normalized = UtilityStoryContextSettingsNormalizer.Normalize(settings);

        if (domOnlyCapture)
        {
            return normalized.Source switch
            {
                UtilityStorySource.LocalLog => CaptureFromLocal(bundle, normalized),
                UtilityStorySource.LivePlayThread => await CaptureLiveDomOnlyAsync(
                    bundle, normalized, playCore, cancellationToken),
                UtilityStorySource.LocalThenLive => CaptureFromLocal(bundle, normalized) is { TurnPairs.Count: > 0 } local
                    ? local
                    : await CaptureLiveDomOnlyAsync(bundle, normalized, playCore, cancellationToken),
                _ => await CaptureLiveDomOnlyThenLocalAsync(bundle, normalized, playCore, cancellationToken),
            };
        }

        return normalized.Source switch
        {
            UtilityStorySource.LocalLog => CaptureFromLocal(bundle, normalized),
            UtilityStorySource.LivePlayThread => await CaptureLiveAsync(bundle, normalized, playCore, cancellationToken),
            UtilityStorySource.LocalThenLive => CaptureFromLocal(bundle, normalized) is { TurnPairs.Count: > 0 } local
                ? local
                : await CaptureLiveAsync(bundle, normalized, playCore, cancellationToken),
            _ => await CaptureLiveThenLocalAsync(bundle, normalized, playCore, cancellationToken),
        };
    }

    private async Task<StoryContextCaptureResult> CaptureLiveThenLocalAsync(
        AdventureBundle bundle,
        UtilityStoryContextSettings settings,
        CoreWebView2? playCore,
        CancellationToken cancellationToken)
    {
        var live = await CaptureLiveAsync(bundle, settings, playCore, cancellationToken);
        if (live.TurnPairs.Count > 0)
            return live;

        var local = CaptureFromLocal(bundle, settings);
        if (local.TurnPairs.Count > 0)
            return local;

        return new StoryContextCaptureResult
        {
            SourceUsed = StoryContextSourceUsed.None,
            Error = live.Error ?? local.Error,
        };
    }

    private async Task<StoryContextCaptureResult> CaptureLiveDomOnlyThenLocalAsync(
        AdventureBundle bundle,
        UtilityStoryContextSettings settings,
        CoreWebView2? playCore,
        CancellationToken cancellationToken)
    {
        var live = await CaptureLiveDomOnlyAsync(bundle, settings, playCore, cancellationToken);
        if (live.TurnPairs.Count > 0)
            return live;

        var local = CaptureFromLocal(bundle, settings);
        if (local.TurnPairs.Count > 0)
            return local;

        return new StoryContextCaptureResult
        {
            SourceUsed = StoryContextSourceUsed.None,
            Error = live.Error ?? local.Error,
        };
    }

    private async Task<StoryContextCaptureResult> CaptureLiveDomOnlyAsync(
        AdventureBundle bundle,
        UtilityStoryContextSettings settings,
        CoreWebView2? playCore,
        CancellationToken cancellationToken)
    {
        var conversationId = ResolveActivePlayConversationId(bundle);
        if (string.IsNullOrWhiteSpace(conversationId) || playCore is null)
            return new StoryContextCaptureResult { Error = "play_thread_unavailable" };

        if (!IsPlayWebViewOnConversation(playCore, conversationId))
            return new StoryContextCaptureResult { Error = "play_tab_url_mismatch" };

        if (turnService is null)
            return new StoryContextCaptureResult { Error = "live_capture_failed" };

        var dom = await turnService.CaptureThreadTranscriptAsync(
            playCore,
            settings.MaxTurnPairs + settings.SkipNewestTurnPairs,
            cancellationToken);
        if (dom.Success && dom.TurnPairs.Count > 0)
            return FilterLiveResult(dom.TurnPairs, settings, bundle, StoryContextSourceUsed.LiveDom);

        return new StoryContextCaptureResult
        {
            Error = dom.Error ?? "live_capture_failed",
        };
    }

    private async Task<StoryContextCaptureResult> CaptureLiveAsync(
        AdventureBundle bundle,
        UtilityStoryContextSettings settings,
        CoreWebView2? playCore,
        CancellationToken cancellationToken)
    {
        var conversationId = ResolveActivePlayConversationId(bundle);
        if (string.IsNullOrWhiteSpace(conversationId) || playCore is null)
            return new StoryContextCaptureResult { Error = "play_thread_unavailable" };

        if (!IsPlayWebViewOnConversation(playCore, conversationId))
            return new StoryContextCaptureResult { Error = "play_tab_url_mismatch" };

        var fetch = await conversationSend.FetchConversationAsync(playCore, conversationId, cancellationToken);
        if (fetch.Success && fetch.Json is { } json)
        {
            var pairs = ConversationStreamParser.ExtractTranscriptTurns(json);
            if (pairs.Count > 0)
            {
                return FilterLiveResult(pairs, settings, bundle, StoryContextSourceUsed.LiveApi);
            }
        }

        if (turnService is not null)
        {
            var dom = await turnService.CaptureThreadTranscriptAsync(
                playCore,
                settings.MaxTurnPairs + settings.SkipNewestTurnPairs,
                cancellationToken);
            if (dom.Success && dom.TurnPairs.Count > 0)
            {
                return FilterLiveResult(dom.TurnPairs, settings, bundle, StoryContextSourceUsed.LiveDom);
            }
        }

        return new StoryContextCaptureResult
        {
            Error = fetch.Error ?? "live_capture_failed",
        };
    }

    private static StoryContextCaptureResult FilterLiveResult(
        IReadOnlyList<TranscriptTurnPair> rawPairs,
        UtilityStoryContextSettings settings,
        AdventureBundle bundle,
        StoryContextSourceUsed sourceUsed)
    {
        var filtered = TranscriptFilterService.ApplyLookbackAndFilter(
            rawPairs,
            settings,
            bundle,
            isLiveSource: true);

        return new StoryContextCaptureResult
        {
            SourceUsed = sourceUsed,
            TurnPairs = filtered,
        };
    }

    internal static StoryContextCaptureResult CaptureFromLocal(
        AdventureBundle bundle,
        UtilityStoryContextSettings settings)
    {
        var normalized = UtilityStoryContextSettingsNormalizer.Normalize(settings);

        if (ThreadConversationLogReader.HasActivePlayLog(bundle))
        {
            var projectionCapture = ThreadTranscriptResolver.ResolvePlayThreadTranscript(bundle, normalized);
            if (projectionCapture.TurnPairs.Count > 0)
                return projectionCapture;

            var entry = ThreadConversationLogReader.GetActiveEntry(bundle, AdventureThreadKind.Play)!;
            var threadPairs = ThreadConversationLogReader.GetTranscriptPairs(bundle, entry);
            if (threadPairs.Count > 0)
            {
                var filteredThread = TranscriptFilterService.ApplyLookbackAndFilter(
                    threadPairs,
                    normalized,
                    bundle,
                    isLiveSource: false);

                return new StoryContextCaptureResult
                {
                    SourceUsed = StoryContextSourceUsed.LocalLog,
                    TurnPairs = filteredThread,
                };
            }
        }

        var turns = bundle.Log.Turns
            .OrderBy(t => t.Index)
            .Where(t => normalized.IncludePendingLocalTurns || t.Status == TurnStatus.Accepted);

        if (normalized.LookbackAnchor == UtilityLookbackAnchor.AcceptedOnly)
            turns = turns.Where(t => t.Status == TurnStatus.Accepted);

        var pairs = turns
            .Select(t => new TranscriptTurnPair
            {
                PlayerText = t.PlayerText ?? "",
                NarratorText = t.NarratorText ?? "",
                TurnIndex = t.Index,
            })
            .ToList();

        if (pairs.Count == 0)
            return new StoryContextCaptureResult { SourceUsed = StoryContextSourceUsed.None };

        var filtered = TranscriptFilterService.ApplyLookbackAndFilter(
            pairs,
            normalized,
            bundle,
            isLiveSource: false);

        return new StoryContextCaptureResult
        {
            SourceUsed = StoryContextSourceUsed.LocalLog,
            TurnPairs = filtered,
        };
    }

    private static bool IsPlayWebViewOnConversation(CoreWebView2 core, string conversationId)
    {
        if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var uri))
            return false;

        return ChatGptUrls.TryParseConversationId(uri, out var parsed)
               && string.Equals(parsed, conversationId, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveActivePlayConversationId(AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var legacy = bundle.Metadata.LinkedConversationId;
        var fromRegistry = AdventureThreadRegistryService.GetActiveConversationId(bundle, AdventureThreadKind.Play);

        if (!string.IsNullOrWhiteSpace(legacy)
            && !string.Equals(legacy, fromRegistry, StringComparison.OrdinalIgnoreCase))
        {
            return legacy;
        }

        return fromRegistry ?? legacy;
    }
}
