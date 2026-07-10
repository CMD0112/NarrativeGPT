using System.Security.Cryptography;
using System.Text;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class UtilityTranscriptScope
{
    public TranscriptTurnPair? TargetPair { get; init; }

    public MemoryAnchor Anchor { get; init; } = new();

    public string ScopeHash { get; init; } = "";
}

internal static class UtilityTranscriptScopeService
{
    public const int PlayerHintLength = 60;

    public static UtilityTranscriptScope? ResolveFromPairs(
        IReadOnlyList<TranscriptTurnPair> pairs,
        int pairOffset = 0)
    {
        if (pairs.Count == 0)
            return null;

        var index = pairs.Count - 1 - pairOffset;
        if (index < 0 || index >= pairs.Count)
            return null;

        var pair = pairs[index];
        var anchor = BuildAnchor(pair, pairOffset);
        return new UtilityTranscriptScope
        {
            TargetPair = pair,
            Anchor = anchor,
            ScopeHash = ComputeScopeHash(pair),
        };
    }

    public static UtilityTranscriptScope? ResolveFromLocalLog(AdventureBundle bundle, int pairOffset = 0)
    {
        var settings = new UtilityStoryContextSettings
        {
            Source = UtilityStorySource.LocalLog,
            MaxTurnPairs = pairOffset + 1,
            LookbackAnchor = UtilityLookbackAnchor.FromEnd,
            Format = UtilityTranscriptFormat.CompactArrow,
        };
        var capture = PlayThreadTranscriptService.CaptureFromLocal(bundle, settings);
        var pairs = capture.TurnPairs.ToList();
        return ResolveFromPairs(pairs, pairOffset);
    }

    public static UtilityTranscriptScope? ResolveFallbackTurn(AdventureBundle bundle)
    {
        var turn = bundle.Log.Turns
            .Where(t => t.Status == TurnStatus.Accepted && !string.IsNullOrWhiteSpace(t.NarratorText))
            .OrderByDescending(t => t.Index)
            .FirstOrDefault();

        if (turn is null)
            return null;

        var pair = new TranscriptTurnPair
        {
            TurnIndex = turn.Index,
            PlayerText = turn.PlayerText,
            NarratorText = turn.NarratorText ?? "",
        };
        var anchor = BuildAnchor(pair, pairOffset: 0);
        anchor.TurnIndex = turn.Index;
        anchor.Kind = "log";
        return new UtilityTranscriptScope
        {
            TargetPair = pair,
            Anchor = anchor,
            ScopeHash = ComputeScopeHash(pair),
        };
    }

    public static MemoryAnchor BuildAnchor(TranscriptTurnPair pair, int pairOffset)
    {
        var player = pair.PlayerText?.Trim() ?? "";
        return new MemoryAnchor
        {
            Kind = "transcript",
            PairOffset = pairOffset,
            PlayerHint = TruncateHint(player),
            CapturedAt = DateTimeOffset.UtcNow,
            TurnIndex = pair.TurnIndex,
            ContentHash = ComputeScopeHash(pair),
        };
    }

    public static string FormatScopeBlock(UtilityTranscriptScope scope) =>
        $"""
            === SCOPE ===
            Target: newest play pair (offset {scope.Anchor.PairOffset}).
            Player hint: {scope.Anchor.PlayerHint ?? "(none)"}
            """;

    public static bool IsDuplicateMemory(MemoryDocument memory, MemoryEntry candidate, GenerationJobContext? context = null)
    {
        IEnumerable<MemoryEntry> candidates = memory.ReviewQueue.Concat(memory.Entries);
        if (context?.AllowCrossSourceDuplicates == true
            && !string.IsNullOrWhiteSpace(context.InferenceSource))
        {
            candidates = candidates.Where(e =>
                string.Equals(e.InferenceSource, context.InferenceSource, StringComparison.OrdinalIgnoreCase));
        }

        if (candidate.Anchor?.ContentHash is { } hash
            && candidates.Any(e =>
                string.Equals(e.Anchor?.ContentHash, hash, StringComparison.Ordinal)
                && string.Equals(NormalizeText(e.Text), NormalizeText(candidate.Text), StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return candidates.Any(e =>
            e.Anchor is not null
            && candidate.Anchor is not null
            && e.Anchor.PairOffset == candidate.Anchor.PairOffset
            && string.Equals(NormalizeText(e.Text), NormalizeText(candidate.Text), StringComparison.OrdinalIgnoreCase));
    }

    private static string ComputeScopeHash(TranscriptTurnPair pair)
    {
        var payload = $"{pair.TurnIndex}|{pair.PlayerText}|{pair.NarratorText}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes)[..16];
    }

    private static string TruncateHint(string player)
    {
        if (string.IsNullOrWhiteSpace(player))
            return "";

        var t = player.Trim();
        return t.Length <= PlayerHintLength ? t : t[..PlayerHintLength] + "…";
    }

    private static string NormalizeText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : text.Trim();
}
