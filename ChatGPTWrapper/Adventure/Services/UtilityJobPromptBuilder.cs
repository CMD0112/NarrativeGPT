using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Shared prompt assembly for utility jobs — keeps ChatGPT worker and local LLM legs comparable.
/// </summary>
internal static class UtilityJobPromptBuilder
{
    /// <summary>Play-tab AI Tools jobs that support dual-run prompt parity (ChatGPT vs Ollama).</summary>
    public static IReadOnlyList<string> ComparablePlayAiToolJobIds { get; } =
    [
        GenerationJobId.ProcessTurn,
        GenerationJobId.ExtractEntities,
        GenerationJobId.ExpandEntity,
        GenerationJobId.ProposeMemories,
        GenerationJobId.UpdateSummary,
        GenerationJobId.BootstrapLore,
        GenerationJobId.ExpandStoryCard,
        GenerationJobId.BootstrapSections,
        GenerationJobId.ExpandSection,
        GenerationJobId.ContinuityCheck,
        GenerationJobId.ProposeSourceEdits,
        GenerationJobId.ProposeJsonImport,
    ];

    public static bool IsComparablePlayAiTool(string jobId) =>
        ComparablePlayAiToolJobIds.Contains(jobId, StringComparer.OrdinalIgnoreCase);

    public static bool HasInstructionGuide(AdventureBundle bundle, string jobId)
    {
        var body = GenerationJobGuideService.ResolveInstructionBody(bundle, jobId);
        return !string.IsNullOrWhiteSpace(body);
    }

    /// <summary>Job packet shared by both inference legs (story context, scope, overrides — no guide duplication).</summary>
    public static string BuildCoreJobBody(
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext context)
    {
        var promptContext = CloneForPrompt(context);
        promptContext.SuppressInlineGuide = true;
        return GenerationJobHandlers.BuildJobPrompt(bundle, jobId, promptContext);
    }

    /// <summary>Local LLM job packet — omits heavy canon-format reference blocks that steer small models off-contract.</summary>
    public static string BuildLocalCoreJobBody(
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext context)
    {
        var promptContext = CloneForPrompt(context);
        promptContext.SuppressInlineGuide = true;
        promptContext.ForLocalInference = true;
        return GenerationJobHandlers.BuildJobPrompt(bundle, jobId, promptContext);
    }

    public static string BuildRemoteWorkerPacket(
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext context,
        Guid? runId = null)
    {
        var core = BuildCoreJobBody(bundle, jobId, context);
        var withContract = UtilityResponseSchemaRegistry.AppendResponseContract(core, jobId);
        return ContextTagFormat.WrapUtilityJob(jobId, withContract, "worker", runId);
    }

    public static (string SystemInstruction, string UserPacket) BuildLocalInferencePrompts(
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext context)
    {
        var system = BuildLocalSystemInstruction(bundle, jobId);
        var core = BuildLocalCoreJobBody(bundle, jobId, context);
        var user = AppendLocalResponseContract(core, jobId);
        return (system.Trim(), user);
    }

    private static string BuildLocalSystemInstruction(AdventureBundle bundle, string jobId)
    {
        var body = GenerationJobGuideService.ResolveInstructionBody(bundle, jobId);
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException(
                $"Missing instruction guide for job '{jobId}'. Add a default in {nameof(GenerationJobGuideService)}.");
        }

