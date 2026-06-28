using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal enum InjectionSectionKind
{
    Reference,
    Delta,
    ConditionalInline,
}

internal enum InjectionOmissionReason
{
    None,
    Policy,
    Budget,
    Empty,
    Delegated,
}

internal enum PacketDelegationMode
{
    SourceDelegated,
    InlineFallback,
    MinimalLocal,
}

internal sealed record InjectionSection(
    string Id,
    InjectionSectionKind Kind,
    bool Mandatory,
    bool Included,
    string? Note = null,
    int CharEstimate = 0,
    InjectionOmissionReason OmissionReason = InjectionOmissionReason.None);

internal sealed record TrimmedSection(
    string Id,
    string Reason);

internal sealed class ContextBudgetAllocationResult
{
    public List<TrimmedSection> Trimmed { get; } = [];
}

internal static class InjectionSectionManifestBuilder
{
    public static PacketDelegationMode ResolveDelegationMode(
        ProjectSourceReadiness readiness,
        PacketProfile profile) =>
        profile switch
        {
            PacketProfile.SourceDelegated when readiness.CanDelegateStaticContent => PacketDelegationMode.SourceDelegated,
            PacketProfile.MinimalLocal => PacketDelegationMode.MinimalLocal,
            _ => PacketDelegationMode.InlineFallback,
        };

    public static IReadOnlyList<InjectionSection> BuildSections(
        AdventureBundle bundle,
        string mergedText,
        string contextText,
        PacketProfile profile,
        ProjectSourceReadiness readiness)
    {
        var pointerFirst = profile is PacketProfile.SourceDelegated or PacketProfile.MinimalLocal;
        var delegated = profile == PacketProfile.SourceDelegated && readiness.CanDelegateStaticContent;
        var sections = new List<InjectionSection>();

        void Add(string id, InjectionSectionKind kind, bool mandatory, bool included, string? note = null) =>
            sections.Add(new InjectionSection(id, kind, mandatory, included, note));

        Add("meta", InjectionSectionKind.Delta, mandatory: true,
            included: mergedText.Contains("[[cgw:meta", StringComparison.Ordinal)
                      || mergedText.Contains("turn=", StringComparison.Ordinal));

        var hasSourcesTag = contextText.Contains("[[cgw:sources", StringComparison.Ordinal)
                            || mergedText.Contains("[[cgw:sources", StringComparison.Ordinal);
        var hasLegacySources = mergedText.Contains("=== PROJECT SOURCES", StringComparison.Ordinal);
        Add("sources",
            delegated ? InjectionSectionKind.Reference : InjectionSectionKind.ConditionalInline,
            mandatory: delegated,
            included: hasSourcesTag || hasLegacySources);

        var hasInstructionsTag = contextText.Contains("[[cgw:instructions", StringComparison.Ordinal);
        var hasInlineContract = mergedText.Contains("Content boundaries:", StringComparison.Ordinal);
        Add("instructions",
            delegated ? InjectionSectionKind.Reference : InjectionSectionKind.ConditionalInline,
            mandatory: false,
            included: hasInstructionsTag || hasInlineContract,
            note: hasInlineContract && pointerFirst && profile != PacketProfile.InlineFallback
                ? "unexpected inline contract"
                : null);

        Add("summary", InjectionSectionKind.Delta, mandatory: false,
            included: contextText.Contains("[[cgw:summary", StringComparison.Ordinal)
                      || mergedText.Contains("=== STORY SO FAR", StringComparison.Ordinal));

        Add("state", InjectionSectionKind.Delta, mandatory: true,
            included: contextText.Contains("[[cgw:state", StringComparison.Ordinal)
                      || mergedText.Contains("=== STATE DELTA", StringComparison.Ordinal)
                      || mergedText.Contains("=== CURRENT STATE", StringComparison.Ordinal));

        Add("memory", InjectionSectionKind.Delta, mandatory: false,
            included: contextText.Contains("[[cgw:memory", StringComparison.Ordinal)
                      || mergedText.Contains("=== PINNED MEMORY", StringComparison.Ordinal));

        Add("transcript", InjectionSectionKind.Delta, mandatory: false,
            included: contextText.Contains("[[cgw:transcript", StringComparison.Ordinal)
                      || mergedText.Contains("=== RECENT TRANSCRIPT", StringComparison.Ordinal));

        Add("cards", InjectionSectionKind.Delta, mandatory: false,
            included: contextText.Contains("[[cgw:cards", StringComparison.Ordinal)
                      || mergedText.Contains("=== RELEVANT LORE CARDS", StringComparison.Ordinal)
                      || mergedText.Contains("=== TRIGGERED CARDS", StringComparison.Ordinal));

        Add("overrides", InjectionSectionKind.Delta, mandatory: false,
            included: mergedText.Contains("=== TURN OVERRIDES ===", StringComparison.Ordinal));

        Add("turn-directive", InjectionSectionKind.Delta, mandatory: false,
            included: mergedText.Contains("=== TURN DIRECTIVE ===", StringComparison.Ordinal));

        Add("canon-notify", InjectionSectionKind.Delta, mandatory: false,
            included: mergedText.Contains("CANON UPDATE", StringComparison.Ordinal));

        Add("attachment-manifest", InjectionSectionKind.Delta, mandatory: false,
            included: mergedText.Contains("=== ATTACHMENTS (staged with this turn) ===", StringComparison.Ordinal));

        Add("attachment-guidance", InjectionSectionKind.Delta, mandatory: false,
            included: mergedText.Contains("=== ATTACHMENT GUIDANCE ===", StringComparison.Ordinal));

        Add("player", InjectionSectionKind.Delta, mandatory: true,
            included: !string.IsNullOrWhiteSpace(ExtractPlayerTail(mergedText, contextText)));

        EnrichWithPolicyOmissions(bundle, sections, profile, readiness);
        ApplyCharEstimates(mergedText, contextText, sections);

        return sections;
    }

