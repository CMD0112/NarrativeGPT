using System.Security.Cryptography;
using System.Text;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.Adventure.Services;

internal static class GenerationJobGuideService
{
    public const int MemoryProposeSeedVersion = 2;
    public const int SummaryUpdateSeedVersion = 1;
    public const int LoreSeedVersion = 1;
    public const int ContinuitySeedVersion = 1;
    public const int ProcessTurnSeedVersion = 1;
    public const int StateUpdateSeedVersion = 1;
    public const int SourceEditSeedVersion = 1;
    public const int JsonImportSeedVersion = 4;
    public const int DesignAdventureSeedVersion = 1;
    public const int DesignExtractSeedVersion = 1;

    public static string GetGuideSyncKey(string jobId) =>
        GenerationJobHandlers.GetUtilityJobId(jobId);

    public static int GetSeedVersion(string jobId) => jobId switch
    {
        GenerationJobId.ProcessTurn => ProcessTurnSeedVersion,
        GenerationJobId.ExtractEntities or GenerationJobId.ExpandEntity => EntityExtractionService.SeedVersion,
        GenerationJobId.ProposeEntityState => EntityInternalStateProposalService.SeedVersion,
        GenerationJobId.ProposeCanonEvolution => EntityCanonEvolutionProposalService.SeedVersion,
        GenerationJobId.ProposeEntitiesFile => EntitiesFileRevisionService.SeedVersion,
        GenerationJobId.ProposeMemories => MemoryProposeSeedVersion,
        GenerationJobId.UpdateState => StateUpdateSeedVersion,
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
            Respond with JSON only — one object with optional keys: memories (array), entities (object).
            memories: event proposals for the scoped exchange only — { text, tags, pinned, anchor: { pairOffset, playerHint }, outcome? }.
            entities: world-model referents — { extractions: [...], updates: [...] }.
            Do not restate entity definitions as events or vice versa.
            """,
        GenerationJobId.ExtractEntities or GenerationJobId.ExpandEntity => EntityExtractionService.BuildGuideInstructionBody(),
        GenerationJobId.ProposeEntityState => EntityInternalStateProposalService.BuildGuideInstructionBody(),
        GenerationJobId.ProposeCanonEvolution => EntityCanonEvolutionProposalService.BuildGuideInstructionBody(),
        GenerationJobId.ProposeEntitiesFile => BuildProposeEntitiesFileInstructionBody(),
        GenerationJobId.ProposeMemories => """
            You propose discrete story events from the scoped play exchange.
            Events are things that happened — not standing world-model definitions (those are entities).
            Respond with JSON only — object:
            { "events": [ { text, tags, pinned, anchor: { pairOffset, playerHint }, outcome? } ], "links": [ ...optional memory links... ] }.
            If nothing worth recording, return { "events": [], "links": [] }.
            """,
        GenerationJobId.UpdateState => """
            You propose session-state deltas from scoped play context.
            Respond with JSON only:
            { "location"?: string, "objectives"?: string[], "objectivesRemove"?: string[], "flags"?: object, "time"?: string, "rationale"?: string }.
            Omit unchanged keys. Use objectivesRemove only for explicitly completed/abandoned objectives.
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
            Respond with JSON only: { "warnings": [ { "message": string, "severity": "info"|"warning"|"high", "category": string, "refs": string[] } ] }.
            """,
        GenerationJobId.ResolveContinuityWarning => """
            You resolve one continuity warning by proposing minimal fixes.
            Respond with JSON object with optional keys:
            - entities: { updates: [...] }
            - state: { location, objectives, objectivesRemove, flags, time, rationale }
            """,
        GenerationJobId.AuditCanon => """
            You audit canon consistency across scenario, entities, context-index, and source files.
            Respond with JSON only: { "warnings": [ { "message": string, "severity": "info"|"warning"|"high", "category": string, "refs": string[] } ] }.
            """,
        GenerationJobId.RefreshContextIndex => """
            This job is wrapper-side rule-based context index refresh.
            Respond with {}.
            """,
        GenerationJobId.ProposeSourceEdits => """
            You propose edits to adventure source files and narrator instructions.
            Respond with JSON only — array of objects:
            { "targetFile": "world.md"|"plot.md"|"scenario.md"|"cast.md"|"instructions", "operation": "replace"|"append", "content": string, "rationale": string }.
            Follow section templates in sources/canon-format.md when editing cast, world, or plot entries (labeled fields, Id slugs, party vs npc buckets).
            Do not invent facts that contradict provided excerpts.
            """,
        GenerationJobId.ProposeJsonImport => ProposeJsonImportInstructionBody(),
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
        GenerationJobId.ProposeEntitiesFile => "Entities file (AI)",
        GenerationJobId.ExpandEntity => "Expand entity (AI)",
        GenerationJobId.ProposeEntityState => "Entity state (AI)",
        GenerationJobId.ProposeCanonEvolution => "Canon evolution (AI)",
        GenerationJobId.ProposeMemories => "Memories (AI)",
        GenerationJobId.UpdateState => "Session state (AI)",
        GenerationJobId.UpdateSummary => "Story digest (AI)",
        GenerationJobId.BootstrapLore => "Cards (AI)",
        GenerationJobId.ExpandStoryCard => "Expand card (AI)",
        GenerationJobId.BootstrapSections => "Canon sections (AI)",
        GenerationJobId.ExpandSection => "Expand section (AI)",
        GenerationJobId.ContinuityCheck => "Continuity (AI)",
        GenerationJobId.ResolveContinuityWarning => "Resolve warning (AI)",
        GenerationJobId.AuditCanon => "Audit canon (AI)",
        GenerationJobId.RefreshContextIndex => "Refresh context index (AI)",
        GenerationJobId.ProposeSourceEdits => "Source edits (AI)",
        GenerationJobId.ProposeJsonImport => "JSON import (AI)",
        GenerationJobId.DesignAdventure => "Adventure design (AI)",
        GenerationJobId.DesignExtractStep => "Design extract (AI)",
        _ => jobId,
    };

    public static IReadOnlyList<string> EditablePlayUtilityJobIds { get; } =
    [
        GenerationJobId.ProcessTurn,
        GenerationJobId.ExtractEntities,
        GenerationJobId.ProposeMemories,
        GenerationJobId.UpdateState,
        GenerationJobId.UpdateSummary,
        GenerationJobId.ContinuityCheck,
        GenerationJobId.ProposeEntityState,
        GenerationJobId.ProposeCanonEvolution,
    ];

    public static IReadOnlyList<string> EditableDesignUtilityJobIds { get; } =
    [
        GenerationJobId.DesignAdventure,
        GenerationJobId.DesignExtractStep,
        GenerationJobId.DraftFramework,
        GenerationJobId.ProposeJsonImport,
        GenerationJobId.ProposeSourceEdits,
        GenerationJobId.ProposeEntitiesFile,
        GenerationJobId.BootstrapSections,
        GenerationJobId.ExpandSection,
        GenerationJobId.AuditCanon,
    ];

    public static IReadOnlyList<string> EditableUtilityJobIds { get; } =
    [
        ..EditablePlayUtilityJobIds,
        ..EditableDesignUtilityJobIds,
    ];

    public static string GetCatalogCategory(string jobId) => jobId switch
    {
        GenerationJobId.ProcessTurn or GenerationJobId.ProposeMemories or GenerationJobId.UpdateSummary
            or GenerationJobId.ContinuityCheck or GenerationJobId.ResolveContinuityWarning => "Narrative",
        GenerationJobId.UpdateState => "Session",
        GenerationJobId.ExtractEntities => "Canon profile",
        GenerationJobId.ProposeEntityState => "Play state",
        GenerationJobId.ProposeCanonEvolution => "Canon evolution",
        GenerationJobId.ProposeEntitiesFile => "File I/O",
        GenerationJobId.BootstrapLore or GenerationJobId.ProposeSourceEdits or GenerationJobId.ProposeJsonImport
            or GenerationJobId.AuditCanon or GenerationJobId.RefreshContextIndex
            => "World & canon",
        GenerationJobId.DesignAdventure or GenerationJobId.DesignExtractStep => "Pre-play design",
        _ => "Other",
    };

    /// <summary>Sort order for play utility job catalog and automation grids (narrative → session → canon).</summary>
    public static int GetLayerSortOrder(string layer) => layer switch
    {
        "Narrative" => 0,
        "Session" => 1,
        "Canon profile" => 2,
        "Play state" => 3,
        "Canon evolution" => 4,
        _ => 99,
    };

    /// <summary>One-line author hint for each utility job layer.</summary>
    public static string DescribeUtilityLayer(string layer) => layer switch
    {
        "Narrative" =>
            "Story-wide artifacts — rolling digest, discrete memories, continuity warnings.",
        "Session" =>
            "Scene snapshot — location, objectives, flags, and time (not entity internal state).",
        "Canon profile" =>
            "Durable entity definitions — creates and profile updates in entities.json.",
        "Play state" =>
            "Live entity internal state — disposition, mood, trust, location, quest progress, etc.",
        "Canon evolution" =>
            "Promote play divergences into durable canon when state and profile disagree.",
        _ => "",
    };

    public static string GetCatalogDescription(string jobId) => jobId switch
    {
        GenerationJobId.ProcessTurn =>
            "Bundled memories + entities for the latest play exchange. Returns JSON proposals for review.",
        GenerationJobId.ExtractEntities =>
            "Extract world-model referents (people, places, concepts) from scoped context.",
        GenerationJobId.ProposeEntitiesFile =>
            "Revise entities.json via Project source publish → pointer → scrape → delete (ephemeral lane).",
        GenerationJobId.DraftFramework =>
            "Draft initial adventure framework sections and source-ready markdown outlines.",
        GenerationJobId.ProposeMemories =>
            "Propose discrete story events from the scoped exchange — not standing entity definitions.",
        GenerationJobId.UpdateState =>
            "Propose structured state deltas (location, objectives, flags, time) for review.",
        GenerationJobId.UpdateSummary =>
            "Refresh the rolling story digest used in play packets.",
        GenerationJobId.BootstrapLore =>
            "Generate keyword-triggered lore cards (not the entity index).",
        GenerationJobId.BootstrapSections =>
            "Generate canon section entity records from scenario context.",
        GenerationJobId.ExpandSection =>
            "Expand one canon section with richer entity detail.",
        GenerationJobId.ContinuityCheck =>
            "Check narrative continuity across transcript, summary, entities, and state.",
        GenerationJobId.ProposeEntityState =>
            "Propose live entity internal-state deltas (disposition, mood, location, etc.) for review before apply.",
        GenerationJobId.ProposeCanonEvolution =>
            "Propose durable canon profile updates when play state diverges from entity definitions.",
        GenerationJobId.ResolveContinuityWarning =>
            "Compose targeted entity/state fixes for a selected continuity warning.",
        GenerationJobId.AuditCanon =>
            "Design-time canon consistency audit across scenario, entities, and sources.",
        GenerationJobId.RefreshContextIndex =>
            "Rule-based refresh of context-index triggers from accepted entities.",
        GenerationJobId.ProposeSourceEdits =>
            "Propose edits to canon source files (world, plot, cast, instructions).",
        GenerationJobId.ProposeJsonImport =>
            "Import structured scenario fields and entities from source excerpts.",
        GenerationJobId.DesignAdventure =>
            "Pre-play design conversation on the pinned design thread.",
        GenerationJobId.DesignExtractStep =>
            "Extract structured design fields from the design conversation.",
        _ => "",
    };

    private static string BuildProposeEntitiesFileInstructionBody() =>
        """
        You revise the adventure entities.json world-model index from scoped play context.
        Entities are durable referents (people, places, things, factions, quests, concepts) — not play-by-play events.
        Retrieve the published Project source entities.json baseline before editing.
        Respond with a complete revised entities.json file (downloadable + matching inline begin/end block).
        Prefer updates over duplicates when an entity clearly matches the baseline. Preserve unrelated ids and entries.
        If nothing new or changed, return the baseline document unchanged.
        """;

    private static string ProposeJsonImportInstructionBody()
    {
        var castFields = CanonFieldReferenceService.BuildPromptCastFieldSummary();
        return $$"""
            Parts 1–2 are required in one reply (downloadable files plus matching inline begin/end blocks).
            Part 3 is optional plain JSON (no markdown fences) — if omitted, the wrapper diffs your proposed files.
            scenarioFields: array of { "field": string, "value": string, "rationale": string }.
            entities: array of { "action": "add"|"update"|"remove", "name": string, "entityType": "person"|"place"|"concept"|"faction", "description": string, "rationale": string }.
            Allowed field keys: setting, playerRole, genre, tone, openingSituation, majorConflicts, startingConstraints, plotEssentials, worldRules, authorsNote, lexiconRules, lexiconPools, lexiconAvoid.
            Every rationale MUST cite supporting sourceRef value(s) from the job packet verbatim (e.g. "plot.md#essentials").
            Use sources/canon-format.md and typed cast field labels when mapping cast.md party/npc entries to entities.json.
            Cast typed fields: {{castFields}}
            Custom attributes without a typed field may appear in entity extendedFields (key = label, value = text) in proposed markdown or JSON patches.
            Derive values from the referenced source material; do not invent facts absent from cited sources.
            For remove actions, description may be omitted. If nothing to propose, return { "scenarioFields": [], "entities": [] }.
            """;
    }

    private static int StableInstructionHash(string body)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body.Trim()));
        return Math.Abs(BitConverter.ToInt32(bytes, 0));
    }
}