        return $"""
            {body.Trim()}

            You are running in local exploratory mode for side-by-side comparison with ChatGPT utility jobs.
            Follow the RESPONSE FORMAT block in the user message exactly — the wrapper will not parse alternate shapes.
            """;
    }

    internal static string AppendLocalResponseContract(string jobBody, string jobId) =>
        $"""
        {jobBody}

        === RESPONSE FORMAT ===
        {DescribeLocalResponseFormat(jobId)}
        """;

    internal static string DescribeLocalResponseFormat(string jobId)
    {
        if (GenerationJobHandlers.ExpectsPlainTextResponse(jobId))
            return "Reply with plain text only — no markdown fences or commentary.";

        if (GenerationJobHandlers.ExpectsJsonObjectResponse(jobId))
        {
            return jobId switch
            {
                GenerationJobId.ProcessTurn =>
                    """
                    Reply with one JSON object only — no markdown fences.
                    Example: {"memories":[{"text":"…","tags":[],"pinned":false}],"entities":[{"name":"…","entityType":"person","description":"…"}],"summary":"…"}
                    Include only the keys requested in the job packet.
                    """,
                GenerationJobId.ContinuityCheck =>
                    """
                    Reply with one JSON object only — no markdown fences.
                    Example: {"warnings":[{"message":"…","severity":"warning"}]}
                    """,
                _ =>
                    "Reply with valid JSON only (single object with the required keys) — no markdown fences or commentary.",
            };
        }

        if (GenerationJobHandlers.ExpectsJsonArrayResponse(jobId))
        {
            var wrapper = ResolveLocalArrayWrapperKey(jobId);
            return jobId switch
            {
                GenerationJobId.BootstrapSections or GenerationJobId.ExpandSection =>
                    $"""
                    Reply with one JSON object only — no markdown fences.
                    Wrap the entity array under "{wrapper}".
                    {BuildEntityArrayExample(wrapper)}
                    Do not return labeled canon field sheets (Relationship, Secrets, Setting, Role, …).
                    """,
                GenerationJobId.ProposeMemories =>
                    $"""
                    Reply with one JSON object only — no markdown fences.
                    Wrap the memory array under "{wrapper}".
                    {BuildMemoryArrayExample(wrapper)}
                    Return an empty "{wrapper}" array when nothing is worth recording.
                    """,
                GenerationJobId.BootstrapLore or GenerationJobId.ExpandStoryCard =>
                    $"""
                    Reply with one JSON object only — no markdown fences.
                    Wrap the card array under "{wrapper}".
                    {BuildCardArrayExample(wrapper)}
                    """,
                _ =>
                    $"""
                    Reply with one JSON object only — no markdown fences.
                    Wrap the result array under "{wrapper}" using the field names from the job packet.
                    """,
            };
        }

        return "Follow the job packet exactly — no markdown fences or commentary.";
    }

    internal static string DescribeResponseFormat(string jobId) =>
        GenerationJobHandlers.ExpectsJsonObjectResponse(jobId)
            ? "Reply with valid JSON only (single object with the required keys) — no markdown fences or commentary."
            : GenerationJobHandlers.ExpectsJsonArrayResponse(jobId)
                ? "Reply with valid JSON only (array or object as specified above) — no markdown fences or commentary."
                : "Reply with plain text only — no markdown fences or commentary.";

    internal static bool UsesStructuredJsonResponse(string jobId) =>
        GenerationJobHandlers.ExpectsJsonObjectResponse(jobId)
        || GenerationJobHandlers.ExpectsJsonArrayResponse(jobId);

    internal static string ResolveLocalArrayWrapperKey(string jobId) => jobId switch
    {
        GenerationJobId.ProposeMemories => "memories",
        GenerationJobId.BootstrapLore or GenerationJobId.ExpandStoryCard => "items",
        GenerationJobId.ProposeSourceEdits => "proposals",
        _ => "entities",
    };

    private static string BuildEntityArrayExample(string wrapper) =>
        $"Example: {{\"{wrapper}\":[{{\"name\":\"Greyford Gate\",\"entityType\":\"place\",\"description\":\"…\",\"aliases\":[\"gate\"]}}]}}";

    private static string BuildMemoryArrayExample(string wrapper) =>
        $"Example: {{\"{wrapper}\":[{{\"text\":\"…\",\"tags\":[],\"pinned\":false}}]}}";

    private static string BuildCardArrayExample(string wrapper) =>
        $"Example: {{\"{wrapper}\":[{{\"name\":\"…\",\"type\":\"Place\",\"triggers\":[\"gate\"],\"content\":\"…\",\"enabled\":true}}]}}";

    private static GenerationJobContext CloneForPrompt(GenerationJobContext context) =>
        new()
        {
            Turn = context.Turn,
            Scope = context.Scope,
            CardId = context.CardId,
            EntityId = context.EntityId,
            EntityKind = context.EntityKind,
            ForceRotate = context.ForceRotate,
            UserPrompt = context.UserPrompt,
            ProcessTurnIncludeMemories = context.ProcessTurnIncludeMemories,
            ProcessTurnIncludeEntities = context.ProcessTurnIncludeEntities,
            ProcessTurnIncludeSummary = context.ProcessTurnIncludeSummary,
            StoryContextBlock = context.StoryContextBlock,
            StoryContextHasTranscript = context.StoryContextHasTranscript,
            OmitRedundantJobTurnSlices = context.OmitRedundantJobTurnSlices,
            StoryContextIncludesSummary = context.StoryContextIncludesSummary,
            StoryContextIncludesState = context.StoryContextIncludesState,
            SuppressInlineGuide = context.SuppressInlineGuide,
            UtilityContextAssembled = context.UtilityContextAssembled,
            UtilityContextManifest = context.UtilityContextManifest,
            DesignStep = context.DesignStep,
            ForLocalInference = context.ForLocalInference,
            JobAttachments = context.JobAttachments,
            AttachmentReferenceNote = context.AttachmentReferenceNote,
        };
}
