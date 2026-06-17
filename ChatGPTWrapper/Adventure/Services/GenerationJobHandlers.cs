using System.Text;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class GenerationJobContext
{
    public TurnRecord? Turn { get; init; }

    public UtilityTranscriptScope? Scope { get; init; }

    public Guid? CardId { get; init; }

    public Guid? EntityId { get; init; }

    public string? EntityKind { get; init; }

    public bool ForceRotate { get; init; }

    public string? UserPrompt { get; init; }

    public bool ProcessTurnIncludeMemories { get; init; } = true;

    public bool ProcessTurnIncludeEntities { get; init; } = true;

    public bool ProcessTurnIncludeSummary { get; init; }

    public string? StoryContextBlock { get; set; }

    public bool StoryContextHasTranscript { get; set; }

    public bool OmitRedundantJobTurnSlices { get; set; }

    public bool StoryContextIncludesSummary { get; set; }

    public bool StoryContextIncludesState { get; set; }

    public bool SuppressInlineGuide { get; set; }

    public AdventureDesignStep? DesignStep { get; init; }
}

internal sealed class GenerationJobResult
{
    public bool Success { get; init; }

    public int ProposalCount { get; init; }

    public string? Error { get; init; }

    public string? SkippedReason { get; init; }

    public string? DisplayText { get; init; }

    public bool Rotated { get; init; }

    public StoryContextSourceUsed? StoryContextSource { get; init; }

    public int StoryContextTurnPairs { get; init; }

    public int StoryContextCharCount { get; init; }

    public string? StoryContextStatusHint { get; init; }

    public List<Guid> ProposalIds { get; init; } = [];

    public string? DraftSourcePath { get; init; }

    public List<DesignStepProposal> DesignProposals { get; init; } = [];

    public AdventureDesignStep? DesignStep { get; init; }
}

internal static class GenerationJobHandlers
{
    public static string BuildSeedPrompt(AdventureBundle bundle, string jobId, int sequence)
    {
        var titleLine = GenerationUtilitySessionService.BuildUtilityTitleLine(bundle, jobId, sequence);
        var seedVersion = GenerationUtilitySessionService.GetSeedVersion(bundle, jobId);
        var body = GenerationJobGuideService.ResolveInstructionBody(bundle, jobId);
        if (string.IsNullOrWhiteSpace(body))
            body = "You run structured generation jobs. Follow each job packet exactly.";

        var playThreadLine = BuildPlayThreadLine(bundle);

        return $"""
            {titleLine}
            Adventure ID: {bundle.Metadata.Id}
            Job: {jobId} · Seed v{seedVersion}
            {playThreadLine}
            {body}
            """;
    }

    public static string BuildJobPrompt(AdventureBundle bundle, string jobId, GenerationJobContext context)
    {
        var core = jobId switch
        {
            GenerationJobId.ProcessTurn =>
                BuildProcessTurnPrompt(bundle, context),
            GenerationJobId.ExtractEntities when context.Scope is { } extractScope =>
                EntityExtractionService.BuildScopedExtractionPrompt(bundle, extractScope),
            GenerationJobId.ExtractEntities when context.Turn is { } turn =>
                EntityExtractionService.BuildExtractionPrompt(bundle, turn),
            GenerationJobId.ExpandEntity when context.EntityId is { } entityId =>
                EntityExtractionService.BuildExpandEntityPrompt(bundle, context.EntityKind ?? "Characters", entityId),
            GenerationJobId.ProposeMemories when context.Scope is { } memScope =>
                BuildScopedMemoryProposalPrompt(bundle, memScope, context.OmitRedundantJobTurnSlices && context.StoryContextHasTranscript),
            GenerationJobId.ProposeMemories when context.Turn is { } memTurn =>
                BuildMemoryProposalPrompt(bundle, memTurn, context.OmitRedundantJobTurnSlices && context.StoryContextHasTranscript),
            GenerationJobId.UpdateSummary =>
                RecapService.BuildSummaryUpdatePrompt(bundle, context.OmitRedundantJobTurnSlices && context.StoryContextHasTranscript),
            GenerationJobId.BootstrapLore =>
                BuildBootstrapLorePrompt(bundle),
            GenerationJobId.BootstrapSections =>
                BuildBootstrapSectionsPrompt(bundle),
            GenerationJobId.ExpandStoryCard when context.CardId is { } cardId =>
                BuildExpandCardPrompt(bundle, cardId),
            GenerationJobId.ExpandSection when context.EntityId is { } sectionEntityId =>
                BuildExpandSectionPrompt(bundle, sectionEntityId),
            GenerationJobId.ContinuityCheck =>
                BuildContinuityCheckPrompt(
                    bundle,
                    context.OmitRedundantJobTurnSlices && context.StoryContextHasTranscript,
                    context.StoryContextIncludesSummary,
                    context.StoryContextIncludesState),
            GenerationJobId.ProposeSourceEdits =>
                SourceEditService.BuildSourceEditPrompt(bundle, context.UserPrompt ?? ""),
            GenerationJobId.ProposeJsonImport =>
                SourceJsonImportService.BuildImportPrompt(bundle),
            GenerationJobId.DraftFramework =>
                """
                === ADVENTURE DRAFTING ===
                Help develop framework elements for this adventure: scenario structure, source file outlines,
                project instructions, and world rules. Propose concrete markdown sections the user can publish.
                """,
            GenerationJobId.DesignExtractStep when context.DesignStep is { } designStep =>
                AdventureDesignExtractionService.BuildExtractPrompt(bundle, designStep),
            GenerationJobId.DesignAdventure when !string.IsNullOrWhiteSpace(context.UserPrompt) =>
                context.UserPrompt!,
            GenerationJobId.SynthesizeSource when !string.IsNullOrWhiteSpace(context.UserPrompt) =>
                context.UserPrompt!,
            _ => throw new InvalidOperationException($"Missing context for job {jobId}"),
        };

        var withPlayThread = AppendPlayThreadLine(bundle, core);
        var withStory = AppendStoryContextBlock(withPlayThread, context.StoryContextBlock);
        var withOverrides = AppendUtilityJobOverrides(bundle, jobId, withStory);
        return context.SuppressInlineGuide
            ? withOverrides
            : AppendInlineGuideIfNeeded(bundle, jobId, withOverrides);
    }

