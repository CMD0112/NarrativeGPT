using System.IO;
using System.Security.Cryptography;
using System.Text;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.NarratorScales;

namespace ChatGPTWrapper.Adventure.Services;

internal enum PacketMode
{
    Fat,
    Thin,
}

internal sealed class PromptPacketResult
{
    public required string Text { get; init; }

    public required string Hash { get; init; }

    public List<string> TriggeredCardNames { get; init; } = [];

    public List<string> ResolvedSectionPointers { get; init; } = [];

    public bool WasTrimmed { get; init; }

    public PacketMode Mode { get; init; } = PacketMode.Fat;

    public PacketProfile Profile { get; init; } = PacketProfile.InlineFallback;

    public List<TrimmedSection> BudgetTrimmed { get; init; } = [];
}

internal sealed class PromptPacketContextResult
{
    public required string ContextText { get; init; }

    public List<string> TriggeredCardNames { get; init; } = [];

    public List<string> ResolvedSectionPointers { get; init; } = [];

    public PacketMode Mode { get; init; } = PacketMode.Fat;

    public PacketProfile Profile { get; init; } = PacketProfile.InlineFallback;

    public int MaxChars { get; init; }

    public bool UseContextTags { get; init; }

    public List<TrimmedSection> BudgetTrimmed { get; init; } = [];
}

internal static class PromptPacketBuilder
{
    public static bool UseThinPackets(AdventureBundle bundle) =>
        PacketProfileResolver.Resolve(bundle) != PacketProfile.InlineFallback;

    public static PromptPacketContextResult BuildContext(
        AdventureBundle bundle,
        string searchHint = "",
        AttachmentContextMode contextMode = AttachmentContextMode.Auto,
        AttachmentContext? attachment = null,
        int? packetTurnIndexOverride = null,
        PlayHandoffContext? handoff = null,
        bool freshNarrativeBootstrap = false,
        bool userChoseInlineFallback = false)
    {
        return PacketProfileResolver.Resolve(bundle, userChoseInlineFallback) switch
        {
            PacketProfile.SourceDelegated => BuildSourceDelegatedContext(
                bundle, searchHint, contextMode, attachment, packetTurnIndexOverride, handoff, freshNarrativeBootstrap),
            PacketProfile.MinimalLocal => BuildMinimalLocalContext(
                bundle, searchHint, contextMode, attachment, packetTurnIndexOverride, handoff, freshNarrativeBootstrap),
            _ => BuildInlineFallbackContext(
                bundle, searchHint, contextMode, attachment, packetTurnIndexOverride, handoff, freshNarrativeBootstrap),
        };
    }

    public static string AssembleWithUser(string contextText, string userText, bool useContextTags)
    {
        var trimmedUser = userText.Trim();
        if (string.IsNullOrWhiteSpace(trimmedUser))
            return contextText;

        if (useContextTags)
        {
            return string.IsNullOrWhiteSpace(contextText)
                ? trimmedUser
                : contextText + "\n\n" + trimmedUser;
        }

        return string.IsNullOrWhiteSpace(contextText)
            ? BuildPlayerSection(trimmedUser)
            : contextText + "\n\n" + BuildPlayerSection(trimmedUser);
    }

    public static PromptPacketResult Build(
        AdventureBundle bundle,
        string playerInput,
        AttachmentContextMode contextMode = AttachmentContextMode.Auto,
        AttachmentContext? attachment = null,
        int? packetTurnIndexOverride = null,
        PlayHandoffContext? handoff = null,
        bool freshNarrativeBootstrap = false,
        bool userChoseInlineFallback = false)
    {
        var ctx = BuildContext(
            bundle, playerInput, contextMode, attachment, packetTurnIndexOverride, handoff,
            freshNarrativeBootstrap, userChoseInlineFallback);
        var merged = AssembleWithUser(ctx.ContextText, playerInput, ctx.UseContextTags);
        var trimmed = merged.Length > ctx.MaxChars;
        if (trimmed && !AttachmentSendPolicy.ShouldSkipTrim(contextMode))
            merged = TrimPacket(merged, ctx.MaxChars);

        return new PromptPacketResult
        {
            Text = merged,
            Hash = ComputeHash(merged),
            TriggeredCardNames = ctx.TriggeredCardNames,
            ResolvedSectionPointers = ctx.ResolvedSectionPointers,
            WasTrimmed = trimmed,
            Mode = ctx.Mode,
            Profile = ctx.Profile,
            BudgetTrimmed = ctx.BudgetTrimmed,
        };
    }

