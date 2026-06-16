using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

internal static class AdventureDesignService
{
    public static readonly IReadOnlyList<AdventureDesignStep> OrderedSteps =
    [
        AdventureDesignStep.Setup,
        AdventureDesignStep.Concept,
        AdventureDesignStep.World,
        AdventureDesignStep.Plot,
        AdventureDesignStep.Cast,
        AdventureDesignStep.Lexicon,
        AdventureDesignStep.Sources,
        AdventureDesignStep.Instructions,
        AdventureDesignStep.Review,
    ];

    public static AdventureDesignWorkspace CreateInitialWorkspace() =>
        new()
        {
            CurrentStep = AdventureDesignStep.Setup,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    public static AdventureBundle CreateDesigningAdventure(string title)
    {
        var bundle = AdventureStore.CreateNew(
            string.IsNullOrWhiteSpace(title) ? "Untitled adventure" : title.Trim(),
            designing: true);
        SyncSetupFromMetadata(bundle);
        AdventureStore.Save(bundle);
        return bundle;
    }

    public static void EnsureWorkspace(AdventureBundle bundle)
    {
        bundle.DesignWorkspace ??= CreateInitialWorkspace();
        bundle.DesignWorkspace.Steps ??=
            new Dictionary<string, DesignStepState>(StringComparer.OrdinalIgnoreCase);
        bundle.DesignWorkspace.SourceFilesPrompted ??=
            new Dictionary<string, DesignSourceFilePromptState>(StringComparer.OrdinalIgnoreCase);

        if (bundle.DesignWorkspace.Steps.Count == 0 && bundle.Metadata.Status == AdventureStatus.Designing)
            bundle.DesignWorkspace = CreateInitialWorkspace();
    }

    private static DesignStepState NormalizeStepState(DesignStepState state)
    {
        state.Fields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        state.ChatMessages ??= [];
        state.PendingProposals ??= [];
        return state;
    }

    public static DesignStepState GetOrCreateStep(AdventureBundle bundle, AdventureDesignStep step)
    {
        EnsureWorkspace(bundle);
        var key = step.ToString();
        if (!bundle.DesignWorkspace.Steps.TryGetValue(key, out var state))
        {
            state = new DesignStepState();
            bundle.DesignWorkspace.Steps[key] = state;
        }

        return NormalizeStepState(state);
    }

    public static string? GetField(AdventureBundle bundle, AdventureDesignStep step, string fieldKey)
    {
        var state = GetOrCreateStep(bundle, step);
        return state.Fields.TryGetValue(fieldKey, out var value) ? value : null;
    }

    public static void SetField(AdventureBundle bundle, AdventureDesignStep step, string fieldKey, string value)
    {
        var state = GetOrCreateStep(bundle, step);
        state.Fields[fieldKey] = value ?? "";
        Touch(bundle);
    }

    public static void SetFreeform(AdventureBundle bundle, AdventureDesignStep step, string text)
    {
        GetOrCreateStep(bundle, step).FreeformDraft = text ?? "";
        Touch(bundle);
    }

    public static string GetFreeform(AdventureBundle bundle, AdventureDesignStep step) =>
        GetOrCreateStep(bundle, step).FreeformDraft;

    public static void GoToStep(AdventureBundle bundle, AdventureDesignStep step)
    {
        EnsureWorkspace(bundle);
        bundle.DesignWorkspace.CurrentStep = step;
        Touch(bundle);
    }

    public static int StepIndex(AdventureDesignStep step)
    {
        for (var i = 0; i < OrderedSteps.Count; i++)
        {
            if (OrderedSteps[i] == step)
                return i;
        }

        return -1;
    }

    public static bool TryAdvanceStep(AdventureBundle bundle, out AdventureDesignStep next)
    {
        var idx = StepIndex(bundle.DesignWorkspace.CurrentStep);
        if (idx < 0 || idx >= OrderedSteps.Count - 1)
        {
            next = bundle.DesignWorkspace.CurrentStep;
            return false;
        }

        next = OrderedSteps[idx + 1];
        bundle.DesignWorkspace.CurrentStep = next;
        Touch(bundle);
        return true;
    }

    public static bool TryRetreatStep(AdventureBundle bundle, out AdventureDesignStep prev)
    {
        var idx = StepIndex(bundle.DesignWorkspace.CurrentStep);
        if (idx <= 0)
        {
            prev = bundle.DesignWorkspace.CurrentStep;
            return false;
        }

        prev = OrderedSteps[idx - 1];
        bundle.DesignWorkspace.CurrentStep = prev;
        Touch(bundle);
        return true;
    }

    public static void MarkStepAccepted(AdventureBundle bundle, AdventureDesignStep step)
    {
        GetOrCreateStep(bundle, step).AcceptedAt = DateTimeOffset.UtcNow;
        Touch(bundle);
    }

    public static void AddChatMessage(
        AdventureBundle bundle,
        AdventureDesignStep step,
        string role,
        string text)
    {
        var state = GetOrCreateStep(bundle, step);
        state.ChatMessages.Add(new DesignChatMessage
        {
            Role = role,
            Text = text.Trim(),
            Step = step,
            Timestamp = DateTimeOffset.UtcNow,
        });
        Touch(bundle);
    }

    public static void AddProposals(AdventureBundle bundle, AdventureDesignStep step, IEnumerable<DesignStepProposal> proposals)
    {
        var state = GetOrCreateStep(bundle, step);
        foreach (var proposal in proposals)
        {
            proposal.Status = DesignProposalStatus.Pending;
            if (state.Fields.TryGetValue(proposal.FieldKey, out var current))
                proposal.CurrentValue = current;
            state.PendingProposals.Add(proposal);
        }

        Touch(bundle);
    }

    public static int AcceptAllPendingProposals(AdventureBundle bundle, AdventureDesignStep step)
    {
        var state = GetOrCreateStep(bundle, step);
        var count = 0;
        foreach (var proposal in state.PendingProposals.Where(p => p.Status == DesignProposalStatus.Pending))
        {
            state.Fields[proposal.FieldKey] = proposal.ProposedValue;
            proposal.Status = DesignProposalStatus.Accepted;
            count++;
        }

        if (count > 0)
            Touch(bundle);

        return count;
    }

    public static void RejectAllPendingProposals(AdventureBundle bundle, AdventureDesignStep step)
    {
        var state = GetOrCreateStep(bundle, step);
        foreach (var proposal in state.PendingProposals.Where(p => p.Status == DesignProposalStatus.Pending))
            proposal.Status = DesignProposalStatus.Rejected;
        Touch(bundle);
    }

    public static void SyncSetupFromMetadata(AdventureBundle bundle)
    {
        SetField(bundle, AdventureDesignStep.Setup, "title", bundle.Metadata.Title);
        if (!string.IsNullOrWhiteSpace(bundle.Metadata.Genre))
            SetField(bundle, AdventureDesignStep.Setup, "genreHook", bundle.Metadata.Genre);
    }

    public static void ApplySetupToMetadata(AdventureBundle bundle)
    {
        var title = GetField(bundle, AdventureDesignStep.Setup, "title");
        if (!string.IsNullOrWhiteSpace(title))
            bundle.Metadata.Title = title.Trim();

        var genre = GetField(bundle, AdventureDesignStep.Setup, "genreHook");
        if (!string.IsNullOrWhiteSpace(genre))
            bundle.Metadata.Genre = genre.Trim();
    }

    public static void HydrateFromScenario(AdventureBundle bundle)
    {
        if (bundle.Metadata.Status != AdventureStatus.Designing)
            return;

        var s = bundle.Scenario;
        SetField(bundle, AdventureDesignStep.Concept, "setting", s.Setting);
        SetField(bundle, AdventureDesignStep.Concept, "playerRole", s.PlayerRole);
        SetField(bundle, AdventureDesignStep.Concept, "genre", s.Genre);
        SetField(bundle, AdventureDesignStep.Concept, "tone", s.Tone);
        SetField(bundle, AdventureDesignStep.Concept, "openingSituation", s.OpeningSituation);
        SetField(bundle, AdventureDesignStep.World, "worldRules", s.WorldRules);
        SetField(bundle, AdventureDesignStep.World, "startingConstraints", s.StartingConstraints);
        SetField(bundle, AdventureDesignStep.Plot, "plotEssentials", s.PlotEssentials);
        SetField(bundle, AdventureDesignStep.Plot, "majorConflicts", s.MajorConflicts);
        InstructionContractService.HydrateDesignInstructionFields(bundle);
        SetField(bundle, AdventureDesignStep.Lexicon, "lexiconRules", s.LexiconRules);
        SetField(bundle, AdventureDesignStep.Lexicon, "lexiconPools", s.LexiconPools);
        SetField(bundle, AdventureDesignStep.Lexicon, "lexiconAvoid", s.LexiconAvoid);
    }

    public static string BuildStepSeedPrompt(AdventureBundle bundle, AdventureDesignStep step)
    {
        var title = bundle.Metadata.Title;
        return step switch
        {
            AdventureDesignStep.Setup =>
                $"""
                === ADVENTURE DESIGN — SETUP ===
                Adventure: {title}
                Help the author refine the adventure title, genre hook, and high-level pitch.
                Ask clarifying questions. Do not write full source files yet.
                """,
            AdventureDesignStep.Concept =>
                $"""
                === ADVENTURE DESIGN — CONCEPT ===
                Adventure: {title}
                Develop the core concept: setting, player role, genre, tone, and opening situation.
                Current draft:
                {BuildStepDraftSummary(bundle, step)}
                """,
            AdventureDesignStep.World =>
                $"""
                === ADVENTURE DESIGN — WORLD ===
                Adventure: {title}
                Develop world rules and starting constraints.
                Current draft:
                {BuildStepDraftSummary(bundle, step)}
                """,
            AdventureDesignStep.Plot =>
                $"""
                === ADVENTURE DESIGN — PLOT ===
                Adventure: {title}
                Develop plot essentials and major conflicts.
                Current draft:
                {BuildStepDraftSummary(bundle, step)}
                """,
            AdventureDesignStep.Cast =>
                $"""
                === ADVENTURE DESIGN — CAST ===
                Adventure: {title}
                Develop initial characters: names, roles, descriptions, relationships to the player.
                Current draft:
                {BuildStepDraftSummary(bundle, step)}
                """,
            AdventureDesignStep.Lexicon =>
                $"""
                === ADVENTURE DESIGN — LEXICON ===
                Adventure: {title}
                Develop naming rules, tone consistency, anti-repetition guidance, and setting-appropriate name pools
                for people, places, groups, and realms. The wrapper will auto-maintain a registry of names already in use.
                Current draft:
                {BuildStepDraftSummary(bundle, step)}
                """,
            AdventureDesignStep.Sources =>
                $"""
                === ADVENTURE DESIGN — SOURCES ===
                Adventure: {title}
                Outline markdown source files (scenario, world, plot, cast, lexicon) for ChatGPT Project RAG.
                Current draft:
                {BuildStepDraftSummary(bundle, step)}
                """,
            AdventureDesignStep.Instructions =>
                $"""
                === ADVENTURE DESIGN — INSTRUCTIONS ===
                Adventure: {title}
                Develop author style notes and narrator contract boundaries (perspective, tone, content limits).
                Current draft:
                {BuildStepDraftSummary(bundle, step)}
                """,
            _ =>
                $"""
                === ADVENTURE DESIGN ===
                Adventure: {title}
                Step: {step}
                """,
        };
    }

    public static string BuildStepDraftSummary(AdventureBundle bundle, AdventureDesignStep step)
    {
        var state = GetOrCreateStep(bundle, step);
        var lines = new List<string>();
        foreach (var field in GetFieldDefinitions(step))
        {
            var value = state.Fields.TryGetValue(field.Key, out var v) ? v : "";
            if (!string.IsNullOrWhiteSpace(value))
                lines.Add($"{field.Label}: {value}");
        }

        if (!string.IsNullOrWhiteSpace(state.FreeformDraft))
            lines.Add($"Notes:\n{state.FreeformDraft}");

        return lines.Count == 0 ? "(empty)" : string.Join(Environment.NewLine, lines);
    }

    public static string BuildRecentChatExcerpt(AdventureBundle bundle, AdventureDesignStep step, int maxMessages = 12)
    {
        var messages = GetOrCreateStep(bundle, step).ChatMessages
            .OrderBy(m => m.Timestamp)
            .TakeLast(maxMessages);

        return string.Join(
            Environment.NewLine + Environment.NewLine,
            messages.Select(m => $"[{m.Role}] {m.Text}"));
    }

    public static IReadOnlyList<DesignFieldDefinition> GetFieldDefinitions(AdventureDesignStep step) =>
        step switch
        {
            AdventureDesignStep.Setup =>
            [
                new("title", "Title"),
                new("genreHook", "Genre hook"),
                new("pitch", "One-line pitch"),
            ],
            AdventureDesignStep.Concept =>
            [
                new("setting", "Setting"),
                new("playerRole", "Player role"),
                new("genre", "Genre"),
                new("tone", "Tone"),
                new("openingSituation", "Opening situation"),
            ],
            AdventureDesignStep.World =>
            [
                new("worldRules", "World rules"),
                new("startingConstraints", "Starting constraints"),
            ],
            AdventureDesignStep.Plot =>
            [
                new("plotEssentials", "Plot essentials"),
                new("majorConflicts", "Major conflicts"),
            ],
            AdventureDesignStep.Cast =>
            [
                new("castNotes", "Cast notes"),
            ],
            AdventureDesignStep.Lexicon =>
            [
                new("lexiconRules", "Naming & tone rules"),
                new("lexiconPools", "Name pools (people, places, realms)"),
                new("lexiconAvoid", "Avoid (overused names & phrases)"),
            ],
            AdventureDesignStep.Sources =>
            [
                new("sourceOutline", "Source outline"),
            ],
            AdventureDesignStep.Instructions =>
            [
                new("authorsNote", "Author's note (style only)"),
                new(InstructionContractService.GlobalBoundariesFieldKey, "Global content boundaries (one per line)"),
                new(InstructionContractService.CharacterPortrayalFieldKey, "Character portrayal rules (Subject: rule, one per line)"),
                new(InstructionContractService.InstructionAddendumFieldKey, "Instruction addendum (optional)"),
            ],
            AdventureDesignStep.Review => [],
            _ => [],
        };

    public static string GetStepDisplayName(AdventureDesignStep step) => step switch
    {
        AdventureDesignStep.Setup => "Setup",
        AdventureDesignStep.Concept => "Concept",
        AdventureDesignStep.World => "World",
        AdventureDesignStep.Plot => "Plot",
        AdventureDesignStep.Cast => "Cast",
        AdventureDesignStep.Lexicon => "Lexicon",
        AdventureDesignStep.Sources => "Sources",
        AdventureDesignStep.Instructions => "Instructions",
        AdventureDesignStep.Review => "Review",
        _ => step.ToString(),
    };

    public static void ImportDraftFrameworkMarkdown(AdventureBundle bundle, string markdown)
    {
        SetFreeform(bundle, AdventureDesignStep.Sources, markdown);
        var state = GetOrCreateStep(bundle, AdventureDesignStep.Sources);
        if (!state.Fields.ContainsKey("sourceOutline") || string.IsNullOrWhiteSpace(state.Fields["sourceOutline"]))
            state.Fields["sourceOutline"] = "Imported from framework draft — review and split into source files on finalize.";
        Touch(bundle);
    }

    public const int MaxSourcePromptAssistantExcerptLength = 4000;

    public static void MarkSourceFilePromptSent(
        AdventureBundle bundle,
        string relativePath,
        string? assistantExcerpt = null)
    {
        EnsureWorkspace(bundle);
        var key = relativePath.Trim();
        string? excerpt = null;
        if (!string.IsNullOrWhiteSpace(assistantExcerpt))
        {
            excerpt = assistantExcerpt.Trim();
            if (excerpt.Length > MaxSourcePromptAssistantExcerptLength)
                excerpt = excerpt[..MaxSourcePromptAssistantExcerptLength];
        }

        bundle.DesignWorkspace.SourceFilesPrompted[key] = new DesignSourceFilePromptState
        {
            SentAt = DateTimeOffset.UtcNow,
            AssistantExcerpt = excerpt,
        };
        Touch(bundle);
    }

    public static bool IsSourceFilePromptSent(AdventureBundle bundle, string relativePath)
    {
        EnsureWorkspace(bundle);
        return bundle.DesignWorkspace.SourceFilesPrompted.ContainsKey(relativePath.Trim());
    }

    public static IReadOnlyList<string> GetSentSourceFiles(AdventureBundle bundle)
    {
        EnsureWorkspace(bundle);
        return AdventureDesignSourcePromptService.PromptPipelineOrder
            .Where(path => bundle.DesignWorkspace.SourceFilesPrompted.ContainsKey(path))
            .ToList();
    }

    public static void ClearSourceFilePromptState(AdventureBundle bundle)
    {
        EnsureWorkspace(bundle);
        bundle.DesignWorkspace.SourceFilesPrompted.Clear();
        Touch(bundle);
    }

    private static void Touch(AdventureBundle bundle) =>
        bundle.DesignWorkspace.UpdatedAt = DateTimeOffset.UtcNow;
}

internal readonly record struct DesignFieldDefinition(string Key, string Label);
