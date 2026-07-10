using System.IO;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class UtilityJobAttachmentLaunchService
{
    public static IReadOnlyList<string> GetSuggestedPaths(AdventureBundle bundle, string jobId)
    {
        var adventureDir = AppDirectories.AdventureDirectory(bundle.Metadata.Id);
        return jobId switch
        {
            GenerationJobId.ExtractEntities or GenerationJobId.ExpandEntity =>
            [
                Path.Combine(adventureDir, "entities.json"),
                Path.Combine(adventureDir, "scenario.json"),
            ],
        GenerationJobId.ProposeEntitiesFile =>
        [
            Path.Combine(adventureDir, "entities.json"),
        ],
            GenerationJobId.ProposeJsonImport =>
            [
                Path.Combine(adventureDir, "scenario.json"),
                Path.Combine(adventureDir, "entities.json"),
            ],
            GenerationJobId.ProposeSourceEdits or GenerationJobId.SynthesizeSource =>
                Directory.Exists(Path.Combine(adventureDir, "sources"))
                    ? Directory.GetFiles(Path.Combine(adventureDir, "sources"), "*.md", SearchOption.TopDirectoryOnly)
                        .Take(3)
                        .ToList()
                    : [],
            GenerationJobId.ContinuityCheck =>
            [
                Path.Combine(adventureDir, "scenario.json"),
                Path.Combine(adventureDir, "state.json"),
            ],
            _ => [],
        };
    }

    public static string GetDefaultReferenceNote(string jobId) => jobId switch
    {
        GenerationJobId.ExtractEntities =>
            """
            Reference files are published to Project sources under the canonical cgw-utility-io path; retrieve via TASK-SCOPED pointer — do not rely on composer attachments for entities.json or scenario.json.
            Use the published entities.json as the canonical schema and id reference.
            Propose only new entities or updates not already present; preserve existing ids and names where they match.
            """,
        GenerationJobId.ExpandEntity =>
            "Reference files are published to Project sources under the canonical cgw-utility-io path; retrieve via TASK-SCOPED pointer — do not rely on composer attachments for entities.json.",
        GenerationJobId.ProposeEntitiesFile =>
            "Input is published to Project sources under the canonical cgw-utility-io path; retrieve via TASK-SCOPED pointer — do not rely on composer attachments for entities.json.",
        GenerationJobId.ProposeJsonImport =>
            "Use attached scenario.json / entities.json as the current canon baseline when proposing import changes.",
        GenerationJobId.ProposeSourceEdits =>
            "Use attached source or JSON files as reference when proposing markdown edits.",
        GenerationJobId.ContinuityCheck =>
            "Use attached scenario/state JSON as authoritative canon when checking continuity.",
        _ =>
            "Use the attached file(s) as reference material for this job. Do not ignore them in favor of duplicating long context already in the packet.",
    };

    public static GenerationJobContext ApplyLaunch(
        GenerationJobContext? context,
        UtilityJobAttachmentLaunchResult launch)
    {
        var baseContext = context ?? new GenerationJobContext();
        return new GenerationJobContext
        {
            Turn = baseContext.Turn,
            Scope = baseContext.Scope,
            CardId = baseContext.CardId,
            EntityId = baseContext.EntityId,
            EntityKind = baseContext.EntityKind,
            ForceRotate = baseContext.ForceRotate,
            UserPrompt = baseContext.UserPrompt,
            ProcessTurnIncludeMemories = baseContext.ProcessTurnIncludeMemories,
            ProcessTurnIncludeEntities = baseContext.ProcessTurnIncludeEntities,
            ProcessTurnIncludeSummary = baseContext.ProcessTurnIncludeSummary,
            StoryContextBlock = baseContext.StoryContextBlock,
            StoryContextHasTranscript = baseContext.StoryContextHasTranscript,
            OmitRedundantJobTurnSlices = baseContext.OmitRedundantJobTurnSlices,
            StoryContextIncludesSummary = baseContext.StoryContextIncludesSummary,
            StoryContextIncludesState = baseContext.StoryContextIncludesState,
            SuppressInlineGuide = baseContext.SuppressInlineGuide,
            UtilityContextAssembled = baseContext.UtilityContextAssembled,
            UtilityContextManifest = baseContext.UtilityContextManifest,
            DesignStep = baseContext.DesignStep,
            InferenceSource = baseContext.InferenceSource,
            UtilityRunId = baseContext.UtilityRunId,
            DualRunGroupId = baseContext.DualRunGroupId,
            AllowCrossSourceDuplicates = baseContext.AllowCrossSourceDuplicates,
            ForLocalInference = baseContext.ForLocalInference,
            JobAttachments = launch.HasAttachments
                ? AttachmentContext.FromMeta(launch.Attachments.Select(a => new ComposerAttachmentMeta
                {
                    Name = a.Name,
                    MimeType = a.MimeType,
                    SizeBytes = a.Content.Length,
                }))
                : baseContext.JobAttachments,
            AttachmentReferenceNote = launch.ReferenceNote ?? baseContext.AttachmentReferenceNote,
        };
    }
}

internal sealed class UtilityJobAttachmentLaunchResult
{
    public IReadOnlyList<DomAttachmentPayload> Attachments { get; init; } = [];

    public string? ReferenceNote { get; init; }

    public bool HasAttachments => Attachments.Count > 0;
}