    private static PromptPacketContextResult BuildSourceDelegatedContext(
        AdventureBundle bundle,
        string searchHint,
        AttachmentContextMode contextMode,
        AttachmentContext? attachment,
        int? packetTurnIndexOverride = null,
        PlayHandoffContext? handoff = null,
        bool freshNarrativeBootstrap = false)
    {
        var settings = bundle.Metadata.Settings;
        var policy = PlayInjectionPolicyService.Resolve(settings);
        var readiness = ProjectSourceInjectionService.Evaluate(bundle);
        var profile = PacketProfile.SourceDelegated;
        var mode = PacketProfileResolver.ToPacketMode(profile);
        InjectionPolicyGuard.EnforceMandatorySections(settings, readiness.CanDelegateStaticContent);

        var signals = ContextSignalBuilder.Build(bundle, searchHint, contextMode, attachment);
        var resolved = ContextPointerResolver.Resolve(
            bundle, signals, fatFallback: false, freshNarrativeBootstrap);
        if (!policy.IncludeTriggeredCards)
            FilterTriggerPointers(resolved);
        var maxChars = PacketProfileResolver.ResolveMaxChars(profile, settings);
        var budgetResult = ContextBudgetAllocator.ApplyBudget(resolved.All, maxChars, fatFallback: false);

        var sections = new List<string>();
        if (policy.IncludeSourcesPointers)
            sections.Add(ContextPointerRenderer.BuildSourcesV2Block(
                bundle, resolved, profile, readiness, settings.UseContextTags));
        sections.Add("You are the narrator for this interactive fiction adventure. Obey ChatGPT Project custom instructions and retrieved project sources for world lore. Do not break character.");
        sections.Add(NarratorScalesResolver.BuildQuickReferenceBlock(bundle));

        var carryForward = ResolveCarryForwardSummary(bundle, handoff, freshNarrativeBootstrap);
        if (!string.IsNullOrWhiteSpace(carryForward))
            sections.Add("=== STORY SO FAR (local cache) ===\n\n" + carryForward.Trim());

        if (policy.IncludeState)
        {
            var stateBlock = BuildStateBlock(bundle);
            if (!string.IsNullOrWhiteSpace(stateBlock))
                sections.Add("=== STATE DELTA ===\n\n" + stateBlock);
        }

        AppendMemoryAndTranscript(bundle, sections, contextMode, attachment, handoff, freshNarrativeBootstrap, mode);

        var result = FinalizeContext(
            bundle, sections, [], settings, profile, mode, maxChars,
            labels: resolved.ResolvedLabels,
            packetTurnIndexOverride: packetTurnIndexOverride,
            handoff: handoff,
            freshNarrativeBootstrap: freshNarrativeBootstrap,
            budgetTrimmed: budgetResult.Trimmed);
#if DEBUG
        if (readiness.CanDelegateStaticContent)
            InjectionPolicyGuard.AssertThinDelegationPolicy(result.ContextText);
#endif
        return result;
    }

