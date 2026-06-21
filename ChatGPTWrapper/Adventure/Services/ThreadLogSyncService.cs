using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class ThreadLogDriftAnalysis
{
    public bool CaptureSucceeded { get; init; } = true;

    public string? CaptureError { get; init; }

    public int ThreadTurnCount { get; init; }

    public int LogTurnCount { get; init; }

    public int ComparedTurnCount { get; init; }

    public bool HasDrift { get; init; }

    public string DriftFingerprint { get; init; } = "";

    public IReadOnlyList<TranscriptTurnPair> ThreadPairs { get; init; } = [];
}

internal static class ThreadLogSyncService
{
    public static UtilityStoryContextSettings CreateSyncSettings() => new()
    {
        Source = UtilityStorySource.LivePlayThread,
        MaxTurnPairs = 0,
        SkipNewestTurnPairs = 0,
        MinTurnPairs = 0,
        LookbackAnchor = UtilityLookbackAnchor.FromEnd,
        ExcludeIncompleteTrailingPair = true,
        StripEmptyTurnPairs = true,
        IncludePlayerMessages = true,
        IncludeNarratorMessages = true,
    };

    public static IReadOnlyList<TranscriptTurnPair> FilterThreadPairsForSync(
        IReadOnlyList<TranscriptTurnPair> rawPairs,
        AdventureBundle bundle) =>
        TranscriptFilterService.ApplyLookbackAndFilter(
            rawPairs,
            CreateSyncSettings(),
            bundle,
            isLiveSource: true);

    public static IReadOnlyList<TranscriptTurnPair> GetAcceptedLogPairs(AdventureBundle bundle) =>
        bundle.Log.Turns
            .Where(t => t.Status == TurnStatus.Accepted)
            .OrderBy(t => t.Index)
            .Select(t => new TranscriptTurnPair
            {
                PlayerText = t.PlayerText,
                NarratorText = t.NarratorText ?? "",
                TurnIndex = t.Index,
            })
            .ToList();

    public static ThreadLogDriftAnalysis Analyze(
        AdventureBundle bundle,
        IReadOnlyList<TranscriptTurnPair> rawThreadPairs)
    {
        var filtered = FilterThreadPairsForSync(rawThreadPairs, bundle);
        var logPairs = NormalizePairsForCompare(GetAcceptedLogPairs(bundle));
        var threadPairs = NormalizePairsForCompare(filtered);
        var (threadSuffix, logSuffix) = GetAlignedSuffixes(threadPairs, logPairs);
        var fingerprint = ComputeDriftFingerprint(threadSuffix, logSuffix);
        return new ThreadLogDriftAnalysis
        {
            CaptureSucceeded = true,
            ThreadTurnCount = threadPairs.Count,
            LogTurnCount = logPairs.Count,
            ComparedTurnCount = threadSuffix.Count,
            HasDrift = !PairsMatchSequential(threadSuffix, logSuffix),
            DriftFingerprint = fingerprint,
            ThreadPairs = filtered,
        };
    }

    public static ThreadLogDriftAnalysis AnalyzeFailed(string? captureError) =>
        new()
        {
            CaptureSucceeded = false,
            CaptureError = captureError,
        };

    public static void ApplyFromThread(AdventureBundle bundle, IReadOnlyList<TranscriptTurnPair> threadPairs)
    {
        bundle.Log.Turns.RemoveAll(t =>
            t.Status is TurnStatus.Accepted or TurnStatus.Pending);
        bundle.ThreadMetadata.Messages.RemoveAll(m => m.LinkedTurnId is not null && !m.IsUtility);

        foreach (var pair in threadPairs)
        {
            var normalized = NormalizePairForCompare(pair);
            if (string.IsNullOrWhiteSpace(normalized.PlayerText)
                && string.IsNullOrWhiteSpace(normalized.NarratorText))
            {
                continue;
            }

            var turn = TurnTimelineService.CreateTurn(bundle, normalized.PlayerText);
            TurnTimelineService.AcceptTurn(turn, normalized.NarratorText);
            ThreadMetadataService.RecordPlayTurnExchange(
                bundle,
                turn,
                turn.PlayerText,
                turn.NarratorText);
        }

        bundle.Metadata.Settings.ThreadLogDriftHint = null;
        bundle.Metadata.Settings.ThreadLogDriftDismissedHash = null;
        bundle.Summary.PendingReview = true;
    }

