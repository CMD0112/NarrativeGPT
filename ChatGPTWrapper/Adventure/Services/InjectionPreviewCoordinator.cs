using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed record InjectionSectionViewModel(
    string Id,
    string DisplayName,
    string StatusBadge,
    bool Included,
    int CharEstimate,
    string? Note,
    string? DeltaSummary);

public sealed record InjectionSectionDelta(
    string SectionId,
    string Summary,
    bool WasIncluded,
    bool IsIncluded);

public sealed record InjectionPreviewSnapshot(
    string MergedText,
    string PacketHash,
    int CharCount,
    bool WasTrimmed,
    string ModeLabel,
    string DelegationLabel,
    IReadOnlyList<InjectionSectionViewModel> SectionRows,
    IReadOnlyList<InjectionSectionDelta> Deltas,
    IReadOnlyList<string> DeltaMessages,
    string FormattedBody,
    string MetaLine,
    string ManifestSummary,
    int MaxPacketChars,
    bool HasPlayerLine);

public static class InjectionPreviewCoordinator
{
    public static InjectionPreviewSnapshot Refresh(
        AdventureBundle stagingBundle,
        string playerLine,
        AttachmentContext? attachment,
        Func<string?>? resolveComposerText,
        string fallbackPlayerLine,
        InjectionPreviewSnapshot? previous = null,
        int priorThreadUserMessageCount = 0)
    {
        PlayInjectionPolicyService.EnsureDefaults(stagingBundle.Metadata);

        var resolvedLine = InjectionPreviewFormatter.ResolvePreviewPlayerLine(
            stagingBundle, resolveComposerText, fallbackPlayerLine);
        if (string.IsNullOrWhiteSpace(resolvedLine))
            resolvedLine = playerLine.Trim();

        var hasPlayerLine = !string.IsNullOrWhiteSpace(resolvedLine);
        if (!hasPlayerLine)
        {
            return EmptySnapshot(stagingBundle, previous);
        }

        var prepared = PromptInjectionService.PrepareSend(
            stagingBundle, resolvedLine, attachment, priorThreadUserMessageCount);
        var readiness = ProjectSourceInjectionService.Evaluate(stagingBundle);

        var deltas = ComputeDeltas(previous, prepared.Sections);
        var deltaMessages = BuildDeltaMessages(deltas, prepared);
        var overrideSnippet = ExtractOverrideSnippet(prepared.MergedText);

        var sectionRows = InjectionSectionManifestBuilder.ToViewModels(
            prepared.Sections, prepared.Trimmed, deltas, overrideSnippet);

        var formattedBody = stagingBundle.Metadata.Settings.UseContextTags
            ? ContextTagFormat.FormatStructuredPreview(prepared.MergedText)
            : prepared.MergedText;

        var metaLine = InjectionPreviewFormatter.FormatMetaLine(
            stagingBundle, prepared, readiness, attachment);
        var manifestSummary = InjectionPreviewFormatter.FormatManifestSummary(prepared);

        return new InjectionPreviewSnapshot(
            prepared.MergedText,
            prepared.Hash,
            prepared.MergedText.Length,
            prepared.WasTrimmed,
            PacketProfileResolver.ProfileMetaMode(prepared.Profile),
            PacketProfileResolver.DisplayLabel(prepared.Profile, readiness),
            sectionRows,
            deltas,
            deltaMessages,
            formattedBody,
            metaLine,
            manifestSummary,
            stagingBundle.Metadata.Settings.MaxPacketChars,
            hasPlayerLine);
    }

    private static InjectionPreviewSnapshot EmptySnapshot(
        AdventureBundle bundle,
        InjectionPreviewSnapshot? previous)
    {
        return new InjectionPreviewSnapshot(
            "",
            "",
            0,
            false,
            "minimal",
            "Minimal local",
            [],
            previous?.Deltas ?? [],
            ["Type in the composer or set a fallback line to preview the next packet."],
            "",
            "",
            "",
            bundle.Metadata.Settings.MaxPacketChars,
            HasPlayerLine: false);
    }

    private static List<InjectionSectionDelta> ComputeDeltas(
        InjectionPreviewSnapshot? previous,
        IReadOnlyList<InjectionSection> current)
    {
        if (previous is null || previous.SectionRows.Count == 0)
            return [];

        var prevMap = previous.SectionRows.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var deltas = new List<InjectionSectionDelta>();

        foreach (var cur in current)
        {
            if (!prevMap.TryGetValue(cur.Id, out var prev))
            {
                if (cur.Included)
                {
                    deltas.Add(new InjectionSectionDelta(
                        cur.Id,
                        $"{InjectionSectionManifestBuilder.GetDisplayName(cur.Id)} added",
                        WasIncluded: false,
                        IsIncluded: true));
                }

                continue;
            }

            if (prev.Included != cur.Included)
            {
                var name = InjectionSectionManifestBuilder.GetDisplayName(cur.Id);
                deltas.Add(new InjectionSectionDelta(
                    cur.Id,
                    cur.Included ? $"{name} now included" : $"{name} now omitted",
                    prev.Included,
                    cur.Included));
            }
        }

        return deltas;
    }

    private static List<string> BuildDeltaMessages(
        IReadOnlyList<InjectionSectionDelta> deltas,
        PromptInjectionPrepareResult prepared)
    {
        var messages = deltas.Select(d => d.Summary).ToList();

        foreach (var trimmed in prepared.Trimmed)
            messages.Add($"Trimmed: {trimmed.Id} ({trimmed.Reason})");

        if (prepared.WasTrimmed && prepared.Trimmed.All(t => t.Id != "packet"))
            messages.Add("Packet tail truncated (MaxPacketChars)");

        var overrideBlock = ExtractOverrideSnippet(prepared.MergedText);
        if (!string.IsNullOrWhiteSpace(overrideBlock))
            messages.Add("Will inject override block: " + overrideBlock);

        return messages;
    }

    private static string? ExtractOverrideSnippet(string mergedText)
    {
        if (!mergedText.Contains("=== TURN OVERRIDES ===", StringComparison.Ordinal))
            return null;

        var start = mergedText.IndexOf("=== TURN OVERRIDES ===", StringComparison.Ordinal)
                    + "=== TURN OVERRIDES ===".Length;
        var end = mergedText.IndexOf("\n=== ", start, StringComparison.Ordinal);
        var body = (end < 0 ? mergedText[start..] : mergedText[start..end]).Trim();
        if (body.Length > 120)
            body = body[..120] + "…";
        return string.IsNullOrWhiteSpace(body) ? null : body.Replace('\n', ' ');
    }
}