    private static PromptPacketContextResult BuildMinimalLocalContext(
        AdventureBundle bundle,
        string searchHint,
        AttachmentContextMode contextMode,
        AttachmentContext? attachment,
        int? packetTurnIndexOverride = null,
        PlayHandoffContext? handoff = null,
        bool freshNarrativeBootstrap = false)
    {
        var settings = bundle.Metadata.Settings;
        var policy = PlayInjectionPolicyService.Resolve(settings);
        var profile = PacketProfile.MinimalLocal;
        var mode = PacketProfileResolver.ToPacketMode(profile);
        var maxChars = PacketProfileResolver.ResolveMaxChars(profile, settings);

        var sections = new List<string>();
        if (policy.IncludeSourcesPointers)
            sections.Add(ContextPointerRenderer.BuildMinimalLocalSourcesBlock(settings.UseContextTags));
        sections.Add("You are the narrator for this interactive fiction adventure. Respond in vivid prose. Do not break character or mention being an AI. Link a ChatGPT Project and publish sources for full world lore retrieval.");
        sections.Add(NarratorScalesResolver.BuildQuickReferenceBlock(bundle));

        var scenarioSection = BuildScenarioSection(bundle, freshNarrativeBootstrap);
        if (!string.IsNullOrWhiteSpace(scenarioSection))
            sections.Add(scenarioSection);

        var carryForward = ResolveCarryForwardSummary(bundle, handoff, freshNarrativeBootstrap);
        if (!string.IsNullOrWhiteSpace(carryForward))
            sections.Add("=== STORY SO FAR (local cache) ===\n\n" + carryForward.Trim());

        if (policy.IncludeState)
        {
            var stateBlock = BuildStateBlock(bundle);
            if (!string.IsNullOrWhiteSpace(stateBlock))
                sections.Add("=== STATE DELTA ===\n\n" + stateBlock);
        }

        AppendMemoryAndTranscript(bundle, sections, contextMode, attachment, handoff, freshNarrativeBootstrap, mode);

        return FinalizeContext(
            bundle, sections, [], settings, profile, mode, maxChars,
            packetTurnIndexOverride: packetTurnIndexOverride,
            handoff: handoff,
            freshNarrativeBootstrap: freshNarrativeBootstrap);
    }

    private static PromptPacketContextResult BuildInlineFallbackContext(
        AdventureBundle bundle,
        string searchHint,
        AttachmentContextMode contextMode,
        AttachmentContext? attachment,
        int? packetTurnIndexOverride = null,
        PlayHandoffContext? handoff = null,
        bool freshNarrativeBootstrap = false)
    {
        var settings = bundle.Metadata.Settings;
        var policy = PlayInjectionPolicyService.Resolve(settings);
        var readiness = ProjectSourceInjectionService.Evaluate(bundle);
        var profile = PacketProfile.InlineFallback;
        var mode = PacketProfileResolver.ToPacketMode(profile);
        var maxChars = PacketProfileResolver.ResolveMaxChars(profile, settings);

        var signals = ContextSignalBuilder.Build(bundle, searchHint, contextMode, attachment);
        var resolved = ContextPointerResolver.Resolve(
            bundle, signals, fatFallback: true, freshNarrativeBootstrap);
        if (!policy.IncludeTriggeredCards)
            FilterTriggerPointers(resolved);
        var budgetResult = ContextBudgetAllocator.ApplyBudget(resolved.All, maxChars, fatFallback: true);

        var sections = new List<string>();
        if (policy.IncludeSourcesPointers || !readiness.CanDelegateStaticContent)
            sections.Add(ContextPointerRenderer.BuildSourcesV2Block(
                bundle, resolved, profile, readiness, settings.UseContextTags));
        sections.Add("You are the narrator for this interactive fiction adventure. Respond in vivid prose. Do not break character or mention being an AI.");
        sections.Add($"Perspective: {settings.Perspective}. Tense: {settings.Tense}. Detail: {settings.DetailLevel}. Tone: {settings.Tone ?? bundle.Scenario.Tone}. {NarratorScaleLabels.CombatDifficulty}: {settings.Difficulty}.");
        sections.Add(NarratorScalesResolver.BuildQuickReferenceBlock(bundle));

        var contractSections = InstructionContractService.BuildContractSections(bundle);
        if (!string.IsNullOrWhiteSpace(contractSections))
            sections.Add(contractSections);

        var scenarioSection = BuildScenarioSection(bundle, freshNarrativeBootstrap);
        if (!string.IsNullOrWhiteSpace(scenarioSection))
            sections.Add(scenarioSection);

        if (!string.IsNullOrWhiteSpace(bundle.Scenario.PlotEssentials))
            sections.Add("=== PLOT ESSENTIALS ===\n" + bundle.Scenario.PlotEssentials.Trim());

        if (!string.IsNullOrWhiteSpace(bundle.Scenario.WorldRules))
            sections.Add("=== WORLD RULES ===\n" + bundle.Scenario.WorldRules.Trim());

        if (!string.IsNullOrWhiteSpace(bundle.Scenario.AuthorsNote))
            sections.Add("=== AUTHOR'S NOTE (style only, not new facts) ===\n" + bundle.Scenario.AuthorsNote.Trim());

        var carryForward = ResolveCarryForwardSummary(bundle, handoff, freshNarrativeBootstrap);
        if (!string.IsNullOrWhiteSpace(carryForward))
            sections.Add("=== STORY SO FAR ===\n\n" + carryForward.Trim());

        if (policy.IncludeState)
        {
            var stateBlock = BuildStateBlock(bundle);
            if (!string.IsNullOrWhiteSpace(stateBlock))
                sections.Add("=== CURRENT STATE ===\n\n" + stateBlock);
        }

        var searchText = (searchHint + " " + bundle.Summary.RollingSummary).ToLowerInvariant();
        var maxCards = AttachmentSendPolicy.MaxLoreCards(contextMode, attachment);
        var triggered = policy.IncludeTriggeredCards && HasExportedLoreSources(bundle)
            ? TriggerStoryCards(bundle, searchText, maxCards)
            : [];
        if (triggered.Count > 0)
            sections.Add("=== RELEVANT LORE CARDS ===\n" + string.Join("\n\n", triggered));

        AppendMemoryAndTranscript(bundle, sections, contextMode, attachment, handoff, freshNarrativeBootstrap, mode);

        return FinalizeContext(
            bundle, sections, triggered, settings, profile, mode, maxChars,
            labels: resolved.ResolvedLabels,
            packetTurnIndexOverride: packetTurnIndexOverride,
            handoff: handoff,
            freshNarrativeBootstrap: freshNarrativeBootstrap,
            budgetTrimmed: budgetResult.Trimmed);
    }

