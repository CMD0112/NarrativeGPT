namespace ChatGPTWrapper.Adventure.Models;

internal static class GenerationJobId
{
    public const string ProcessTurn = "process_turn";
    public const string ExtractEntities = "extract_entities";
    public const string ExpandEntity = "expand_entity";
    public const string ProposeMemories = "propose_memories";
    public const string UpdateSummary = "update_summary";
    public const string BootstrapLore = "bootstrap_lore";
    public const string ExpandStoryCard = "expand_story_card";
    public const string BootstrapSections = "bootstrap_sections";
    public const string ExpandSection = "expand_section";
    public const string ContinuityCheck = "continuity_check";
    public const string ProposeSourceEdits = "propose_source_edits";

    public const string ProposeJsonImport = "propose_json_import";

    public const string DraftFramework = "draft_framework";

    public const string DesignAdventure = "design_adventure";

    public const string DesignExtractStep = "design_extract_step";

    public const string SynthesizeSource = "synthesize_source";

    /// <summary>Capability probe for utility worker lane registration.</summary>
    public const string UtilityWorkerPing = "utility_worker_ping";

    /// <summary>Obsolete — recap UI uses local digest formatting only.</summary>
    public const string GenerateRecap = "generate_recap";

    public static IReadOnlyList<string> All { get; } =
    [
        ProcessTurn,
        ExtractEntities,
        ExpandEntity,
        ProposeMemories,
        UpdateSummary,
        BootstrapLore,
        ExpandStoryCard,
        BootstrapSections,
        ExpandSection,
        ContinuityCheck,
        ProposeSourceEdits,
        ProposeJsonImport,
    ];
}