    public static void UpdateDriftHint(AdventureBundle bundle, ThreadLogDriftAnalysis analysis)
    {
        bundle.Metadata.Settings.ThreadLogDriftHint =
            analysis.LogTurnCount == analysis.ThreadTurnCount
                ? $"Play thread text differs from local log ({analysis.ComparedTurnCount} compared turn(s))."
                : $"Log differs from play thread ({analysis.LogTurnCount} local vs {analysis.ThreadTurnCount} thread turn(s); {analysis.ComparedTurnCount} compared).";
    }

    public static void RecordSkippedDrift(AdventureBundle bundle, ThreadLogDriftAnalysis analysis)
    {
        UpdateDriftHint(bundle, analysis);
        bundle.Metadata.Settings.ThreadLogDriftDismissedHash = analysis.DriftFingerprint;
    }

    internal static (IReadOnlyList<TranscriptTurnPair> ThreadSuffix, IReadOnlyList<TranscriptTurnPair> LogSuffix)
        GetAlignedSuffixes(
            IReadOnlyList<TranscriptTurnPair> thread,
            IReadOnlyList<TranscriptTurnPair> log)
    {
        if (thread.Count == 0 || log.Count == 0)
            return (thread, log);

        var compareCount = Math.Min(thread.Count, log.Count);
        return (
            thread.Skip(thread.Count - compareCount).ToList(),
            log.Skip(log.Count - compareCount).ToList());
    }

    internal static bool PairsMatchSequential(
        IReadOnlyList<TranscriptTurnPair> left,
        IReadOnlyList<TranscriptTurnPair> right)
    {
        if (left.Count == 0 && right.Count == 0)
            return true;

        if (left.Count != right.Count)
            return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (!PairTextsMatch(left[i], right[i]))
                return false;
        }

        return true;
    }

    internal static string ComputeDriftFingerprint(
        IReadOnlyList<TranscriptTurnPair> threadSuffix,
        IReadOnlyList<TranscriptTurnPair> logSuffix) =>
        $"{logSuffix.Count}:{threadSuffix.Count}:{FingerprintPairs(logSuffix)}:{FingerprintPairs(threadSuffix)}";

    internal static IReadOnlyList<TranscriptTurnPair> NormalizePairsForCompare(
        IReadOnlyList<TranscriptTurnPair> pairs) =>
        pairs.Select(NormalizePairForCompare).ToList();

    internal static TranscriptTurnPair NormalizePairForCompare(TranscriptTurnPair pair)
    {
        var playerRaw = pair.PlayerText ?? "";
        var player = ConversationStreamParser.ExtractTranscriptPlayerText(playerRaw)
                     ?? TranscriptTextSanitizer.Sanitize(playerRaw);
        return new TranscriptTurnPair
        {
            PlayerText = player,
            NarratorText = TranscriptTextSanitizer.Sanitize(pair.NarratorText),
            TurnIndex = pair.TurnIndex,
        };
    }

    private static bool PairTextsMatch(TranscriptTurnPair left, TranscriptTurnPair right) =>
        PlayerTextsMatch(left.PlayerText, right.PlayerText)
        && NarratorTextsMatch(left.NarratorText, right.NarratorText);

    private static bool PlayerTextsMatch(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
            return true;

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool NarratorTextsMatch(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
            return true;

        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return false;

        if (left.Length >= right.Length && left.StartsWith(right, StringComparison.Ordinal))
            return true;

        if (right.Length >= left.Length && right.StartsWith(left, StringComparison.Ordinal))
            return true;

        return false;
    }

    private static string FingerprintPairs(IReadOnlyList<TranscriptTurnPair> pairs) =>
        string.Join("\u001e", pairs.Select(p => $"{p.PlayerText}\u001f{p.NarratorText}"));
}