    private static void FilterTriggerPointers(ContextResolveResult resolved)
    {
        resolved.All.RemoveAll(p => p.Source == PointerSource.Trigger);
        resolved.ThisTurn.RemoveAll(p => p.Source == PointerSource.Trigger);
        resolved.Baseline.RemoveAll(p => p.Source == PointerSource.Trigger);
    }

    private static void AppendMemoryAndTranscript(
        AdventureBundle bundle,
        List<string> sections,
        AttachmentContextMode contextMode,
        AttachmentContext? attachment,
        PlayHandoffContext? handoff = null,
        bool freshNarrativeBootstrap = false,
        PacketMode mode = PacketMode.Fat)
    {
        var policy = PlayInjectionPolicyService.Resolve(bundle.Metadata.Settings);
        if (policy.IncludePinnedMemory)
        {
            var pinnedMemory = bundle.Memory.Entries.Where(m => m.Pinned).Select(m => "- " + m.Text).ToList();
            if (pinnedMemory.Count > 0)
                sections.Add("=== PINNED MEMORY ===\n" + string.Join("\n", pinnedMemory));
        }

        AppendTranscriptSection(bundle, sections, contextMode, attachment, handoff, freshNarrativeBootstrap, mode);
    }

    private static void AppendTranscriptSection(
        AdventureBundle bundle,
        List<string> sections,
        AttachmentContextMode contextMode,
        AttachmentContext? attachment,
        PlayHandoffContext? handoff,
        bool freshNarrativeBootstrap = false,
        PacketMode mode = PacketMode.Fat)
    {
        if (freshNarrativeBootstrap)
            return;

        if (!PlayInjectionPolicyService.Resolve(bundle.Metadata.Settings).IncludeTranscript)
            return;

        if (AttachmentSendPolicy.ShouldOmitTranscript(contextMode, attachment))
            return;

        var transcript = handoff is not null
            ? BuildHandoffTranscriptSection(handoff)
            : BuildRecentTranscriptSection(bundle, mode);

        if (!string.IsNullOrWhiteSpace(transcript))
            sections.Add(transcript);
    }

    private static string? ResolveCarryForwardSummary(
        AdventureBundle bundle,
        PlayHandoffContext? handoff,
        bool freshNarrativeBootstrap = false)
    {
        if (freshNarrativeBootstrap)
            return null;

        if (!PlayInjectionPolicyService.Resolve(bundle.Metadata.Settings).IncludeSummary)
            return null;

        return handoff is not null
            ? handoff.CarryForwardSummary
            : bundle.Summary.RollingSummary;
    }

