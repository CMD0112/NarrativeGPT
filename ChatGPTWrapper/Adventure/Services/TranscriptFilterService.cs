using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class TranscriptFilterService
{
    public static IReadOnlyList<TranscriptTurnPair> ApplyLookbackAndFilter(
        IReadOnlyList<TranscriptTurnPair> pairs,
        UtilityStoryContextSettings settings,
        AdventureBundle? bundle = null,
        bool isLiveSource = false)
    {
        var normalized = UtilityStoryContextSettingsNormalizer.Normalize(settings);
        if (pairs.Count == 0)
            return pairs;

        var working = pairs.ToList();
        working = ApplyStripUtilityPairs(working);
        working = ApplyAnchorSlice(working, normalized, bundle, isLiveSource);
        working = ApplyStripInjectedContextPlayerMessages(working, isLiveSource);
        working = ApplySanitizeTranscriptText(working);
        working = ApplyTailWindow(working, normalized);
        working = ApplyStripNarratorlessPairs(working, normalized, isLiveSource);
        working = ApplyIncompleteTrailingPair(working, normalized, isLiveSource);
        working = ApplyPerPairCharCap(working, normalized);
        working = ApplyStripEmptyPairs(working, normalized);

        if (normalized.MinTurnPairs > 0 && working.Count < normalized.MinTurnPairs)
            return [];

        return working;
    }

    private static List<TranscriptTurnPair> ApplyStripUtilityPairs(List<TranscriptTurnPair> pairs) =>
        pairs.Where(p =>
            !ConversationStreamParser.IsUtilityUserMessage(p.PlayerText)
            && !ConversationStreamParser.IsUtilityAssistantMessage(p.NarratorText)).ToList();

    private static List<TranscriptTurnPair> ApplyAnchorSlice(
        List<TranscriptTurnPair> pairs,
        UtilityStoryContextSettings settings,
        AdventureBundle? bundle,
        bool isLiveSource)
    {
        if (settings.LookbackAnchor == UtilityLookbackAnchor.FromEnd)
            return pairs;

        if (settings.LookbackAnchor == UtilityLookbackAnchor.AcceptedOnly && !isLiveSource && bundle is not null)
        {
            var acceptedIndices = bundle.Log.Turns
                .Where(t => t.Status == TurnStatus.Accepted)
                .Select(t => t.Index)
                .ToHashSet();
            return pairs.Where(p => p.TurnIndex is null || acceptedIndices.Contains(p.TurnIndex.Value)).ToList();
        }

        if (settings.LookbackAnchor == UtilityLookbackAnchor.SinceLastAcceptedTurn)
        {
            if (!isLiveSource && bundle is not null)
            {
                var lastAccepted = bundle.Log.Turns
                    .Where(t => t.Status == TurnStatus.Accepted)
                    .OrderBy(t => t.Index)
                    .LastOrDefault();
                if (lastAccepted is not null)
                {
                    return pairs
                        .Where(p => p.TurnIndex is null || p.TurnIndex > lastAccepted.Index)
                        .ToList();
                }
            }

            if (isLiveSource && bundle is not null)
            {
                var acceptedCount = bundle.Log.Turns.Count(t => t.Status == TurnStatus.Accepted);
                if (acceptedCount > 0 && pairs.Count > acceptedCount)
                    return pairs.Skip(acceptedCount).ToList();
            }
        }

        if (settings.LookbackAnchor == UtilityLookbackAnchor.SinceTurnIndex)
        {
            var anchored = pairs.Where(p => p.TurnIndex is null || p.TurnIndex >= settings.AnchorTurnIndex).ToList();
            if (anchored.Count > 0 || !isLiveSource)
                return anchored;
        }

        return pairs;
    }

    private static List<TranscriptTurnPair> ApplyStripInjectedContextPlayerMessages(
        List<TranscriptTurnPair> pairs,
        bool isLiveSource)
    {
        if (!isLiveSource)
            return pairs;

        return pairs.Select(p => new TranscriptTurnPair
        {
            PlayerText = ConversationStreamParser.ExtractTranscriptPlayerText(p.PlayerText) ?? "",
            NarratorText = p.NarratorText,
            TurnIndex = p.TurnIndex,
        }).ToList();
    }

    private static List<TranscriptTurnPair> ApplySanitizeTranscriptText(List<TranscriptTurnPair> pairs) =>
        pairs.Select(p => new TranscriptTurnPair
        {
            PlayerText = TranscriptTextSanitizer.Sanitize(p.PlayerText),
            NarratorText = TranscriptTextSanitizer.Sanitize(p.NarratorText),
            TurnIndex = p.TurnIndex,
        }).ToList();

    private static List<TranscriptTurnPair> ApplyStripNarratorlessPairs(
        List<TranscriptTurnPair> pairs,
        UtilityStoryContextSettings settings,
        bool isLiveSource)
    {
        if (!isLiveSource || pairs.Count == 0)
            return pairs;

        return pairs.Where((p, index) =>
        {
            if (!string.IsNullOrWhiteSpace(p.NarratorText))
                return true;

            return index == pairs.Count - 1 && !settings.ExcludeIncompleteTrailingPair;
        }).ToList();
    }

    private static List<TranscriptTurnPair> ApplyTailWindow(
        List<TranscriptTurnPair> pairs,
        UtilityStoryContextSettings settings)
    {
        if (settings.SkipNewestTurnPairs > 0 && pairs.Count > settings.SkipNewestTurnPairs)
            pairs = pairs.Take(pairs.Count - settings.SkipNewestTurnPairs).ToList();

        if (settings.MaxTurnPairs > 0 && pairs.Count > settings.MaxTurnPairs)
            pairs = pairs.TakeLast(settings.MaxTurnPairs).ToList();

        return pairs;
    }

    private static List<TranscriptTurnPair> ApplyIncompleteTrailingPair(
        List<TranscriptTurnPair> pairs,
        UtilityStoryContextSettings settings,
        bool isLiveSource)
    {
        if (!isLiveSource || !settings.ExcludeIncompleteTrailingPair || pairs.Count == 0)
            return pairs;

        var last = pairs[^1];
        if (string.IsNullOrWhiteSpace(last.NarratorText))
            return pairs.Take(pairs.Count - 1).ToList();

        return pairs;
    }

    private static List<TranscriptTurnPair> ApplyPerPairCharCap(
        List<TranscriptTurnPair> pairs,
        UtilityStoryContextSettings settings)
    {
        if (settings.MaxCharsPerTurnPair <= 0)
            return pairs;

        return pairs.Select(p => new TranscriptTurnPair
        {
            PlayerText = CapText(p.PlayerText, settings.MaxCharsPerTurnPair),
            NarratorText = CapText(p.NarratorText, settings.MaxCharsPerTurnPair),
            TurnIndex = p.TurnIndex,
        }).ToList();
    }

    private static List<TranscriptTurnPair> ApplyStripEmptyPairs(
        List<TranscriptTurnPair> pairs,
        UtilityStoryContextSettings settings)
    {
        if (!settings.StripEmptyTurnPairs)
            return pairs;

        return pairs.Where(p =>
        {
            var hasPlayer = settings.IncludePlayerMessages && !string.IsNullOrWhiteSpace(p.PlayerText);
            var hasNarrator = settings.IncludeNarratorMessages && !string.IsNullOrWhiteSpace(p.NarratorText);
            return hasPlayer || hasNarrator;
        }).ToList();
    }

    private static string CapText(string text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        return text[..maxChars] + "…";
    }
}