    public static string GetDisplayName(string sectionId) =>
        sectionId switch
        {
            "meta" => "Packet meta",
            "sources" => "Project sources",
            "instructions" => "Narrator contract",
            "summary" => "Story so far",
            "state" => "Current state",
            "memory" => "Pinned memory",
            "transcript" => "Recent transcript",
            "cards" => "Lore cards",
            "overrides" => "Behavior overrides",
            "turn-directive" => "Turn directive",
            "canon-notify" => "Canon update notice",
            "attachment-manifest" => "Attachments",
            "attachment-guidance" => "Attachment guidance",
            "player" => "Your message",
            _ => sectionId,
        };

    public static IReadOnlyList<InjectionSectionViewModel> ToViewModels(
        IReadOnlyList<InjectionSection> sections,
        IReadOnlyList<TrimmedSection> trimmed,
        IReadOnlyList<InjectionSectionDelta>? deltas = null,
        string? behaviorOverrideSnippet = null)
    {
        var deltaMap = deltas?.ToDictionary(d => d.SectionId, StringComparer.OrdinalIgnoreCase)
                       ?? new Dictionary<string, InjectionSectionDelta>(StringComparer.OrdinalIgnoreCase);
        var trimmedIds = trimmed.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return sections.Select(s =>
        {
            deltaMap.TryGetValue(s.Id, out var delta);
            var contentHint = s.Id == "overrides" ? behaviorOverrideSnippet : null;
            var status = FormatStatusBadge(
                s,
                trimmedIds.Contains(s.Id),
                delta,
                s.CharEstimate,
                contentHint);

            return new InjectionSectionViewModel(
                s.Id,
                GetDisplayName(s.Id),
                status,
                s.Included,
                s.CharEstimate,
                s.Note,
                delta?.Summary);
        }).ToList();
    }

    private static string FormatStatusBadge(
        InjectionSection section,
        bool isTrimmed,
        InjectionSectionDelta? delta,
        int charEstimate,
        string? contentHint)
    {
        if (!section.Included)
            return "Omitted";

        if (isTrimmed)
            return charEstimate > 0 ? $"Trimmed · {charEstimate:N0} ch" : "Trimmed";

        if (delta?.Summary is { Length: > 0 } change)
            return charEstimate > 0 ? $"{change} · {charEstimate:N0} ch" : change;

        if (!string.IsNullOrWhiteSpace(contentHint))
        {
            var hint = contentHint.Length > 72 ? contentHint[..72] + "…" : contentHint;
            return charEstimate > 0 ? $"{hint} · {charEstimate:N0} ch" : hint;
        }

        var kindLabel = section.Kind switch
        {
            InjectionSectionKind.Reference => "Pointer only",
            InjectionSectionKind.ConditionalInline => "In packet",
            InjectionSectionKind.Delta => "In packet",
            _ => "In packet",
        };
        return charEstimate > 0 ? $"{kindLabel} · {charEstimate:N0} ch" : kindLabel;
    }

