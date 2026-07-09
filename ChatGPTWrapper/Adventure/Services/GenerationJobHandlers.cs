using System.Text;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;
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

    public bool UtilityContextAssembled { get; set; }

    public UtilityContextManifest? UtilityContextManifest { get; set; }

    public AdventureDesignStep? DesignStep { get; init; }

    /// <summary>Utility lane that produced the current apply (e.g. local-llm, play-legacy-inline).</summary>
    public string? InferenceSource { get; set; }

    public Guid? UtilityRunId { get; set; }

    /// <summary>Targets for <see cref="GenerationJobId.ProposeEntityState"/>.</summary>
    public IReadOnlyList<EntityReferenceRow>? EntityStateTargets { get; init; }

    /// <summary>Reference panel category filter when proposing entity state.</summary>
    public string? EntityCategoryFilter { get; init; }

    /// <summary>Links paired runs when dual-run compare mode is active.</summary>
    public Guid? DualRunGroupId { get; set; }

    /// <summary>When true, duplicate suppression ignores proposals from other inference sources.</summary>
    public bool AllowCrossSourceDuplicates { get; set; }

    /// <summary>Local LLM leg — shorter prompts, wrapped JSON contracts, no canon-format reference block.</summary>
    public bool ForLocalInference { get; set; }

    /// <summary>Attachment metadata for utility worker jobs (manifest in packet; bytes staged separately).</summary>
    public AttachmentContext? JobAttachments { get; init; }

    /// <summary>Author instructions for staged reference files (appended to job packet, not the main job body).</summary>
    public string? AttachmentReferenceNote { get; init; }

    public Guid? PlayThreadIngestEventId { get; set; }

    public Guid? PlayThreadEntryId { get; set; }

    public string? PlayThreadRawPath { get; set; }

    public string? PlayThreadProjectionPath { get; set; }

    public string? ContextProjectionPath { get; set; }

    public string? SourceIoInputPath { get; set; }

    public string? EphemeralCapturePath { get; set; }
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

    /// <summary>True when the job ran on the utility worker lane (not play-thread inline/injection).</summary>
    public bool RanOnUtilityWorker { get; init; }

    /// <summary>True when the job completed via local OpenAI-compatible inference (Ollama, etc.).</summary>
    public bool RanOnLocalInference { get; init; }

    /// <summary>True when both local inference and ChatGPT utility lanes ran for comparison.</summary>
    public bool RanDualInference { get; init; }
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
                EntityExtractionService.BuildScopedExtractionPrompt(
                    bundle,
                    extractScope,
                    context.UtilityRunId),
            GenerationJobId.ExtractEntities when context.Turn is { } turn =>
                EntityExtractionService.BuildExtractionPrompt(bundle, turn, context.UtilityRunId),
            GenerationJobId.ProposeEntitiesFile when context.Scope is { } fileScope =>
                EntitiesFileRevisionService.BuildRevisionPrompt(
                    bundle,
                    fileScope,
                    context.UtilityRunId ?? Guid.NewGuid()),
            GenerationJobId.ProposeEntitiesFile =>
                EntitiesFileRevisionService.BuildRevisionPrompt(
                    bundle,
                    context.Scope,
                    context.UtilityRunId ?? Guid.NewGuid()),
            GenerationJobId.ExpandEntity when context.EntityId is { } entityId =>
                EntityExtractionService.BuildExpandEntityPrompt(
                    bundle,
                    context.EntityKind ?? "Characters",
                    entityId,
                    context.UtilityRunId),
            GenerationJobId.ProposeMemories when context.Scope is { } memScope =>
                BuildScopedMemoryProposalPrompt(bundle, memScope, UtilityStoryContextDedup.ShouldOmitTurnSlices(context)),
            GenerationJobId.ProposeMemories when context.Turn is { } memTurn =>
                BuildMemoryProposalPrompt(bundle, memTurn, UtilityStoryContextDedup.ShouldOmitTurnSlices(context)),
            GenerationJobId.UpdateState =>
                StateUpdateService.BuildPrompt(bundle, context.Scope, UtilityStoryContextDedup.ShouldOmitTurnSlices(context)),
            GenerationJobId.ProposeEntityState =>
                EntityInternalStateProposalService.BuildPrompt(
                    bundle,
                    ResolveEntityStateTargets(bundle, context),
                    context.EntityCategoryFilter ?? "",
                    context.Scope ?? BuildScopeFromTurn(context.Turn),
                    context.UtilityRunId,
                    UtilityStoryContextDedup.ShouldOmitTurnSlices(context)),
            GenerationJobId.ProposeCanonEvolution =>
                EntityCanonEvolutionProposalService.BuildPrompt(
                    bundle,
                    ResolveEntityStateTargets(bundle, context),
                    context.EntityCategoryFilter ?? "",
                    context.Scope ?? BuildScopeFromTurn(context.Turn),
                    context.UtilityRunId),
            GenerationJobId.UpdateSummary =>
                RecapService.BuildSummaryUpdatePrompt(bundle, UtilityStoryContextDedup.ShouldOmitTurnSlices(context)),
            GenerationJobId.BootstrapLore =>
                BuildBootstrapLorePrompt(bundle),
            GenerationJobId.BootstrapSections =>
                BuildBootstrapSectionsPrompt(bundle, context.ForLocalInference),
            GenerationJobId.ExpandStoryCard when context.CardId is { } cardId =>
                BuildExpandCardPrompt(bundle, cardId),
            GenerationJobId.ExpandSection when context.EntityId is { } sectionEntityId =>
                BuildExpandSectionPrompt(bundle, sectionEntityId),
            GenerationJobId.ContinuityCheck =>
                BuildContinuityCheckPrompt(bundle, context),
            GenerationJobId.ResolveContinuityWarning =>
                """
                === RESOLVE CONTINUITY WARNING JOB ===
                Return JSON object with optional keys:
                entities: { updates: [...] }
                state: { location, objectives, objectivesRemove, flags, time, rationale }
                Include only the sections needed to resolve the selected warning.
                """,
            GenerationJobId.AuditCanon =>
                """
                === CANON AUDIT JOB ===
                Return JSON object: { "warnings": [ { "message": string, "severity": "info|warning|high", "category": string, "refs": string[] } ] }.
                """,
            GenerationJobId.RefreshContextIndex =>
                """
                === CONTEXT INDEX REFRESH JOB ===
                This job is rule-based in the wrapper. Return {}.
                """,
            GenerationJobId.ProposeSourceEdits when context.UtilityRunId is { } sourceEditRunId
                && UtilitySourceFileIoCatalog.UsesSourceFileIo(GenerationJobId.ProposeSourceEdits)
                && !context.ForLocalInference =>
                SourceFileRevisionService.BuildRevisionPrompt(
                    bundle,
                    context.UserPrompt ?? "",
                    sourceEditRunId),
            GenerationJobId.ProposeSourceEdits =>
                SourceEditService.BuildSourceEditPrompt(bundle, context.UserPrompt ?? "", context.ForLocalInference),
            GenerationJobId.ProposeJsonImport =>
                SourceJsonImportService.BuildImportPrompt(bundle, context.ForLocalInference),
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
            GenerationJobId.UtilityWorkerPing =>
                BuildWorkerPingPrompt(context.UserPrompt ?? Guid.NewGuid().ToString("N")[..8]),
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
        var conversationId = PlayThreadBindingService.GetActiveConversationId(bundle)
                               ?? bundle.Metadata.LinkedConversationId;
        if (string.IsNullOrWhiteSpace(conversationId))
            return "";

        return $"Play thread: {conversationId}";
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
            GenerationJobId.ProcessTurn => ApplyProcessTurn(bundle, responseText, context),
            GenerationJobId.ExtractEntities or GenerationJobId.ExpandEntity => ApplyExtractEntities(bundle, responseText, context),
            GenerationJobId.ProposeEntitiesFile => ApplyProposeEntitiesFile(bundle, responseText, context),
            GenerationJobId.ProposeMemories => ApplyProposeMemories(bundle, responseText, context),
            GenerationJobId.ProposeEntityState => ApplyProposeEntityState(bundle, responseText, context),
            GenerationJobId.ProposeCanonEvolution => ApplyProposeCanonEvolution(bundle, responseText, context),
            GenerationJobId.UpdateState => ApplyUpdateState(bundle, responseText, context),
            GenerationJobId.UpdateSummary => ApplyUpdateSummary(bundle, responseText, context),
            GenerationJobId.BootstrapLore => ApplyBootstrapLore(bundle, responseText, context),
            GenerationJobId.BootstrapSections => ApplySectionBootstrap(bundle, responseText, context),
            GenerationJobId.ExpandStoryCard => ApplyExpandCard(bundle, responseText, context),
            GenerationJobId.ExpandSection => ApplySectionBootstrap(bundle, responseText, context),
            GenerationJobId.ContinuityCheck => ApplyContinuityCheck(bundle, responseText, context),
            GenerationJobId.ResolveContinuityWarning => ApplyResolveContinuityWarning(bundle, responseText, context),
            GenerationJobId.AuditCanon => ApplyAuditCanon(bundle, responseText, context),
            GenerationJobId.RefreshContextIndex => ApplyRefreshContextIndex(bundle, context),
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
            GenerationJobId.UtilityWorkerPing =>
                new GenerationJobResult
                {
                    Success = IsWorkerPingResponseValid(responseText, context?.UserPrompt),
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
        GenerationJobId.ExtractEntities => true,
        GenerationJobId.ProposeMemories => true,
        GenerationJobId.ProposeEntityState => true,
        GenerationJobId.ProposeCanonEvolution => true,
        GenerationJobId.UpdateState => true,
        GenerationJobId.DesignExtractStep => true,
        GenerationJobId.ProposeJsonImport => true,
        GenerationJobId.ProposeEntitiesFile => true,
        GenerationJobId.UtilityWorkerPing => true,
        GenerationJobId.ContinuityCheck => true,
        GenerationJobId.ResolveContinuityWarning => true,
        GenerationJobId.AuditCanon => true,
        GenerationJobId.RefreshContextIndex => true,
        _ => false,
    };

    public static bool ExpectsJsonArrayResponse(string jobId) => jobId switch
    {
        GenerationJobId.ExpandEntity
            
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
            if (string.Equals(jobId, GenerationJobId.ExtractEntities, StringComparison.Ordinal))
                return IsParseableExtractEntitiesResponse(responseText);
            if (string.Equals(jobId, GenerationJobId.ProposeMemories, StringComparison.Ordinal))
                return IsParseableMemoryResponse(responseText);
            if (string.Equals(jobId, GenerationJobId.ProposeJsonImport, StringComparison.Ordinal))
                return SourceJsonImportService.IsParseableResponse(responseText);
            if (string.Equals(jobId, GenerationJobId.ProposeEntitiesFile, StringComparison.Ordinal))
                return EntitiesFileRevisionService.IsParseableResponse(responseText);
            if (string.Equals(jobId, GenerationJobId.UtilityWorkerPing, StringComparison.OrdinalIgnoreCase))
                return IsSettledWorkerPingResponse(responseText);
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
            if (string.Equals(jobId, GenerationJobId.ProposeEntitiesFile, StringComparison.Ordinal))
                return EntitiesFileRevisionService.IsSettledResponse(responseText, streamComplete);
            if (string.Equals(jobId, GenerationJobId.UtilityWorkerPing, StringComparison.OrdinalIgnoreCase))
                return IsSettledWorkerPingResponse(responseText);
            if (string.Equals(jobId, GenerationJobId.ContinuityCheck, StringComparison.Ordinal))
                return IsSettledContinuityCheckResponse(responseText, streamComplete);
            if (string.Equals(jobId, GenerationJobId.ExtractEntities, StringComparison.Ordinal))
                return IsSettledExtractEntitiesResponse(responseText, streamComplete);
            if (string.Equals(jobId, GenerationJobId.ProposeMemories, StringComparison.Ordinal))
                return IsSettledMemoryResponse(responseText, streamComplete);
            if (string.Equals(jobId, GenerationJobId.UpdateState, StringComparison.Ordinal))
                return IsSettledStateUpdateResponse(responseText, streamComplete);
            return IsSettledProcessTurnResponse(responseText, streamComplete);
        }

        if (HasActionableJobProposals(jobId, responseText))
            return true;

        if (string.Equals(jobId, GenerationJobId.ProposeSourceEdits, StringComparison.Ordinal)
            && SourceFileRevisionService.IsSettledResponse(responseText, streamComplete))
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
            tasks.Add("- entities: object with extractions[] and updates[] world-model proposals");

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
            Return JSON object with:
            - events: array of event objects (text, tags, pinned, anchor object, optional outcome)
            - links: array of optional memory links (fromMemoryId/fromMemoryText, toMemoryId/toMemoryText, relation, notes)
            Record discrete events only — not entity definitions or rolling digest.

            {MemoryBaselineService.BuildBaselineBlock(bundle)}

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

    private static string BuildBootstrapSectionsPrompt(AdventureBundle bundle, bool forLocalInference = false)
    {
        var s = bundle.Scenario;
        var formatReference = forLocalInference
            ? ""
            : CanonFormatReferenceService.BuildPromptBlock(bundle);
        var localHint = forLocalInference
            ? """

            Do not return labeled canon field sheets (Relationship, Secrets, Setting, etc.).
            Return entity records only — each with name, entityType, description, aliases.
            """
            : "";
        return $"""
            === BOOTSTRAP SECTIONS JOB ===
            Generate 3-6 canon entity sections (NPCs, places, or concepts) from the scenario. JSON array only.
            Each object must include name, entityType (person|place|concept), description, and aliases array.
            {formatReference}{localHint}

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

    private static string BuildContinuityCheckPrompt(AdventureBundle bundle, GenerationJobContext context)
    {
        var sections = new List<string>
        {
            """
            === CONTINUITY CHECK JOB ===
            Return JSON object:
            {
              "warnings": [
                { "message": string, "severity": "info|warning|high", "category": string, "refs": string[] }
              ]
            }
            """,
            $"""
            === CONTINUITY BRIEF ===
            {ContinuityBriefBuilder.BuildBriefJson(bundle, context)}
            """,
        };

        if (UtilityStoryContextDedup.ShouldIncludeSummary(context))
        {
            sections.Add($"""
                === SUMMARY ===
                {bundle.Summary.RollingSummary}
                """);
        }

        if (UtilityStoryContextDedup.ShouldIncludeState(context))
        {
            sections.Add($"""
                === STATE ===
                Location: {bundle.State.CurrentLocation}
                Objectives: {bundle.State.OpenObjectives}
                """);
        }

        if (UtilityStoryContextDedup.ShouldIncludeEntityIndex(context))
        {
            sections.Add($"""
                === ENTITY INDEX ===
                {EntityExtractionService.BuildEntityIndex(bundle.Entities)}
                """);
        }

        if (!UtilityStoryContextDedup.ShouldOmitTurnSlices(context))
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

    private static GenerationJobResult ApplyProposeEntitiesFile(
        AdventureBundle bundle,
        string responseText,
        GenerationJobContext? context = null)
    {
        var count = EntitiesFileRevisionService.ParseAndEnqueue(bundle, responseText, context);
        return new GenerationJobResult
        {
            Success = true,
            ProposalCount = count,
            Error = count == 0 && !EntitiesFileRevisionService.HasProposedSnapshot(bundle.Entities)
                ? "no_proposals_parsed"
                : null,
        };
    }

    private static GenerationJobResult ApplyExtractEntities(AdventureBundle bundle, string responseText, GenerationJobContext? context = null)
    {
        var dual = EntityExtractionService.ParseDualSectionResponse(responseText);
        var proposals = dual.Extractions.Concat(dual.Updates).ToList();
        foreach (var proposal in proposals)
            UtilityProposalInferenceTagging.TagEntity(proposal, context);

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

    private static GenerationJobResult ApplyProposeEntityState(AdventureBundle bundle, string responseText, GenerationJobContext? context = null)
    {
        var count = EntityInternalStateProposalService.ApplyPatches(bundle, responseText, context);
        return new GenerationJobResult
        {
            Success = true,
            ProposalCount = count,
            Error = count == 0 ? "no_proposals_parsed" : null,
        };
    }

    private static GenerationJobResult ApplyProposeCanonEvolution(AdventureBundle bundle, string responseText, GenerationJobContext? context = null)
    {
        var count = EntityCanonEvolutionProposalService.ApplyEvolutions(bundle, responseText, context);
        return new GenerationJobResult
        {
            Success = true,
            ProposalCount = count,
            Error = count == 0 ? "no_proposals_parsed" : null,
        };
    }

    private static GenerationJobResult ApplyProposeMemories(AdventureBundle bundle, string responseText, GenerationJobContext? context = null)
    {
        var count = ApplyMemoryArray(bundle, responseText, context);
        return new GenerationJobResult
        {
            Success = true,
            ProposalCount = count,
            Error = count == 0 ? "no_proposals_parsed" : null,
        };
    }

    private static int ApplyMemoryArray(AdventureBundle bundle, string responseText, GenerationJobContext? context = null)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            var root = doc.RootElement;
            JsonElement eventsArray;
            JsonElement linksArray;
            if (root.ValueKind == JsonValueKind.Array)
            {
                eventsArray = root;
                linksArray = default;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                eventsArray = root.TryGetProperty("events", out var eventsEl) && eventsEl.ValueKind == JsonValueKind.Array
                    ? eventsEl
                    : root.TryGetProperty("memories", out var legacyMemories) && legacyMemories.ValueKind == JsonValueKind.Array
                        ? legacyMemories
                        : default;
                linksArray = root.TryGetProperty("links", out var linksEl) && linksEl.ValueKind == JsonValueKind.Array
                    ? linksEl
                    : default;
            }
            else
            {
                return 0;
            }

            var count = 0;
            if (eventsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in JsonElementParsing.EnumerateObjectElements(eventsArray))
                {
                    var entry = ParseMemoryEntry(element);
                    if (entry is null)
                        continue;

                    if (UtilityTranscriptScopeService.IsDuplicateMemory(bundle.Memory, entry, context))
                        continue;

                    UtilityProposalInferenceTagging.TagMemory(entry, context);
                    bundle.Memory.ReviewQueue.Add(entry);
                    count++;
                }
            }

            if (linksArray.ValueKind == JsonValueKind.Array)
                ApplyMemoryLinks(bundle, linksArray, context);

            return count;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return 0;
        }
    }

    private static void ApplyMemoryLinks(AdventureBundle bundle, JsonElement linksArray, GenerationJobContext? context)
    {
        foreach (var link in JsonElementParsing.EnumerateObjectElements(linksArray))
        {
            var relation = JsonElementParsing.GetStringProperty(link, "relation") ?? "related";
            var fromMemoryId = JsonElementParsing.GetStringProperty(link, "fromMemoryId");
            var toMemoryId = JsonElementParsing.GetStringProperty(link, "toMemoryId");
            var fromMemoryText = JsonElementParsing.GetStringProperty(link, "fromMemoryText");
            var toMemoryText = JsonElementParsing.GetStringProperty(link, "toMemoryText");
            var notes = JsonElementParsing.GetStringProperty(link, "notes");

            if (string.IsNullOrWhiteSpace(fromMemoryId)
                && string.IsNullOrWhiteSpace(fromMemoryText)
                && string.IsNullOrWhiteSpace(toMemoryId)
                && string.IsNullOrWhiteSpace(toMemoryText))
            {
                continue;
            }

            bundle.Memory.Links.Add(new MemoryLinkEntry
            {
                FromMemoryId = Guid.TryParse(fromMemoryId, out var fromId) ? fromId : null,
                ToMemoryId = Guid.TryParse(toMemoryId, out var toId) ? toId : null,
                FromMemoryText = fromMemoryText,
                ToMemoryText = toMemoryText,
                Relation = relation,
                Notes = notes,
                InferenceSource = context?.InferenceSource,
                UtilityRunId = context?.UtilityRunId,
            });
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

    private static GenerationJobResult ApplyProcessTurn(AdventureBundle bundle, string responseText, GenerationJobContext? context = null)
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
                total += ApplyMemoryArray(bundle, memJson, context);
            }

            if (root.TryGetProperty("entities", out var entities))
            {
                var entJson = entities.ValueKind is JsonValueKind.Array or JsonValueKind.Object
                    ? entities.GetRawText()
                    : "{}";
                var dual = EntityExtractionService.ParseDualSectionResponse(entJson);
                var proposals = dual.Extractions.Concat(dual.Updates).ToList();
                foreach (var proposal in proposals)
                    UtilityProposalInferenceTagging.TagEntity(proposal, context);
                if (proposals.Count > 0)
                    EntityExtractionService.EnqueueProposals(bundle.Entities, proposals);
                total += proposals.Count;
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
                              && ent.ValueKind is JsonValueKind.Array or JsonValueKind.Object;

            if (!hasMemories && !hasEntities)
                return false;

            if (hasMemories && HasActionableMemoryArray(mem.GetRawText()))
                return true;
            if (hasEntities && HasActionableEntityArray(ent.GetRawText()))
                return true;

            return streamComplete && (hasMemories || hasEntities);
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

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasActionableEntityArray(string json) =>
        EntityExtractionService.ParseDualSectionResponse(json).Extractions.Count > 0
        || EntityExtractionService.ParseDualSectionResponse(json).Updates.Count > 0
        || IsEmptyArrayJson(json)
        || IsEmptyObjectEntitySections(json);

    private static bool IsEmptyObjectEntitySections(string json)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonObjectResponse(json);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            var hasExtractions = doc.RootElement.TryGetProperty("extractions", out var ex)
                                 && ex.ValueKind == JsonValueKind.Array
                                 && ex.GetArrayLength() == 0;
            var hasUpdates = doc.RootElement.TryGetProperty("updates", out var up)
                             && up.ValueKind == JsonValueKind.Array
                             && up.GetArrayLength() == 0;
            return hasExtractions || hasUpdates;
        }
        catch (JsonException)
        {
            return false;
        }
    }

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

    private static GenerationJobResult ApplyUpdateSummary(AdventureBundle bundle, string responseText, GenerationJobContext? context = null)
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

        SummaryReviewService.QueueProposal(bundle, text, context);
        return new GenerationJobResult { Success = true, ProposalCount = 1 };
    }

    private static GenerationJobResult ApplyUpdateState(AdventureBundle bundle, string responseText, GenerationJobContext? context = null)
    {
        var proposal = StateUpdateService.ParseResponse(responseText, context);
        if (proposal is null)
            return new GenerationJobResult { Success = true, ProposalCount = 0, Error = "no_proposals_parsed" };

        bundle.State.ReviewQueue.Add(proposal);
        return new GenerationJobResult { Success = true, ProposalCount = 1 };
    }

    private static GenerationJobResult ApplyBootstrapLore(AdventureBundle bundle, string responseText, GenerationJobContext? context = null) =>
        ApplyCardArray(bundle, responseText, context);

    private static GenerationJobResult ApplyExpandCard(AdventureBundle bundle, string responseText, GenerationJobContext? context = null) =>
        ApplyCardArray(bundle, responseText, context);

    private static GenerationJobResult ApplySectionBootstrap(AdventureBundle bundle, string responseText, GenerationJobContext? context = null)
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

                var item = new EntityReviewItem
                {
                    EntityType = JsonElementParsing.GetStringProperty(element, "entityType") ?? "person",
                    ProposedChange = element.GetRawText(),
                };
                UtilityProposalInferenceTagging.TagEntity(item, context);
                bundle.Entities.ReviewQueue.Add(item);
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

    private static GenerationJobResult ApplyCardArray(AdventureBundle bundle, string responseText, GenerationJobContext? context = null)
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

                var card = new CardReviewItem
                {
                    ProposedChange = element.GetRawText(),
                };
                UtilityProposalInferenceTagging.TagCard(card, context);
                bundle.Cards.ReviewQueue.Add(card);
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
        var extracted = SourceFileRevisionService.TryExtractProposalsJson(responseText);
        if (!string.IsNullOrWhiteSpace(extracted))
            responseText = extracted;

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

    private static bool IsSettledContinuityCheckResponse(string responseText, bool streamComplete)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonObjectResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            if (!doc.RootElement.TryGetProperty("warnings", out var warnings))
                return streamComplete;

            return warnings.ValueKind == JsonValueKind.Array
                   && (streamComplete || warnings.GetArrayLength() > 0);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsSettledExtractEntitiesResponse(string responseText, bool streamComplete)
    {
        var parsed = EntityExtractionService.ParseDualSectionResponse(responseText);
        if (parsed.Extractions.Count > 0 || parsed.Updates.Count > 0)
            return true;

        var normalizedArray = EntityExtractionService.TryNormalizeJsonArrayResponse(responseText);
        if (!string.IsNullOrWhiteSpace(normalizedArray))
        {
            try
            {
                using var arrayDoc = JsonDocument.Parse(normalizedArray);
                if (arrayDoc.RootElement.ValueKind == JsonValueKind.Array)
                    return streamComplete;
            }
            catch (JsonException)
            {
                /* continue */
            }
        }

        var normalized = EntityExtractionService.TryNormalizeJsonObjectResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var hasEx = doc.RootElement.TryGetProperty("extractions", out var ex) && ex.ValueKind == JsonValueKind.Array;
            var hasUp = doc.RootElement.TryGetProperty("updates", out var up) && up.ValueKind == JsonValueKind.Array;
            return streamComplete && (hasEx || hasUp);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsParseableExtractEntitiesResponse(string responseText)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                return true;
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var hasEx = doc.RootElement.TryGetProperty("extractions", out var ex) && ex.ValueKind == JsonValueKind.Array;
            var hasUp = doc.RootElement.TryGetProperty("updates", out var up) && up.ValueKind == JsonValueKind.Array;
            return hasEx || hasUp;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsSettledStateUpdateResponse(string responseText, bool streamComplete)
    {
        var proposal = StateUpdateService.ParseResponse(responseText);
        if (proposal is not null)
            return true;

        var normalized = EntityExtractionService.TryNormalizeJsonObjectResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            return streamComplete && doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsParseableMemoryResponse(string responseText)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                return true;
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var hasEvents = doc.RootElement.TryGetProperty("events", out var events)
                            && events.ValueKind == JsonValueKind.Array;
            var hasMemories = doc.RootElement.TryGetProperty("memories", out var memories)
                              && memories.ValueKind == JsonValueKind.Array;
            return hasEvents || hasMemories;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsSettledMemoryResponse(string responseText, bool streamComplete)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                return streamComplete || HasActionableMemoryArray(doc.RootElement.GetRawText());
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            var events = doc.RootElement.TryGetProperty("events", out var eventsEl) && eventsEl.ValueKind == JsonValueKind.Array
                ? eventsEl
                : doc.RootElement.TryGetProperty("memories", out var memoriesEl) && memoriesEl.ValueKind == JsonValueKind.Array
                    ? memoriesEl
                    : default;
            if (events.ValueKind != JsonValueKind.Array)
                return streamComplete;
            if (HasActionableMemoryArray(events.GetRawText()))
                return true;
            return streamComplete;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static GenerationJobResult ApplyContinuityCheck(AdventureBundle bundle, string responseText, GenerationJobContext? context = null)
    {
        bundle.Continuity.Warnings.Clear();
        foreach (var local in ContinuityService.Analyze(bundle))
        {
            if (ContinuityWarningDismissalService.IsDismissed(bundle.Continuity, local.Message))
                continue;

            bundle.Continuity.Warnings.Add(new ContinuityWarningEntry
            {
                Message = local.Message,
                Severity = local.Severity,
                Source = "local",
                Category = "local-heuristic",
            });
        }

        foreach (var crossLayer in EntityCanonStateOverlapService.AnalyzeCrossLayer(bundle))
        {
            if (ContinuityWarningDismissalService.IsDismissed(bundle.Continuity, crossLayer.Message))
                continue;

            bundle.Continuity.Warnings.Add(new ContinuityWarningEntry
            {
                Message = crossLayer.Message,
                Severity = crossLayer.Severity,
                Source = "local",
                Category = "canon-state-divergence",
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
                        var category = JsonElementParsing.GetStringProperty(w, "category") ?? "general";
                        var refs = ParseStringArrayProperty(w, "refs");
                        if (ContinuityWarningDismissalService.IsDismissed(bundle.Continuity, message))
                            continue;

                        bundle.Continuity.Warnings.Add(new ContinuityWarningEntry
                        {
                            Message = message,
                            Severity = severity,
                            Source = context?.InferenceSource ?? "ai",
                            Category = category,
                            Refs = refs,
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
        if (context?.Turn is { } turn)
            bundle.Continuity.LastCheckedTurnIndex = turn.Index;
        return new GenerationJobResult
        {
            Success = true,
            ProposalCount = bundle.Continuity.Warnings.Count,
        };
    }

    private static GenerationJobResult ApplyResolveContinuityWarning(
        AdventureBundle bundle,
        string responseText,
        GenerationJobContext? context = null)
    {
        var total = 0;
        var normalized = EntityExtractionService.TryNormalizeJsonObjectResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return new GenerationJobResult { Success = true, ProposalCount = 0, Error = "no_proposals_parsed" };

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            var root = doc.RootElement;
            if (root.TryGetProperty("entities", out var entitiesSection))
            {
                JsonElement updates;
                if (entitiesSection.ValueKind == JsonValueKind.Object
                    && entitiesSection.TryGetProperty("updates", out updates)
                    && updates.ValueKind == JsonValueKind.Array)
                {
                    var parsed = EntityExtractionService.ParseDualSectionResponse(
                        "{\"updates\":" + updates.GetRawText() + "}");
                    foreach (var proposal in parsed.Updates)
                        UtilityProposalInferenceTagging.TagEntity(proposal, context);
                    EntityExtractionService.EnqueueProposals(bundle.Entities, parsed.Updates);
                    total += parsed.Updates.Count;
                }
            }

            if (root.TryGetProperty("state", out var stateSection)
                && stateSection.ValueKind == JsonValueKind.Object)
            {
                var stateProposal = StateUpdateService.ParseResponse(stateSection.GetRawText(), context);
                if (stateProposal is not null)
                {
                    bundle.State.ReviewQueue.Add(stateProposal);
                    total++;
                }
            }
        }
        catch (JsonException)
        {
            return new GenerationJobResult { Success = true, ProposalCount = 0, Error = "parse_failed" };
        }

        return new GenerationJobResult
        {
            Success = true,
            ProposalCount = total,
            Error = total == 0 ? "no_proposals_parsed" : null,
        };
    }

    private static GenerationJobResult ApplyAuditCanon(
        AdventureBundle bundle,
        string responseText,
        GenerationJobContext? context = null)
    {
        var normalized = EntityExtractionService.TryNormalizeJsonObjectResponse(responseText);
        if (string.IsNullOrWhiteSpace(normalized))
            return new GenerationJobResult { Success = true, ProposalCount = 0, Error = "no_proposals_parsed" };

        try
        {
            using var doc = JsonDocument.Parse(normalized);
            if (!doc.RootElement.TryGetProperty("warnings", out var warnings)
                || warnings.ValueKind != JsonValueKind.Array)
            {
                return new GenerationJobResult { Success = true, ProposalCount = 0, Error = "no_proposals_parsed" };
            }

            var count = JsonElementParsing.EnumerateObjectElements(warnings).Count();
            return new GenerationJobResult { Success = true, ProposalCount = count };
        }
        catch (JsonException)
        {
            return new GenerationJobResult { Success = true, ProposalCount = 0, Error = "parse_failed" };
        }
    }

    private static GenerationJobResult ApplyRefreshContextIndex(
        AdventureBundle bundle,
        GenerationJobContext? context = null)
    {
        var count = ContextIndexRefreshService.RefreshFromEntities(bundle);
        return new GenerationJobResult
        {
            Success = true,
            ProposalCount = count,
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

    public static string BuildWorkerPingPrompt(string probeId) =>
        $$"""
        Utility worker capability probe.
        Reply with JSON only (no markdown fences):
        { "pong": true, "probeId": "{{probeId}}" }
        """;

    public static bool IsWorkerPingResponseValid(string? responseText, string? probeId)
    {
        if (string.IsNullOrWhiteSpace(responseText) || string.IsNullOrWhiteSpace(probeId))
            return false;

        var payload = NormalizeCapturedJobResponse(responseText);
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (!root.TryGetProperty("pong", out var pong) || pong.ValueKind != JsonValueKind.True)
                return false;
            if (!root.TryGetProperty("probeId", out var id))
                return false;
            return string.Equals(id.GetString(), probeId, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal static bool IsSettledWorkerPingResponse(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return false;

        var payload = NormalizeCapturedJobResponse(responseText);
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (!root.TryGetProperty("probeId", out var id))
                return false;

            var probeId = id.GetString();
            return IsWorkerPingResponseValid(payload, probeId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IReadOnlyList<EntityReferenceRow> ResolveEntityStateTargets(
        AdventureBundle bundle,
        GenerationJobContext context) =>
        context.EntityStateTargets is { Count: > 0 } explicitTargets
            ? explicitTargets
            : EntityStateJobTargetSelector.SelectPlayTrackedTargets(bundle);

    private static UtilityTranscriptScope? BuildScopeFromTurn(TurnRecord? turn)
    {
        if (turn is null)
            return null;

        var pair = new TranscriptTurnPair
        {
            TurnIndex = turn.Index,
            PlayerText = turn.PlayerText,
            NarratorText = turn.NarratorText ?? "",
        };
        return new UtilityTranscriptScope
        {
            TargetPair = pair,
            Anchor = UtilityTranscriptScopeService.BuildAnchor(pair, pairOffset: 0),
        };
    }
}