    private static string BuildPlayerSection(string playerInput) =>
        "=== PLAYER TURN ===\n" + playerInput.Trim();

    private static PromptPacketContextResult FinalizeContext(
        AdventureBundle bundle,
        List<string> sections,
        List<string> triggered,
        AdventureSettings settings,
        PacketProfile profile,
        PacketMode mode,
        int maxChars,
        List<string>? labels = null,
        int? packetTurnIndexOverride = null,
        PlayHandoffContext? handoff = null,
        bool freshNarrativeBootstrap = false,
        IReadOnlyList<TrimmedSection>? budgetTrimmed = null)
    {
        var contextText = AssembleContextText(bundle, sections, profile, packetTurnIndexOverride, handoff);
        var resolvedLabels = labels ?? triggered.Select(t => t.Split('\n')[0]).ToList();
        return new PromptPacketContextResult
        {
            ContextText = contextText,
            TriggeredCardNames = resolvedLabels,
            ResolvedSectionPointers = resolvedLabels,
            Mode = mode,
            Profile = profile,
            MaxChars = maxChars,
            UseContextTags = settings.UseContextTags,
            BudgetTrimmed = budgetTrimmed?.ToList() ?? [],
        };
    }

    private static string AssembleContextText(
        AdventureBundle bundle,
        List<string> sections,
        PacketProfile profile,
        int? packetTurnIndexOverride = null,
        PlayHandoffContext? handoff = null)
    {
        if (!bundle.Metadata.Settings.UseContextTags)
            return string.Join("\n\n", sections);

        var turnIndex = packetTurnIndexOverride ?? PlayTurnScopeService.GetNextPacketTurnIndex(bundle);
        var blocks = new List<string>
        {
            ContextTagFormat.WrapMeta(
                profile,
                turnIndex,
                continuation: handoff is not null,
                adventureTurn: handoff?.AdventureTurnOrdinal),
        };

        string? sources = null;
        string? instructions = null;
        string? summary = null;
        string? state = null;
        string? cards = null;
        string? memory = null;
        string? transcript = null;

        foreach (var section in sections)
        {
            if (section.TrimStart().StartsWith(ContextTagFormat.TagPrefix + "sources", StringComparison.Ordinal))
            {
                blocks.Add(section.Trim());
                continue;
            }

            switch (ClassifyTaggedSection(section))
            {
                case TaggedSectionKind.Sources:
                    sources = JoinTagParts(sources, FormatTaggedSectionBody(section));
                    break;
                case TaggedSectionKind.Summary:
                    summary = JoinTagParts(summary, FormatTaggedSectionBody(section));
                    break;
                case TaggedSectionKind.State:
                    state = JoinTagParts(state, FormatTaggedSectionBody(section));
                    break;
                case TaggedSectionKind.Cards:
                    cards = JoinTagParts(cards, FormatTaggedSectionBody(section));
                    break;
                case TaggedSectionKind.Memory:
                    memory = JoinTagParts(memory, FormatTaggedSectionBody(section));
                    break;
                case TaggedSectionKind.Transcript:
                    transcript = JoinTagParts(transcript, FormatTaggedSectionBody(section));
                    break;
                default:
                    instructions = JoinTagParts(instructions, FormatTaggedSectionBody(section));
                    break;
            }
        }

        AppendTagBlock(blocks, "sources", sources);
        AppendTagBlock(blocks, "instructions", instructions);
        AppendTagBlock(blocks, "summary", summary);
        AppendTagBlock(blocks, "state", state);
        AppendTagBlock(blocks, "cards", cards);
        AppendTagBlock(blocks, "memory", memory);
        AppendTagBlock(blocks, "transcript", transcript);

        return string.Join("\n\n", blocks.Where(b => !string.IsNullOrWhiteSpace(b)));
    }

    private enum TaggedSectionKind
    {
        Instructions,
        Sources,
        Summary,
        State,
        Cards,
        Memory,
        Transcript,
    }