    private static void EnrichWithPolicyOmissions(
        AdventureBundle bundle,
        List<InjectionSection> sections,
        PacketProfile profile,
        ProjectSourceReadiness readiness)
    {
        var policy = PlayInjectionPolicyService.Resolve(bundle.Metadata.Settings);
        var thinDelegated = profile == PacketProfile.SourceDelegated && readiness.CanDelegateStaticContent;

        void EnsureOmitted(string id, InjectionSectionKind kind, bool mandatory, bool policyOff)
        {
            if (!policyOff)
                return;

            var existing = sections.FindIndex(s => s.Id == id);
            if (existing >= 0 && sections[existing].Included)
                return;

            var note = "disabled by injection policy";
            if (existing >= 0)
                sections[existing] = sections[existing] with { Included = false, Note = note, OmissionReason = InjectionOmissionReason.Policy };
            else
                sections.Add(new InjectionSection(id, kind, mandatory, Included: false, note, OmissionReason: InjectionOmissionReason.Policy));
        }

        EnsureOmitted("summary", InjectionSectionKind.Delta, false, !policy.IncludeSummary);
        EnsureOmitted("memory", InjectionSectionKind.Delta, false, !policy.IncludePinnedMemory);
        EnsureOmitted("transcript", InjectionSectionKind.Delta, false, !policy.IncludeTranscript);
        EnsureOmitted("cards", InjectionSectionKind.Delta, false, !policy.IncludeTriggeredCards);
        EnsureOmitted("state", InjectionSectionKind.Delta, mandatory: thinDelegated, !policy.IncludeState);
        EnsureOmitted("sources", thinDelegated ? InjectionSectionKind.Reference : InjectionSectionKind.ConditionalInline,
            mandatory: thinDelegated, !policy.IncludeSourcesPointers);
        EnsureOmitted("attachment-guidance", InjectionSectionKind.Delta, false, !bundle.Metadata.Settings.InjectAttachmentGuidance);
    }

    private static void ApplyCharEstimates(string mergedText, string contextText, List<InjectionSection> sections)
    {
        for (var i = 0; i < sections.Count; i++)
        {
            var s = sections[i];
            if (!s.Included)
                continue;

            var estimate = EstimateSectionChars(s.Id, mergedText, contextText);
            if (estimate > 0)
                sections[i] = s with { CharEstimate = estimate };
        }
    }

    private static int EstimateSectionChars(string id, string mergedText, string contextText)
    {
        var tagBody = ContextTagFormat.ExtractBlock(mergedText, id)
                      ?? ContextTagFormat.ExtractBlock(contextText, id);
        if (!string.IsNullOrWhiteSpace(tagBody))
            return tagBody.Length;

        return id switch
        {
            "overrides" when mergedText.Contains("=== TURN OVERRIDES ===", StringComparison.Ordinal) =>
                ExtractLegacyBlock(mergedText, "=== TURN OVERRIDES ===").Length,
            "turn-directive" when mergedText.Contains("=== TURN DIRECTIVE ===", StringComparison.Ordinal) =>
                ExtractLegacyBlock(mergedText, "=== TURN DIRECTIVE ===").Length,
            "player" => ExtractPlayerTail(mergedText, contextText).Length,
            _ => 0,
        };
    }

    private static string ExtractLegacyBlock(string text, string header)
    {
        var idx = text.IndexOf(header, StringComparison.Ordinal);
        if (idx < 0)
            return "";

        var start = idx + header.Length;
        var next = text.IndexOf("\n=== ", start, StringComparison.Ordinal);
        return next < 0 ? text[start..].Trim() : text[start..next].Trim();
    }

    public static string FormatSectionSummary(IReadOnlyList<InjectionSection> sections)
    {
        if (sections.Count == 0)
            return "";

        static string Badge(InjectionSectionKind kind) => kind switch
        {
            InjectionSectionKind.Reference => "reference",
            InjectionSectionKind.Delta => "delta",
            InjectionSectionKind.ConditionalInline => "inline",
            _ => "unknown",
        };

        var parts = sections
            .Select(s =>
            {
                var status = s.Included ? Badge(s.Kind) : "omitted";
                var note = string.IsNullOrWhiteSpace(s.Note) ? "" : $" ({s.Note})";
                return $"[{status}] {s.Id}{note}";
            });

        return "Sections: " + string.Join(" · ", parts);
    }

    public static string FormatTrimmedSummary(IReadOnlyList<TrimmedSection> trimmed)
    {
        if (trimmed.Count == 0)
            return "";

        var parts = trimmed.Select(t => $"{t.Id} ({t.Reason})");
        return "Trimmed: " + string.Join(" · ", parts);
    }

    private static string ExtractPlayerTail(string merged, string context)
    {
        if (string.IsNullOrWhiteSpace(merged))
            return "";

        if (!string.IsNullOrWhiteSpace(context) && merged.Length > context.Length)
        {
            var tail = merged[context.Length..].Trim();
            if (!string.IsNullOrWhiteSpace(tail))
                return tail;
        }

        if (merged.Contains("=== PLAYER TURN ===", StringComparison.Ordinal))
        {
            var idx = merged.LastIndexOf("=== PLAYER TURN ===", StringComparison.Ordinal);
            return merged[(idx + "=== PLAYER TURN ===".Length)..].Trim();
        }

        return merged.Trim();
    }
}