    private static string AppendUtilityJobOverrides(AdventureBundle bundle, string jobId, string prompt)
    {
        var utilityId = GetUtilityJobId(jobId);
        if (!bundle.Metadata.Settings.UtilityJobOverrides.TryGetValue(utilityId, out var overrides))
            return prompt;

        var lines = new List<string>();
        if (!string.Equals(overrides.ResponseLength, "normal", StringComparison.OrdinalIgnoreCase))
            lines.Add($"Response length: {overrides.ResponseLength}");
        if (!string.Equals(overrides.ResponseDetail, "standard", StringComparison.OrdinalIgnoreCase))
            lines.Add($"Response detail: {overrides.ResponseDetail}");

        if (lines.Count == 0)
            return prompt;

        return $"""
            {prompt}

            === JOB OVERRIDES ===
            {string.Join(Environment.NewLine, lines)}
            """;
    }

    private static string AppendStoryContextBlock(string core, string? block)
    {
        if (string.IsNullOrWhiteSpace(block))
            return core;

        return $"""
            {block.Trim()}

            {core}
            """;
    }

    private static string BuildPlayThreadLine(AdventureBundle bundle)
    {
        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId))
            return "";

        return $"Play thread: {bundle.Metadata.LinkedConversationId}";
    }

    private static string AppendPlayThreadLine(AdventureBundle bundle, string core)
    {
        var line = BuildPlayThreadLine(bundle);
        if (string.IsNullOrWhiteSpace(line))
            return core;

        return $"""
            {line}

            {core}
            """;
    }

    private static string AppendInlineGuideIfNeeded(AdventureBundle bundle, string jobId, string core)
    {
        var guide = GenerationJobGuideService.ResolveInstructionBody(bundle, jobId);
        if (string.IsNullOrWhiteSpace(guide))
            return core;

        return $"""
            {core}

            === JOB GUIDE (inline) ===
            {guide.Trim()}
            """;
    }

    public static bool IsCaptureFailureError(string? error) =>
        string.Equals(error, "empty_response", StringComparison.OrdinalIgnoreCase)
        || string.Equals(error, "capture_timeout", StringComparison.OrdinalIgnoreCase)
        || string.Equals(error, "utility_page_not_ready", StringComparison.OrdinalIgnoreCase)
        || string.Equals(error, "capture_no_assistant", StringComparison.OrdinalIgnoreCase)
        || string.Equals(error, "capture_premature", StringComparison.OrdinalIgnoreCase)
        || string.Equals(error, "conversation_mismatch", StringComparison.OrdinalIgnoreCase)
        || string.Equals(error, "submit_not_observed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(error, "bridge_not_ready", StringComparison.OrdinalIgnoreCase)
        || string.Equals(error, "conversation_unregistered", StringComparison.OrdinalIgnoreCase)
        || string.Equals(error, "rate_limited", StringComparison.OrdinalIgnoreCase);

    public static GenerationJobResult ApplyResponse(
        AdventureBundle bundle,
        string jobId,
        string? responseText,
        string? captureError = null,
        GenerationJobContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return new GenerationJobResult
            {
                Success = true,
                ProposalCount = 0,
                Error = string.IsNullOrWhiteSpace(captureError) ? "empty_response" : captureError,
            };
        }

        responseText = NormalizeCapturedJobResponse(responseText);

        return jobId switch
        {
            GenerationJobId.ProcessTurn => ApplyProcessTurn(bundle, responseText),
            GenerationJobId.ExtractEntities or GenerationJobId.ExpandEntity => ApplyExtractEntities(bundle, responseText),
            GenerationJobId.ProposeMemories => ApplyProposeMemories(bundle, responseText),
            GenerationJobId.UpdateSummary => ApplyUpdateSummary(bundle, responseText),
            GenerationJobId.BootstrapLore => ApplyBootstrapLore(bundle, responseText),
            GenerationJobId.BootstrapSections => ApplySectionBootstrap(bundle, responseText),
            GenerationJobId.ExpandStoryCard => ApplyExpandCard(bundle, responseText),
            GenerationJobId.ExpandSection => ApplySectionBootstrap(bundle, responseText),
            GenerationJobId.ContinuityCheck => ApplyContinuityCheck(bundle, responseText),
            GenerationJobId.ProposeSourceEdits => ApplyProposeSourceEdits(bundle, responseText),
            GenerationJobId.ProposeJsonImport => ApplyProposeJsonImport(bundle, responseText),
            GenerationJobId.DraftFramework =>
                ApplyDraftFramework(bundle, responseText),
            GenerationJobId.DesignExtractStep =>
                ApplyDesignExtract(bundle, responseText, context?.DesignStep),
            GenerationJobId.DesignAdventure =>
                new GenerationJobResult
                {
                    Success = true,
                    ProposalCount = 0,
                    DisplayText = responseText,
                },
            GenerationJobId.SynthesizeSource =>
                new GenerationJobResult
                {
                    Success = true,
                    ProposalCount = 0,
                    DisplayText = responseText,
                },
            _ => new GenerationJobResult { Success = false, Error = "unknown_job" },
        };
    }

    public static string GetUtilityJobId(string jobId) => jobId switch
    {
        GenerationJobId.ExpandStoryCard => GenerationJobId.BootstrapLore,
        GenerationJobId.ExpandSection => GenerationJobId.BootstrapSections,
        GenerationJobId.ExpandEntity => GenerationJobId.ExtractEntities,
        GenerationJobId.DesignExtractStep => GenerationJobId.DesignAdventure,
        _ => jobId,
    };

    public static bool ExpectsJsonObjectResponse(string jobId) => jobId switch
    {
        GenerationJobId.ProcessTurn => true,
        GenerationJobId.DesignExtractStep => true,
        GenerationJobId.ProposeJsonImport => true,
        _ => false,
    };

    public static bool ExpectsJsonArrayResponse(string jobId) => jobId switch
    {
        GenerationJobId.ExtractEntities
            or GenerationJobId.ExpandEntity
            or GenerationJobId.ProposeMemories
            or GenerationJobId.BootstrapLore
            or GenerationJobId.ExpandStoryCard
            or GenerationJobId.BootstrapSections
            or GenerationJobId.ExpandSection
            or GenerationJobId.ProposeSourceEdits => true,
        _ => false,
    };

    public static bool ExpectsPlainTextResponse(string jobId) =>
        string.Equals(jobId, GenerationJobId.UpdateSummary, StringComparison.Ordinal);

    public static bool IsParseableJobResponse(string jobId, string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return false;

        responseText = NormalizeCapturedJobResponse(responseText);

        if (ExpectsPlainTextResponse(jobId))
            return IsValidPlainTextJobResponse(responseText);

        if (ExpectsJsonObjectResponse(jobId))
        {
            if (string.Equals(jobId, GenerationJobId.ProposeJsonImport, StringComparison.Ordinal))
                return SourceJsonImportService.IsParseableResponse(responseText);
            return !string.IsNullOrWhiteSpace(EntityExtractionService.TryNormalizeJsonObjectResponse(responseText));
        }

        if (!ExpectsJsonArrayResponse(jobId))
            return !string.IsNullOrWhiteSpace(StripPlainText(responseText));

        var normalized = EntityExtractionService.TryNormalizeJsonArrayResponse(responseText);
        return EntityExtractionService.IsValidJsonArray(normalized);
    }

    /// <summary>
    /// True when the response is structurally valid and ready to apply (including intentional empty arrays).
    /// </summary>
    public static bool IsSettledJobResponse(string jobId, string? responseText, bool streamComplete)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return false;

        responseText = NormalizeCapturedJobResponse(responseText);
        if (!IsParseableJobResponse(jobId, responseText))
            return false;

        if (ExpectsPlainTextResponse(jobId))
            return IsValidPlainTextJobResponse(responseText);

        if (ExpectsJsonObjectResponse(jobId))
        {
            if (string.Equals(jobId, GenerationJobId.ProposeJsonImport, StringComparison.Ordinal))
                return SourceJsonImportService.IsSettledResponse(responseText, streamComplete);
            return IsSettledProcessTurnResponse(responseText, streamComplete);
        }

        if (HasActionableJobProposals(jobId, responseText))
            return true;

        return streamComplete && IsEmptyJsonArrayResponse(responseText);
    }

    public static bool HasActionableJobProposals(string jobId, string responseText)
    {
        if (!ExpectsJsonArrayResponse(jobId))
            return false;

        var normalized = EntityExtractionService.TryNormalizeJsonArrayResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var element in JsonElementParsing.EnumerateObjectElements(doc.RootElement))
            {
                if (JobProposalHasRequiredFields(jobId, element))
                    return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    public static bool IsEmptyJsonArrayResponse(string? responseText)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonArrayResponse(responseText ?? "");
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                   && doc.RootElement.GetArrayLength() == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool JobProposalHasRequiredFields(string jobId, JsonElement element) => jobId switch
    {
        GenerationJobId.ProposeMemories =>
            !string.IsNullOrWhiteSpace(JsonElementParsing.GetStringProperty(element, "text")),
        GenerationJobId.ExtractEntities =>
            !string.IsNullOrWhiteSpace(JsonElementParsing.GetStringProperty(element, "name")),
        GenerationJobId.BootstrapLore or GenerationJobId.ExpandStoryCard
            or GenerationJobId.BootstrapSections or GenerationJobId.ExpandSection =>
            !string.IsNullOrWhiteSpace(JsonElementParsing.GetStringProperty(element, "name")),
        GenerationJobId.ProposeSourceEdits =>
            !string.IsNullOrWhiteSpace(JsonElementParsing.GetStringProperty(element, "targetFile"))
            && !string.IsNullOrWhiteSpace(JsonElementParsing.GetStringProperty(element, "content")),
        _ => true,
    };

    private static string BuildProcessTurnPrompt(AdventureBundle bundle, GenerationJobContext context)
    {
        var tasks = new List<string>();
        if (context.ProcessTurnIncludeMemories)
            tasks.Add("- memories: event proposals for the scoped exchange");
        if (context.ProcessTurnIncludeEntities)
            tasks.Add("- entities: world-model proposals for the scoped exchange");
        if (context.ProcessTurnIncludeSummary)
            tasks.Add("- summary: updated rolling digest (plain string)");

        var scopeBlock = context.Scope is { } scope
            ? UtilityTranscriptScopeService.FormatScopeBlock(scope)
            : "=== SCOPE ===\nTarget: newest play exchange (offset 0).";

        return $"""
            === PROCESS EXCHANGE JOB ===
            Return JSON object with keys: {string.Join(", ", tasks.Select(t => t.Split(':')[0].TrimStart('-', ' ')))}.
            {scopeBlock}

            === TASKS ===
            {string.Join(Environment.NewLine, tasks)}
            """;
    }

    private static string BuildScopedMemoryProposalPrompt(
        AdventureBundle bundle,
        UtilityTranscriptScope scope,
        bool omitTurnSlice)
    {
        var scopeBlock = UtilityTranscriptScopeService.FormatScopeBlock(scope);
        var pair = scope.TargetPair;
        var exchange = omitTurnSlice || pair is null
            ? ""
            : $"""

              === EXCHANGE ===
              PLAYER: {pair.PlayerText}
              NARRATOR: {pair.NarratorText}
              """;

        return $"""
            === MEMORY PROPOSAL JOB ===
            Return JSON array of event objects: text, tags, pinned, anchor object (pairOffset, playerHint), optional outcome.
            Record discrete events only — not entity definitions or rolling digest.

            {scopeBlock}
            {exchange}
            """;
    }

    private static string BuildMemoryProposalPrompt(AdventureBundle bundle, TurnRecord turn, bool omitTurnSlice)
    {
        var scope = new UtilityTranscriptScope
        {
            TargetPair = new TranscriptTurnPair
            {
                TurnIndex = turn.Index,
                PlayerText = turn.PlayerText,
                NarratorText = turn.NarratorText ?? "",
            },
            Anchor = UtilityTranscriptScopeService.BuildAnchor(
                new TranscriptTurnPair
                {
                    TurnIndex = turn.Index,
                    PlayerText = turn.PlayerText,
                    NarratorText = turn.NarratorText ?? "",
                },
                pairOffset: 0),
        };
        return BuildScopedMemoryProposalPrompt(bundle, scope, omitTurnSlice);
    }

    private static string BuildBootstrapLorePrompt(AdventureBundle bundle)
    {
        var s = bundle.Scenario;
        return $"""
            === BOOTSTRAP LORE JOB ===
            Generate 3-6 story cards from the scenario below. JSON array only.

            Title: {bundle.Metadata.Title}
            Genre: {s.Genre}
            Setting: {s.Setting}
            Player role: {s.PlayerRole}
            Opening: {s.OpeningSituation}
            Plot essentials: {s.PlotEssentials}
            World rules: {s.WorldRules}
            """;
    }

    private static string BuildBootstrapSectionsPrompt(AdventureBundle bundle)
    {
        var s = bundle.Scenario;
        return $"""
            === BOOTSTRAP SECTIONS JOB ===
            Generate 3-6 canon entity sections (NPCs, places, or concepts) from the scenario. JSON array only.
            Each object must include name, entityType (person|place|concept), description, and aliases array.

            Title: {bundle.Metadata.Title}
            Genre: {s.Genre}
            Setting: {s.Setting}
            Player role: {s.PlayerRole}
            Opening: {s.OpeningSituation}
            Plot essentials: {s.PlotEssentials}
            World rules: {s.WorldRules}
            """;
    }

    private static string BuildExpandSectionPrompt(AdventureBundle bundle, Guid entityId)
    {
        var character = bundle.Entities.Characters.FirstOrDefault(c => c.Id == entityId);
        if (character is not null)
        {
            return $"""
                === EXPAND SECTION JOB ===
                Expand this NPC section with richer canon. JSON array with one object (name, entityType, description, aliases, flavor).

                Name: {character.Name}
                Description: {character.Description}
                Aliases: {string.Join(", ", character.Aliases)}
                """;
        }

        return EntityExtractionService.BuildExpandEntityPrompt(bundle, "Characters", entityId);
    }

    private static string BuildExpandCardPrompt(AdventureBundle bundle, Guid cardId)
    {
        var card = bundle.Cards.Cards.FirstOrDefault(c => c.Id == cardId)
                   ?? throw new InvalidOperationException("Card not found.");
        return $"""
            === EXPAND STORY CARD JOB ===
            Expand this story card with richer lore. Return JSON array with one card object.

            Name: {card.Name}
            Type: {card.Type}
            Triggers: {string.Join(", ", card.Triggers)}
            Current content: {card.Content}
            """;
    }

    private static string BuildContinuityCheckPrompt(
        AdventureBundle bundle,
        bool omitRedundantSlices = false,
        bool storyContextIncludesSummary = false,
        bool storyContextIncludesState = false)
    {
        var sections = new List<string>
        {
            """
            === CONTINUITY CHECK JOB ===
            Return JSON object with a warnings array; each warning has message and severity fields.
            """,
        };

        if (!omitRedundantSlices || !storyContextIncludesSummary)
        {
            sections.Add($"""
                === SUMMARY ===
                {bundle.Summary.RollingSummary}
                """);
        }

        if (!omitRedundantSlices || !storyContextIncludesState)
        {
            sections.Add($"""
                === STATE ===
                Location: {bundle.State.CurrentLocation}
                Objectives: {bundle.State.OpenObjectives}
                """);
        }

        sections.Add($"""
            === ENTITY INDEX ===
            {EntityExtractionService.BuildEntityIndex(bundle.Entities)}
            """);

        if (!omitRedundantSlices)
        {
            var recent = bundle.Log.Turns
                .Where(t => t.Status == TurnStatus.Accepted)
                .OrderBy(t => t.Index)
                .TakeLast(8)
                .Select(t => $"[{t.Index}] P: {t.PlayerText}\nN: {t.NarratorText ?? ""}");

            sections.Add($"""
                === RECENT TURNS ===
                {string.Join(Environment.NewLine + Environment.NewLine, recent)}
                """);
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private static GenerationJobResult ApplyDraftFramework(AdventureBundle bundle, string responseText)
    {
        var path = DraftFrameworkService.WriteDraftToSources(bundle, responseText);
        return new GenerationJobResult
        {
            Success = true,
            ProposalCount = 0,
            DisplayText = responseText,
            DraftSourcePath = path,
        };
    }

    private static GenerationJobResult ApplyDesignExtract(
        AdventureBundle bundle,
        string responseText,
        AdventureDesignStep? step)
    {
        var designStep = step ?? bundle.DesignWorkspace.CurrentStep;
        var proposals = AdventureDesignExtractionService.ParseExtractResponse(designStep, responseText);
        if (proposals.Count > 0)
            AdventureDesignService.AddProposals(bundle, designStep, proposals);

        return new GenerationJobResult
        {
            Success = true,
            ProposalCount = proposals.Count,
            DesignProposals = proposals,
            DesignStep = designStep,
            Error = proposals.Count == 0 ? "no_proposals_parsed" : null,
        };
    }

    private static GenerationJobResult ApplyExtractEntities(AdventureBundle bundle, string responseText)
    {
        var proposals = EntityExtractionService.ParseExtractionResponse(responseText);
        if (proposals.Count > 0)
            EntityExtractionService.EnqueueProposals(bundle.Entities, proposals);

        return new GenerationJobResult
        {
            Success = true,
            ProposalCount = proposals.Count,
            ProposalIds = proposals.Select(p => p.Id).ToList(),
            Error = proposals.Count == 0 ? "no_proposals_parsed" : null,
        };
    }

    private static GenerationJobResult ApplyProposeMemories(AdventureBundle bundle, string responseText)
    {
        var count = ApplyMemoryArray(bundle, responseText);
        return new GenerationJobResult
        {
            Success = true,
            ProposalCount = count,
            Error = count == 0 ? "no_proposals_parsed" : null,
        };
    }

    private static int ApplyMemoryArray(AdventureBundle bundle, string responseText)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonArrayResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return 0;

            var count = 0;
            foreach (var element in JsonElementParsing.EnumerateObjectElements(doc.RootElement))
            {
                var entry = ParseMemoryEntry(element);
                if (entry is null)
                    continue;

                if (UtilityTranscriptScopeService.IsDuplicateMemory(bundle.Memory, entry))
                    continue;

                bundle.Memory.ReviewQueue.Add(entry);
                count++;
            }

            return count;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return 0;
        }
    }

    private static MemoryEntry? ParseMemoryEntry(JsonElement element)
    {
        var text = JsonElementParsing.GetStringProperty(element, "text") ?? "";
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var tags = ParseStringArrayProperty(element, "tags");
        var pinned = element.TryGetProperty("pinned", out var pinEl) && pinEl.ValueKind == JsonValueKind.True;
        var outcome = JsonElementParsing.GetStringProperty(element, "outcome");

        MemoryAnchor? anchor = null;
        if (element.TryGetProperty("anchor", out var anchorEl) && anchorEl.ValueKind == JsonValueKind.Object)
        {
            anchor = new MemoryAnchor
            {
                Kind = JsonElementParsing.GetStringProperty(anchorEl, "kind") ?? "transcript",
                PairOffset = anchorEl.TryGetProperty("pairOffset", out var off) && off.TryGetInt32(out var o) ? o : 0,
                PlayerHint = JsonElementParsing.GetStringProperty(anchorEl, "playerHint"),
                ContentHash = JsonElementParsing.GetStringProperty(anchorEl, "contentHash"),
                CapturedAt = DateTimeOffset.UtcNow,
            };
        }

        return new MemoryEntry
        {
            Text = text,
            Tags = tags,
            Pinned = pinned,
            Outcome = outcome,
            Anchor = anchor,
        };
    }

    private static GenerationJobResult ApplyProcessTurn(AdventureBundle bundle, string responseText)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonObjectResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return new GenerationJobResult { Success = true, ProposalCount = 0, Error = "parse_failed" };

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            var root = doc.RootElement;
            var total = 0;

            if (root.TryGetProperty("memories", out var memories))
            {
                var memJson = memories.ValueKind == JsonValueKind.Array ? memories.GetRawText() : "[]";
                total += ApplyMemoryArray(bundle, memJson);
            }

            if (root.TryGetProperty("entities", out var entities))
            {
                var entJson = entities.ValueKind == JsonValueKind.Array ? entities.GetRawText() : "[]";
                var proposals = EntityExtractionService.ParseExtractionResponse(entJson);
                if (proposals.Count > 0)
                    EntityExtractionService.EnqueueProposals(bundle.Entities, proposals);
                total += proposals.Count;
            }

            if (root.TryGetProperty("summary", out var summaryEl)
                && summaryEl.ValueKind == JsonValueKind.String)
            {
                var summaryText = summaryEl.GetString()?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(summaryText))
                {
                    bundle.Summary.ProposedSummary = summaryText;
                    bundle.Summary.PendingReview = true;
                    total++;
                }
            }

            return new GenerationJobResult
            {
                Success = true,
                ProposalCount = total,
                Error = total == 0 ? "no_proposals_parsed" : null,
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new GenerationJobResult { Success = true, ProposalCount = 0, Error = "parse_failed" };
        }
    }

    private static bool IsSettledProcessTurnResponse(string responseText, bool streamComplete)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonObjectResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            var hasMemories = root.TryGetProperty("memories", out var mem)
                              && mem.ValueKind == JsonValueKind.Array;
            var hasEntities = root.TryGetProperty("entities", out var ent)
                              && ent.ValueKind == JsonValueKind.Array;
            var hasSummary = root.TryGetProperty("summary", out var sum)
                             && sum.ValueKind == JsonValueKind.String;

            if (!hasMemories && !hasEntities && !hasSummary)
                return false;

            if (hasMemories && HasActionableMemoryArray(mem.GetRawText()))
                return true;
            if (hasEntities && HasActionableEntityArray(ent.GetRawText()))
                return true;
            if (hasSummary && !string.IsNullOrWhiteSpace(sum.GetString()))
                return true;

            return streamComplete && (hasMemories || hasEntities || hasSummary);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasActionableMemoryArray(string json)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonArrayResponse(json);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var element in JsonElementParsing.EnumerateObjectElements(doc.RootElement))
            {
                if (!string.IsNullOrWhiteSpace(JsonElementParsing.GetStringProperty(element, "text")))
                    return true;
            }

            return doc.RootElement.GetArrayLength() == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasActionableEntityArray(string json) =>
        EntityExtractionService.ParseExtractionResponse(json).Count > 0
        || IsEmptyArrayJson(json);

    private static bool IsEmptyArrayJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array
                   && doc.RootElement.GetArrayLength() == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static GenerationJobResult ApplyUpdateSummary(AdventureBundle bundle, string responseText)
    {
        if (!IsValidPlainTextJobResponse(responseText))
        {
            return new GenerationJobResult
            {
                Success = true,
                ProposalCount = 0,
                Error = LooksLikeMisroutedStructuredJson(responseText)
                    ? "wrong_response_format"
                    : "no_proposals_parsed",
            };
        }

        var text = StripPlainText(responseText);
        if (string.IsNullOrWhiteSpace(text))
            return new GenerationJobResult { Success = true, ProposalCount = 0, Error = "no_proposals_parsed" };

        bundle.Summary.ProposedSummary = text;
        bundle.Summary.PendingReview = true;
        return new GenerationJobResult { Success = true, ProposalCount = 1 };
    }

    private static GenerationJobResult ApplyBootstrapLore(AdventureBundle bundle, string responseText) =>
        ApplyCardArray(bundle, responseText);

    private static GenerationJobResult ApplyExpandCard(AdventureBundle bundle, string responseText) =>
        ApplyCardArray(bundle, responseText);

    private static GenerationJobResult ApplySectionBootstrap(AdventureBundle bundle, string responseText)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonArrayResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return new GenerationJobResult { Success = true, ProposalCount = 0, Error = "no_proposals_parsed" };

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            var elements = doc.RootElement.ValueKind == JsonValueKind.Array
                ? JsonElementParsing.EnumerateObjectElements(doc.RootElement).ToList()
                : doc.RootElement.ValueKind == JsonValueKind.Object
                    ? [doc.RootElement]
                    : [];

            var count = 0;
            foreach (var element in elements)
            {
                var name = JsonElementParsing.GetStringProperty(element, "name") ?? "";
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                bundle.Entities.ReviewQueue.Add(new EntityReviewItem
                {
                    EntityType = JsonElementParsing.GetStringProperty(element, "entityType") ?? "person",
                    ProposedChange = element.GetRawText(),
                });
                count++;
            }

            return new GenerationJobResult
            {
                Success = true,
                ProposalCount = count,
                Error = count == 0 ? "no_proposals_parsed" : null,
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new GenerationJobResult { Success = true, ProposalCount = 0, Error = "parse_failed" };
        }
    }

    private static GenerationJobResult ApplyCardArray(AdventureBundle bundle, string responseText)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonArrayResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return new GenerationJobResult { Success = true, ProposalCount = 0, Error = "no_proposals_parsed" };

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            var elements = doc.RootElement.ValueKind == JsonValueKind.Array
                ? JsonElementParsing.EnumerateObjectElements(doc.RootElement).ToList()
                : doc.RootElement.ValueKind == JsonValueKind.Object
                    ? [doc.RootElement]
                    : [];

            var count = 0;
            foreach (var element in elements)
            {
                var name = JsonElementParsing.GetStringProperty(element, "name") ?? "";
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                bundle.Cards.ReviewQueue.Add(new CardReviewItem
                {
                    ProposedChange = element.GetRawText(),
                });
                count++;
            }

            return new GenerationJobResult
            {
                Success = true,
                ProposalCount = count,
                Error = count == 0 ? "no_proposals_parsed" : null,
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new GenerationJobResult { Success = true, ProposalCount = 0, Error = "parse_failed" };
        }
    }

    private static GenerationJobResult ApplyProposeJsonImport(AdventureBundle bundle, string responseText)
    {
        var count = SourceJsonImportService.ParseAndEnqueue(bundle, responseText);
        return new GenerationJobResult
        {
            Success = true,
            ProposalCount = count,
            Error = count == 0 ? "no_proposals_parsed" : null,
        };
    }

    private static GenerationJobResult ApplyProposeSourceEdits(AdventureBundle bundle, string responseText)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return new GenerationJobResult { Success = true, ProposalCount = 0, Error = "no_proposals_parsed" };

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            var elements = doc.RootElement.ValueKind == JsonValueKind.Array
                ? JsonElementParsing.EnumerateObjectElements(doc.RootElement).ToList()
                : doc.RootElement.ValueKind == JsonValueKind.Object
                    ? [doc.RootElement]
                    : [];

            var count = 0;
            foreach (var element in elements)
            {
                var target = JsonElementParsing.GetStringProperty(element, "targetFile") ?? "";
                var content = JsonElementParsing.GetStringProperty(element, "content") ?? "";
                if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(content))
                    continue;

                bundle.Scenario.SourceEditReviewQueue.Add(new SourceEditReviewItem
                {
                    TargetFile = target,
                    Operation = JsonElementParsing.GetStringProperty(element, "operation") ?? "replace",
                    Content = content,
                    Rationale = JsonElementParsing.GetStringProperty(element, "rationale") ?? "",
                });
                count++;
            }

            return new GenerationJobResult
            {
                Success = true,
                ProposalCount = count,
                Error = count == 0 ? "no_proposals_parsed" : null,
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new GenerationJobResult { Success = true, ProposalCount = 0, Error = "parse_failed" };
        }
    }

    private static GenerationJobResult ApplyContinuityCheck(AdventureBundle bundle, string responseText)
    {
        bundle.Continuity.Warnings.Clear();
        foreach (var local in ContinuityService.Analyze(bundle))
        {
            bundle.Continuity.Warnings.Add(new ContinuityWarningEntry
            {
                Message = local.Message,
                Severity = local.Severity,
                Source = "local",
            });
        }

        var normalized = EntityExtractionService.TryNormalizeJsonResponse(responseText);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            try
            {
                using var doc = JsonDocument.Parse(normalized);
                if (doc.RootElement.TryGetProperty("warnings", out var warnings)
                    && warnings.ValueKind == JsonValueKind.Array)
                {
                    foreach (var w in JsonElementParsing.EnumerateObjectElements(warnings))
                    {
                        var message = JsonElementParsing.GetStringProperty(w, "message") ?? "";
                        if (string.IsNullOrWhiteSpace(message))
                            continue;

                        var severity = JsonElementParsing.GetStringProperty(w, "severity") ?? "warning";
                        bundle.Continuity.Warnings.Add(new ContinuityWarningEntry
                        {
                            Message = message,
                            Severity = severity,
                            Source = "ai",
                        });
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                /* keep local warnings */
            }
        }

        bundle.Continuity.LastCheckedAt = DateTimeOffset.UtcNow;
        return new GenerationJobResult
        {
            Success = true,
            ProposalCount = bundle.Continuity.Warnings.Count,
        };
    }

    public static bool ApplyAcceptedCardReviewItem(CardsDocument cards, CardReviewItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ProposedChange))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(item.ProposedChange);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var typeStr = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "Lore" : "Lore";
            Enum.TryParse<StoryCardType>(typeStr, ignoreCase: true, out var cardType);

            var triggers = root.TryGetProperty("triggers", out var trigEl) && trigEl.ValueKind == JsonValueKind.Array
                ? trigEl.EnumerateArray().Select(t => t.GetString() ?? "").Where(t => !string.IsNullOrWhiteSpace(t)).ToList()
                : [];
            var content = root.TryGetProperty("content", out var contentEl) ? contentEl.GetString() ?? "" : "";
            var enabled = !root.TryGetProperty("enabled", out var enEl) || enEl.ValueKind != JsonValueKind.False;

            cards.Cards.Add(new StoryCard
            {
                Name = name,
                Type = cardType,
                Triggers = triggers.Count > 0 ? triggers : ["keyword"],
                Content = content,
                Enabled = enabled,
            });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool IsValidPlainTextJobResponse(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return false;

        if (LooksLikeMisroutedStructuredJson(responseText))
            return false;

        return !string.IsNullOrWhiteSpace(StripPlainText(responseText));
    }

    internal static bool LooksLikeMisroutedStructuredJson(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return false;

        var normalized = EntityExtractionService.TryNormalizeJsonArrayResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return false;

            foreach (var element in JsonElementParsing.EnumerateObjectElements(doc.RootElement))
            {
                if (element.TryGetProperty("entityType", out _))
                    return true;

                if (element.TryGetProperty("text", out var textProp)
                    && textProp.ValueKind != JsonValueKind.Null
                    && element.TryGetProperty("tags", out _))
                {
                    return true;
                }

                if (element.TryGetProperty("triggers", out _)
                    && element.TryGetProperty("content", out _))
                {
                    return true;
                }

                if (element.TryGetProperty("targetFile", out _))
                    return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static List<string> ParseStringArrayProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var arrayEl) || arrayEl.ValueKind != JsonValueKind.Array)
            return [];

        var tags = new List<string>();
        foreach (var item in arrayEl.EnumerateArray())
        {
            var value = JsonElementParsing.GetStringOrNull(item);
            if (!string.IsNullOrWhiteSpace(value))
                tags.Add(value);
        }

        return tags;
    }

    private static string NormalizeCapturedJobResponse(string responseText) =>
        ContextTagFormat.UnwrapUtilityJobResponse(responseText);

    private static string StripPlainText(string responseText)
    {
        var text = responseText.Trim();
        var fenceMatch = System.Text.RegularExpressions.Regex.Match(
            text,
            @"```(?:\w+)?\s*([\s\S]*?)\s*```",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (fenceMatch.Success)
            text = fenceMatch.Groups[1].Value.Trim();

        return text.Trim();
    }
}