    private static TaggedSectionKind ClassifyTaggedSection(string section)
    {
        if (section.StartsWith("=== PROJECT SOURCES", StringComparison.Ordinal))
            return TaggedSectionKind.Sources;

        if (section.StartsWith("=== STORY SO FAR", StringComparison.Ordinal))
            return TaggedSectionKind.Summary;

        if (section.StartsWith("=== CURRENT STATE ===", StringComparison.Ordinal)
            || section.StartsWith("=== STATE DELTA ===", StringComparison.Ordinal))
            return TaggedSectionKind.State;

        if (section.Contains("=== RELEVANT LORE CARDS ===", StringComparison.Ordinal)
            || section.Contains("=== TRIGGERED CARDS", StringComparison.Ordinal))
            return TaggedSectionKind.Cards;

        if (section.Contains("=== PINNED MEMORY ===", StringComparison.Ordinal))
            return TaggedSectionKind.Memory;

        if (section.StartsWith("=== RECENT TRANSCRIPT ===", StringComparison.Ordinal))
            return TaggedSectionKind.Transcript;

        return TaggedSectionKind.Instructions;
    }

    private static string? BuildHandoffTranscriptSection(PlayHandoffContext handoff)
    {
        if (!handoff.IncludeTranscript || handoff.TranscriptTurns.Count == 0)
            return null;

        var lines = handoff.TranscriptTurns
            .Select(t =>
            {
                var narrator = (t.NarratorText ?? "").Trim();
                if (PlayTurnScopeService.IsIncompleteNarratorCapture(narrator))
                    narrator = "";

                return $"Player: {t.PlayerText.Trim()}\nNarrator: {narrator}";
            })
            .Where(line => !string.IsNullOrWhiteSpace(line.Replace("Narrator:", "", StringComparison.Ordinal).Trim()));

        var body = string.Join("\n\n", lines);
        return string.IsNullOrWhiteSpace(body)
            ? null
            : "=== RECENT TRANSCRIPT ===\n\n" + body;
    }

    private static string? BuildRecentTranscriptSection(AdventureBundle bundle, PacketMode mode)
    {
        var maxTurns = PlayInjectionPolicyService.ResolveTranscriptMaxTurns(bundle.Metadata.Settings, mode);
        var policy = PlayInjectionPolicyService.Resolve(bundle.Metadata.Settings);

        var turns = PlayTurnScopeService.GetPacketContextTurns(bundle)
            .TakeLast(maxTurns)
            .ToList();

        if (turns.Count == 0)
            return null;

        var lines = turns
            .Select(t =>
            {
                var narrator = (t.NarratorText ?? "").Trim();
                if (PlayTurnScopeService.IsIncompleteNarratorCapture(narrator))
                    narrator = "";

                return $"Player: {t.PlayerText.Trim()}\nNarrator: {narrator}";
            })
            .Where(line => !string.IsNullOrWhiteSpace(line.Replace("Narrator:", "", StringComparison.Ordinal).Trim()));

        var body = string.Join("\n\n", lines);
        if (string.IsNullOrWhiteSpace(body))
            return null;

        if (policy.TranscriptMaxChars > 0 && body.Length > policy.TranscriptMaxChars)
            body = body[..policy.TranscriptMaxChars] + "…";

        return "=== RECENT TRANSCRIPT ===\n\n" + body;
    }

    private static string FormatTaggedSectionBody(string section)
    {
        if (TryStripLegacyHeader(section, out var body))
            return ContextTagFormat.NormalizeLineBreaks(body);

        return ContextTagFormat.NormalizeLineBreaks(section);
    }

    private static bool TryStripLegacyHeader(string section, out string body)
    {
        body = section;
        if (!section.StartsWith("=== ", StringComparison.Ordinal))
            return false;

        var close = section.IndexOf(" ===", 4, StringComparison.Ordinal);
        if (close < 0)
            return false;

        body = section[(close + 4)..].TrimStart('\r', '\n');
        return true;
    }

    private static string JoinTagParts(string? existing, string? addition)
    {
        if (string.IsNullOrWhiteSpace(addition))
            return existing ?? "";

        var trimmed = ContextTagFormat.NormalizeLineBreaks(addition);
        return string.IsNullOrWhiteSpace(existing)
            ? trimmed
            : ContextTagFormat.NormalizeLineBreaks(existing) + "\n\n" + trimmed;
    }

    private static void AppendTagBlock(List<string> blocks, string tagName, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        blocks.Add(ContextTagFormat.WrapBlock(tagName, content));
    }

