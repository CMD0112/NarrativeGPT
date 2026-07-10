using System.Text;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class UtilityStoryContextBuildResult
{
    public string Text { get; init; } = "";

    public StoryContextSourceUsed TranscriptSource { get; init; }

    public int TurnPairCount { get; init; }

    public string? CaptureError { get; init; }

    public UtilityContextManifestRecord? Manifest { get; init; }

    public string? JobCorePreview { get; init; }

    public bool HasTranscriptSection =>
        TurnPairCount > 0 && Text.Contains("=== STORY TRANSCRIPT ===", StringComparison.Ordinal);

    public string FormatStatusHint()
    {
        var source = TranscriptSource switch
        {
            StoryContextSourceUsed.LiveApi => "live API",
            StoryContextSourceUsed.LiveDom => "live DOM",
            StoryContextSourceUsed.LocalLog => "local log",
            _ => "none",
        };

        var chars = Text.Length;
        var charLabel = chars >= 1000 ? $"{chars / 1000.0:0.#}k chars" : $"{chars} chars";
        var hint = $"story context: {source} · {TurnPairCount} pair(s) · {charLabel}";
        if (!string.IsNullOrWhiteSpace(CaptureError))
            hint += $" ({CaptureError})";
        if (Manifest is not null)
            hint += $" · {Manifest.FormatSummary()}";
        return hint;
    }

    public string FormatPreviewBody()
    {
        var sb = new StringBuilder();
        if (Manifest is not null)
        {
            sb.AppendLine("=== CONTEXT MANIFEST ===");
            sb.AppendLine(Manifest.FormatSummary());
            if (Manifest.SectionsIncluded.Count > 0)
                sb.AppendLine($"  included: {string.Join(", ", Manifest.SectionsIncluded)}");
            if (Manifest.SectionsOmitted.Count > 0)
                sb.AppendLine($"  omitted: {string.Join(", ", Manifest.SectionsOmitted)}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(Text))
        {
            sb.AppendLine("=== STORY BLOCK ===");
            sb.AppendLine(Text.Trim());
        }

        if (!string.IsNullOrWhiteSpace(JobCorePreview))
        {
            sb.AppendLine();
            sb.AppendLine("=== JOB CORE (deduped) ===");
            sb.AppendLine(JobCorePreview.Trim());
        }

        return sb.ToString().TrimEnd();
    }
}

internal sealed class UtilityStoryContextBuilder(PlayThreadTranscriptService transcriptService)
{
    public async Task<UtilityStoryContextBuildResult> BuildAsync(
        AdventureBundle bundle,
        string jobId,
        CoreWebView2? playCore,
        CancellationToken cancellationToken = default,
        bool domOnlyCapture = false)
    {
        var settings = UtilityStoryContextSettingsService.Resolve(bundle, jobId);
        var capture = await transcriptService.CaptureAsync(
            bundle,
            settings,
            playCore,
            cancellationToken,
            domOnlyCapture);
        var text = Assemble(bundle, settings, capture.TurnPairs);

        return new UtilityStoryContextBuildResult
        {
            Text = text,
            TranscriptSource = capture.SourceUsed,
            TurnPairCount = capture.TurnPairs.Count,
            CaptureError = capture.Error,
        };
    }

    public static UtilityStoryContextBuildResult BuildPreviewFromLocal(AdventureBundle bundle, string jobId)
    {
        var settings = UtilityStoryContextSettingsService.Resolve(bundle, jobId);
        var capture = PlayThreadTranscriptService.CaptureFromLocal(bundle, settings);
        return new UtilityStoryContextBuildResult
        {
            Text = Assemble(bundle, settings, capture.TurnPairs),
            TranscriptSource = capture.SourceUsed,
            TurnPairCount = capture.TurnPairs.Count,
        };
    }

    private static string Assemble(
        AdventureBundle bundle,
        UtilityStoryContextSettings settings,
        IReadOnlyList<TranscriptTurnPair> turnPairs)
    {
        var normalized = UtilityStoryContextSettingsNormalizer.Normalize(settings);
        var sections = new List<string>();

        if (!string.IsNullOrWhiteSpace(normalized.DirectionPreamble))
        {
            sections.Add($"""
                === STORY DIRECTION ===
                {normalized.DirectionPreamble.Trim()}
                """);
        }

        if (normalized.IncludeRollingSummary && !string.IsNullOrWhiteSpace(bundle.Summary.RollingSummary))
        {
            sections.Add($"""
                === ROLLING SUMMARY ===
                {bundle.Summary.RollingSummary.Trim()}
                """);
        }

        if (normalized.IncludeState)
        {
            var state = EntityExtractionService.BuildWorldSnapshot(
                bundle,
                includeSummary: !normalized.IncludeRollingSummary);
            if (state != "(none)")
            {
                sections.Add($"""
                    === STATE ===
                    {state}
                    """);
            }
        }

        if (normalized.IncludePinnedMemory)
        {
            var pinned = bundle.Memory.Entries.Where(e => e.Pinned).Select(e => e.Text).ToList();
            if (pinned.Count > 0)
            {
                sections.Add($"""
                    === PINNED MEMORIES ===
                    {string.Join(Environment.NewLine, pinned.Select(p => $"- {p}"))}
                    """);
            }
        }

        if (normalized.IncludeEntityIndex)
        {
            var index = EntityExtractionService.BuildCompactEntityIndex(bundle.Entities);
            if (index != "(none)")
            {
                sections.Add($"""
                    === ENTITY INDEX ===
                    {index}
                    """);
            }
        }

        if (normalized.IncludeScenarioExcerpt)
        {
            var s = bundle.Scenario;
            sections.Add($"""
                === SCENARIO EXCERPT ===
                Title: {bundle.Metadata.Title}
                Setting: {s.Setting}
                Opening: {s.OpeningSituation}
                Plot essentials: {s.PlotEssentials}
                """);
        }

        if (turnPairs.Count > 0
            && (normalized.IncludePlayerMessages || normalized.IncludeNarratorMessages))
        {
            var transcript = FormatTranscript(turnPairs, normalized);
            if (!string.IsNullOrWhiteSpace(transcript))
            {
                if (normalized.MaxTranscriptChars > 0 && transcript.Length > normalized.MaxTranscriptChars)
                    transcript = "…" + transcript[^(normalized.MaxTranscriptChars - 1)..];

                sections.Add($"""
                    === STORY TRANSCRIPT ===
                    {transcript}
                    """);
            }
        }

        return ApplyTrimStrategy(string.Join(Environment.NewLine + Environment.NewLine, sections), normalized);
    }

    internal static string FormatTranscript(
        IReadOnlyList<TranscriptTurnPair> pairs,
        UtilityStoryContextSettings settings)
    {
        var normalized = UtilityStoryContextSettingsNormalizer.Normalize(settings);
        var lines = new List<string>();

        foreach (var pair in pairs)
        {
            var player = normalized.IncludePlayerMessages
                ? TranscriptTextSanitizer.Sanitize(pair.PlayerText)
                : "";
            var narrator = normalized.IncludeNarratorMessages
                ? TranscriptTextSanitizer.Sanitize(pair.NarratorText)
                : "";

            if (string.IsNullOrWhiteSpace(player) && string.IsNullOrWhiteSpace(narrator))
                continue;

            if (normalized.Format == UtilityTranscriptFormat.CompactArrow)
            {
                lines.Add(!string.IsNullOrWhiteSpace(player) && !string.IsNullOrWhiteSpace(narrator)
                    ? $"{player} -> {narrator}"
                    : player + narrator);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(player))
                lines.Add($"PLAYER:\n{player}");
            if (!string.IsNullOrWhiteSpace(narrator))
                lines.Add($"NARRATOR:\n{narrator}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    internal static string ApplyTrimStrategy(string text, UtilityStoryContextSettings settings)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= settings.MaxContextChars)
            return text.Trim();

        return settings.Trim switch
        {
            UtilityTrimStrategy.HeadAndTail => TrimHeadAndTail(text, settings.MaxContextChars),
            UtilityTrimStrategy.TranscriptOnly => TrimTranscriptSection(text, settings.MaxContextChars),
            _ => text[^settings.MaxContextChars..].TrimStart(),
        };
    }

    private static string TrimHeadAndTail(string text, int maxChars)
    {
        if (text.Length <= maxChars)
            return text;

        var headLen = maxChars / 3;
        var tailLen = maxChars - headLen - 40;
        return text[..headLen] + Environment.NewLine + "…[trimmed]…" + Environment.NewLine + text[^tailLen..];
    }

    private static string TrimTranscriptSection(string text, int maxChars)
    {
        const string marker = "=== STORY TRANSCRIPT ===";
        var idx = text.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
            return text.Length <= maxChars ? text : text[^maxChars..];

        var prefix = text[..idx].TrimEnd();
        var transcript = text[(idx + marker.Length)..].TrimStart();
        var budget = maxChars - prefix.Length - marker.Length - 8;
        if (budget < 200)
            return text[^maxChars..];

        if (transcript.Length > budget)
            transcript = "…" + transcript[^(budget - 1)..];

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            new[] { prefix, marker, transcript }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }
}
