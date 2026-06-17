using System.Security.Cryptography;
using System.Text;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class GenerationJobGuideService
{
    public const int MemoryProposeSeedVersion = 2;
    public const int SummaryUpdateSeedVersion = 1;
    public const int LoreSeedVersion = 1;
    public const int ContinuitySeedVersion = 1;
    public const int ProcessTurnSeedVersion = 1;
    public const int SourceEditSeedVersion = 1;
    public const int JsonImportSeedVersion = 3;
    public const int DesignAdventureSeedVersion = 1;
    public const int DesignExtractSeedVersion = 1;

    public static string GetGuideSyncKey(string jobId) =>
        GenerationJobHandlers.GetUtilityJobId(jobId);

    public static int GetSeedVersion(string jobId) => jobId switch
    {
        GenerationJobId.ProcessTurn => ProcessTurnSeedVersion,
        GenerationJobId.ExtractEntities or GenerationJobId.ExpandEntity => EntityExtractionService.SeedVersion,
        GenerationJobId.ProposeMemories => MemoryProposeSeedVersion,
        GenerationJobId.UpdateSummary => SummaryUpdateSeedVersion,
        GenerationJobId.BootstrapLore or GenerationJobId.ExpandStoryCard => LoreSeedVersion,
        GenerationJobId.BootstrapSections or GenerationJobId.ExpandSection => LoreSeedVersion,
        GenerationJobId.ContinuityCheck => ContinuitySeedVersion,
        GenerationJobId.ProposeSourceEdits => SourceEditSeedVersion,
        GenerationJobId.ProposeJsonImport => JsonImportSeedVersion,
        GenerationJobId.DesignAdventure => DesignAdventureSeedVersion,
        GenerationJobId.DesignExtractStep => DesignExtractSeedVersion,
        _ => 1,
    };

    public static string BuildDefaultInstructionBody(string jobId) => jobId switch
    {
        GenerationJobId.ProcessTurn => """
            You run bundled post-exchange utility jobs for interactive fiction.
            Respond with JSON only — one object with optional keys: memories (array), entities (array), summary (string).
            memories: event proposals for the scoped exchange only — { text, tags, pinned, anchor: { pairOffset, playerHint }, outcome? }.
            entities: world-model referents — { entityType, name, description, roleOrStatus?, category?, action? }.
            summary: plain rolling digest text when requested.
            Do not restate entity definitions as events or vice versa.
            """,
        GenerationJobId.ExtractEntities or GenerationJobId.ExpandEntity => EntityExtractionService.BuildGuideInstructionBody(),
        GenerationJobId.ProposeMemories => """
            You propose discrete story events from the scoped play exchange.
            Events are things that happened — not standing world-model definitions (those are entities).
            Respond with JSON only — array of { text, tags, pinned, anchor: { pairOffset, playerHint }, outcome? }.
            If nothing worth recording, return [].
            """,
        GenerationJobId.UpdateSummary => """
            You update the rolling story digest for interactive fiction.
            Respond with plain summary text only — no markdown fences or commentary.
            Preserve major events, relationships, conflicts, and consequences.
            """,
        GenerationJobId.BootstrapLore or GenerationJobId.ExpandStoryCard => """
            You generate story cards for interactive fiction — keyword-triggered lore snippets injected into narrator prompts when triggers match player input.
            This is NOT the entity index (people/places/things/concepts tracked for reference). Cards are compact, triggerable lore blocks.
            Respond with JSON only — array of { "name", "type", "triggers": string[], "content", "enabled": true }.
            Types: Character, Place, Faction, Item, Rule, Creature, Organization, Lore.
            """,
        GenerationJobId.BootstrapSections or GenerationJobId.ExpandSection => """
            You generate canon entity sections for interactive fiction — stable records exported to cast.md / world.md / plot.md.
            Respond with JSON only — array of { "name", "entityType": "person"|"place"|"concept"|"faction", "description", "aliases": string[], "flavor": string }.
            Aliases are surface forms used for context matching. Flavor is optional evocative detail.
            """,
        GenerationJobId.ContinuityCheck => """
            You check narrative continuity across transcript, summary, entities, and state.
            Respond with JSON only: { "warnings": [ { "message": string, "severity": "info"|"warning"|"high" } ] }.
            """,
        GenerationJobId.ProposeSourceEdits => """
            You propose edits to adventure source files and narrator instructions.
            Respond with JSON only — array of objects:
            { "targetFile": "world.md"|"plot.md"|"scenario.md"|"instructions", "operation": "replace"|"append", "content": string, "rationale": string }.
            Do not invent facts that contradict provided excerpts.
            """,
        GenerationJobId.ProposeJsonImport => """
            Parts 1–2 are required in one reply (downloadable files plus matching inline begin/end blocks).
            Part 3 is optional plain JSON (no markdown fences) — if omitted, the wrapper diffs your proposed files.
            scenarioFields: array of { "field": string, "value": string, "rationale": string }.
            entities: array of { "action": "add"|"update"|"remove", "name": string, "entityType": "person"|"place"|"concept"|"faction", "description": string, "rationale": string }.
            Allowed field keys: setting, playerRole, genre, tone, openingSituation, majorConflicts, startingConstraints, plotEssentials, worldRules, authorsNote, lexiconRules, lexiconPools, lexiconAvoid.
            Every rationale MUST cite supporting sourceRef value(s) from the job packet verbatim (e.g. "plot.md#essentials").
            Derive values from the referenced source material; do not invent facts absent from cited sources.
            For remove actions, description may be omitted. If nothing to propose, return { "scenarioFields": [], "entities": [] }.
            """,
        GenerationJobId.DesignAdventure => """
            You help design a new interactive fiction adventure before play begins.
            Work step-by-step with the author: ask clarifying questions, propose ideas, and refine drafts.
            Do not start narrating play — this is pre-production design only.
            When asked to extract or finalize, follow the job packet format exactly.
            """,
        GenerationJobId.DesignExtractStep => """
            You extract structured design fields from the design conversation and current draft.
            Respond with JSON only — no markdown fences or commentary.
            """,
        _ => "",
    };

    public static string ResolveInstructionBody(AdventureBundle bundle, string jobId)
    {
        var key = GetGuideSyncKey(jobId);
        bundle.Metadata.UtilityJobGuideOverrides ??=
            new Dictionary<string, UtilityJobGuideOverride>(StringComparer.OrdinalIgnoreCase);

        if (bundle.Metadata.UtilityJobGuideOverrides.TryGetValue(key, out var over)
            && !string.IsNullOrWhiteSpace(over.InstructionBody))
            return over.InstructionBody.Trim();

        return BuildDefaultInstructionBody(jobId);
    }

    public static bool IsUsingDefaultInstruction(AdventureBundle bundle, string jobId)
    {
        var key = GetGuideSyncKey(jobId);
        bundle.Metadata.UtilityJobGuideOverrides ??=
            new Dictionary<string, UtilityJobGuideOverride>(StringComparer.OrdinalIgnoreCase);

        if (!bundle.Metadata.UtilityJobGuideOverrides.TryGetValue(key, out var over))
            return true;

        var custom = over.InstructionBody?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(custom))
            return true;

        return string.Equals(custom, BuildDefaultInstructionBody(jobId).Trim(), StringComparison.Ordinal);
    }

    public static int GetEffectiveSeedVersion(AdventureBundle bundle, string jobId)
    {
        if (IsUsingDefaultInstruction(bundle, jobId))
            return GetSeedVersion(jobId);

        return StableInstructionHash(ResolveInstructionBody(bundle, jobId));
    }

    public static void SetInstructionOverride(AdventureBundle bundle, string jobId, string body)
    {
        var key = GetGuideSyncKey(jobId);
        var trimmed = body.Trim();
        var defaultBody = BuildDefaultInstructionBody(jobId).Trim();

        bundle.Metadata.UtilityJobGuideOverrides ??=
            new Dictionary<string, UtilityJobGuideOverride>(StringComparer.OrdinalIgnoreCase);

        if (string.Equals(trimmed, defaultBody, StringComparison.Ordinal))
            bundle.Metadata.UtilityJobGuideOverrides.Remove(key);
        else
            bundle.Metadata.UtilityJobGuideOverrides[key] = new UtilityJobGuideOverride { InstructionBody = trimmed };
    }

    public static void ResetInstructionOverride(AdventureBundle bundle, string jobId)
    {
        var key = GetGuideSyncKey(jobId);
        bundle.Metadata.UtilityJobGuideOverrides?.Remove(key);
    }

    public static string GetDisplayLabel(string jobId) => jobId switch
    {
        GenerationJobId.ProcessTurn => "Process exchange (AI)",
        GenerationJobId.ExtractEntities => "Entities (AI)",
        GenerationJobId.ExpandEntity => "Expand entity (AI)",
        GenerationJobId.ProposeMemories => "Memories (AI)",
        GenerationJobId.UpdateSummary => "Story digest (AI)",
        GenerationJobId.BootstrapLore => "Cards (AI)",
        GenerationJobId.ContinuityCheck => "Continuity (AI)",
        GenerationJobId.ProposeSourceEdits => "Source edits (AI)",
        GenerationJobId.ProposeJsonImport => "JSON import (AI)",
        GenerationJobId.DesignAdventure => "Adventure design (AI)",
        GenerationJobId.DesignExtractStep => "Design extract (AI)",
        _ => jobId,
    };

    public static IReadOnlyList<string> EditableUtilityJobIds { get; } =
    [
        GenerationJobId.ProcessTurn,
        GenerationJobId.ExtractEntities,
        GenerationJobId.ProposeMemories,
        GenerationJobId.UpdateSummary,
        GenerationJobId.BootstrapLore,
        GenerationJobId.ContinuityCheck,
        GenerationJobId.ProposeSourceEdits,
        GenerationJobId.ProposeJsonImport,
        GenerationJobId.DesignAdventure,
        GenerationJobId.DesignExtractStep,
    ];

    private static int StableInstructionHash(string body)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body.Trim()));
        return Math.Abs(BitConverter.ToInt32(bytes, 0));
    }
}