    public static string Preview(AdventureBundle bundle, string playerInput) =>
        Build(bundle, playerInput).Text;

    private static string BuildScenarioSection(AdventureBundle bundle, bool freshNarrativeBootstrap = false)
    {
        var s = bundle.Scenario;
        var accepted = freshNarrativeBootstrap
            ? 0
            : bundle.Log.Turns.Count(t => t.Status == TurnStatus.Accepted);
        var includeFull = accepted == 0 || accepted <= 3;

        if (!includeFull && string.IsNullOrWhiteSpace(s.PlotEssentials))
            return "";

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.Setting))
            parts.Add($"Setting: {s.Setting.Trim()}");
        if (!string.IsNullOrWhiteSpace(s.PlayerRole))
            parts.Add($"Player role: {s.PlayerRole.Trim()}");
        if (!string.IsNullOrWhiteSpace(s.Genre))
            parts.Add($"Genre: {s.Genre.Trim()}");
        if (!string.IsNullOrWhiteSpace(s.OpeningSituation))
            parts.Add($"Opening: {s.OpeningSituation.Trim()}");
        if (!string.IsNullOrWhiteSpace(s.MajorConflicts))
            parts.Add($"Conflicts: {s.MajorConflicts.Trim()}");
        if (!string.IsNullOrWhiteSpace(s.StartingConstraints))
            parts.Add($"Constraints: {s.StartingConstraints.Trim()}");

        if (parts.Count == 0)
            return "";

        return "=== SCENARIO ===\n" + string.Join("\n", parts);
    }

    private static string BuildStateBlock(AdventureBundle bundle)
    {
        var s = bundle.State;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.CurrentLocation)) parts.Add($"Location: {s.CurrentLocation}");
        if (!string.IsNullOrWhiteSpace(s.PlayerCondition)) parts.Add($"Player: {s.PlayerCondition}");
        if (!string.IsNullOrWhiteSpace(s.ActiveThreats)) parts.Add($"Threats: {s.ActiveThreats}");
        if (!string.IsNullOrWhiteSpace(s.OpenObjectives)) parts.Add($"Objectives: {s.OpenObjectives}");
        if (!string.IsNullOrWhiteSpace(s.UnresolvedMysteries)) parts.Add($"Mysteries: {s.UnresolvedMysteries}");
        if (!string.IsNullOrWhiteSpace(s.RecentConsequences)) parts.Add($"Consequences: {s.RecentConsequences}");

        var scene = s.Scene;
        if (!string.IsNullOrWhiteSpace(scene.Location) || !string.IsNullOrWhiteSpace(scene.Participants))
        {
            parts.Add($"Scene — {scene.Location}; {scene.Participants}; mood: {scene.Atmosphere}");
        }

        if (!string.IsNullOrWhiteSpace(s.Time.InWorldTime))
            parts.Add($"Time: {s.Time.InWorldTime}");

        return string.Join("\n", parts);
    }

    private static bool HasExportedLoreSources(AdventureBundle bundle)
    {
        var dir = ProjectSourceExportService.SourcesDirectory(bundle);
        if (!Directory.Exists(dir))
            return false;

        return SectionSchema.CoreLoreFiles.Any(file =>
            File.Exists(Path.Combine(dir, file)));
    }

    private static List<string> TriggerStoryCards(AdventureBundle bundle, string searchText, int maxCards = int.MaxValue)
    {
        var result = new List<string>();
        foreach (var card in bundle.Cards.Cards.Where(c => c.Enabled))
        {
            if (result.Count >= maxCards)
                break;

            if (card.Triggers.Count == 0)
                continue;

            var hit = card.Triggers.Any(t =>
                !string.IsNullOrWhiteSpace(t) &&
                searchText.Contains(t.Trim().ToLowerInvariant(), StringComparison.Ordinal));

            if (hit)
                result.Add($"{card.Name} ({card.Type})\n{card.Content}");
        }

        return result;
    }

    private static string TrimPacket(string text, int maxChars)
    {
        if (text.Length <= maxChars)
            return text;

        return text[..maxChars] + "\n\n[... trimmed for length ...]";
    }

    internal static string ComputeHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes)[..16];
    }
}
