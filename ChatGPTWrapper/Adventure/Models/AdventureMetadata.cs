using System.Text.Json.Serialization;

namespace ChatGPTWrapper.Adventure.Models;

public sealed class AdventureMetadata
{
    public int SchemaVersion { get; set; } = AdventureJson.SchemaVersion;

    /// <summary>Canon schema registry version applied to this adventure (CMD-205).</summary>
    public int CanonSchemaVersion { get; set; }

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = "Untitled adventure";

    public string Genre { get; set; } = "";

    public string ScenarioSummary { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastPlayedAt { get; set; } = DateTimeOffset.UtcNow;

    public AdventureStatus Status { get; set; } = AdventureStatus.Active;

    public bool Archived { get; set; }

    public List<string> Tags { get; set; } = [];

    public string? LinkedConversationId { get; set; }

    /// <summary>ChatGPT Project (gizmo) id from backend-api.</summary>
    public string? LinkedProjectId { get; set; }

    public string? LinkedProjectHint { get; set; }

    /// <summary>Stable TabItem key for the pinned ChatGPT play tab.</summary>
    public string? PinnedPlayTabKey { get; set; }

    public string? PinnedPlayTabTitle { get; set; }

    /// <summary>Last known ChatGPT URL for the pinned play tab (restored across app restarts).</summary>
    public string? PinnedPlayTabUrl { get; set; }

    /// <summary>Stable TabItem key for the pinned utility (AI jobs) ChatGPT tab.</summary>
    public string? PinnedUtilityTabKey { get; set; }

    public string? PinnedUtilityTabTitle { get; set; }

    /// <summary>Stable TabItem key for the pinned adventure-design ChatGPT tab.</summary>
    public string? PinnedDesignTabKey { get; set; }

    public string? PinnedDesignTabTitle { get; set; }

    /// <summary>Last known ChatGPT URL for the pinned design tab (restored across app restarts).</summary>
    public string? PinnedDesignTabUrl { get; set; }

    public ProjectLink? ProjectLink { get; set; }

    /// <summary>Active utility conversations keyed by generation job id.</summary>
    public Dictionary<string, GenerationUtilitySession> UtilitySessions { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<GenerationUtilitySessionArchive> UtilitySessionArchive { get; set; } = [];

    /// <summary>Prior play thread conversation ids archived after handoff to a new thread.</summary>
    public List<PlayThreadArchiveEntry> PlayThreadArchive { get; set; } = [];

    /// <summary>Legacy — migrated to UtilitySessions on load.</summary>
    public EntityUtilitySession? EntityUtility { get; set; }

    /// <summary>Legacy — migrated to UtilitySessionArchive on load.</summary>
    public List<EntityUtilitySessionArchive> EntityUtilityArchive { get; set; } = [];

    /// <summary>Last error per utility job id (persisted for Session tab diagnostics).</summary>
    public Dictionary<string, string> UtilityJobLastErrors { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset? LastProjectInstructionsSyncedAt { get; set; }

    public string? LastProjectInstructionsSyncedHash { get; set; }

    public DateTimeOffset? InstructionsManuallyPublishedAt { get; set; }

    public string? InstructionsManuallyPublishedHash { get; set; }

    /// <summary>Transient diagnostic for the last utility conversation ensure failure.</summary>
    [JsonIgnore]
    public string? UtilityConversationLastError { get; set; }

    /// <summary>Per-utility-job custom instruction bodies (key = utility job id, e.g. bootstrap_lore).</summary>
    public Dictionary<string, UtilityJobGuideOverride> UtilityJobGuideOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public AdventureSettings Settings { get; set; } = new();

    public DateTimeOffset? SectionInjectionMigratedAt { get; set; }

    /// <summary>Registered play/design/utility thread instances (CMD-221).</summary>
    public List<AdventureThreadEntry> ThreadRegistry { get; set; } = [];

    /// <summary>Active thread entry id per kind name (<see cref="AdventureThreadKind"/>).</summary>
    public Dictionary<string, Guid> ActiveThreadIds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>When set, legacy singleton pins were migrated into <see cref="ThreadRegistry"/>.</summary>
    public DateTimeOffset? ThreadRegistryMigratedAt { get; set; }

    /// <summary>Auto utility jobs waiting for the next play packet (CMD-328).</summary>
    public List<PendingUtilityInjection> PlayUtilityInjectionQueue { get; set; } = [];

    /// <summary>Utility jobs included in the last sent play packet (CMD-332 retrieval).</summary>
    public List<PendingUtilityInjection> LastDispatchedUtilityJobs { get; set; } = [];

    /// <summary>Utility worker lane capability probe results.</summary>
    public UtilityWorkerCapabilities? UtilityWorkerCapabilities { get; set; }
}

public sealed class UtilityJobGuideOverride
{
    public string InstructionBody { get; set; } = "";

    public UtilityStoryContextSettings? Context { get; set; }
}

public sealed class PlayThreadArchiveEntry
{
    public string ConversationId { get; set; } = "";

    public DateTimeOffset ArchivedAt { get; set; } = DateTimeOffset.UtcNow;

    public int AcceptedTurnCountAtArchive { get; set; }
}

public sealed class PlayTurnOverrideSettings
{
    public string? ResponseLength { get; set; }

    public string? DetailLevel { get; set; }

    public string? Tone { get; set; }

    public string? Difficulty { get; set; }

    public string? ViolenceLevel { get; set; }

    public string? NarrativePacing { get; set; }

    public string? ConsequenceWeight { get; set; }

    /// <summary>Freeform one-shot directive for the next play send only.</summary>
    public string? TurnDirective { get; set; }

    public bool EmphasizeBoundaries { get; set; }

    public bool EmphasizePortrayalRules { get; set; }
}

/// <summary>Session-scoped narrator overrides for the active play session.</summary>
public sealed class PlaySessionNarratorOverrides
{
    public string? ResponseLength { get; set; }

    public string? DetailLevel { get; set; }

    public string? Tone { get; set; }

    public string? Difficulty { get; set; }

    public string? ViolenceLevel { get; set; }

    public string? NarrativePacing { get; set; }

    public string? ConsequenceWeight { get; set; }

    public string? TemporaryAddendum { get; set; }

    public bool EmphasizeBoundaries { get; set; }

    public bool EmphasizePortrayalRules { get; set; }
}

public enum NarratorOverrideScope
{
    Turn,
    Session,
    Adventure,
}

public enum NarratorParameter
{
    ResponseLength,
    DetailLevel,
    Tone,
    Difficulty,
    ViolenceLevel,
    NarrativePacing,
    ConsequenceWeight,
}

public sealed class UtilityJobOverrideSettings
{
    public string ResponseLength { get; set; } = "normal";

    public string ResponseDetail { get; set; } = "standard";
}

public enum AttachmentContextMode
{
    Auto,
    Full,
    Minimal,
}

public enum AdventureStatus
{
    Active,
    Paused,
    Completed,
    Designing,
}

/// <summary>How adventure sources are published to the linked ChatGPT Project.</summary>
public enum SourcePublishMode
{
    /// <summary>Wrapper is authoritative; user copies instructions and drags files manually.</summary>
    Manual,

    /// <summary>Programmatic upload via ChatGPT API (advanced; may not work in browser).</summary>
    ApiSync,
}

public sealed class AdventureSettings
{
    public int MaxPacketChars { get; set; } = 28000;

    public bool AdventureAutomationEnabled { get; set; } = true;

    public bool OfferStartOnPlay { get; set; } = true;

    /// <summary>When true, always inline full lore in play packets (debug escape hatch).</summary>
    [JsonPropertyName("forceFatPackets")]
    public bool ForceInlineLore { get; set; }

    /// <summary>Wrap packet sections in [[cgw:…]] tags for stripping/display.</summary>
    public bool UseContextTags { get; set; } = true;

    /// <summary>When true, play/utility sends use DOM composer submit instead of conversation API.</summary>
    public bool PreferDomPlaySend { get; set; }

    /// <summary>When true, replace ChatGPT's composer with the legacy in-page wrapper UI.</summary>
    public bool UseWrapperComposer { get; set; }

    public string Tone { get; set; } = "";

    public string Perspective { get; set; } = "second person";

    public string Tense { get; set; } = "present";

    public string DetailLevel { get; set; } = "medium";

    public string ViolenceLevel { get; set; } = "moderate";

    public string Difficulty { get; set; } = "balanced";

    public string NarrativePacing { get; set; } = "balanced";

    public string ConsequenceWeight { get; set; } = "balanced";

    /// <summary>Last narrator override scope selected in play UI (Turn, Session, or Adventure).</summary>
    public string? LastNarratorOverrideScope { get; set; }

    public List<string> ContentBoundaries { get; set; } = [];

    /// <summary>Per-subject portrayal rules (characters, factions, concepts) for the narrator contract.</summary>
    public List<CharacterPortrayalRule> CharacterPortrayalRules { get; set; } = [];

    /// <summary>Optional extra narrator-contract text (not world lore).</summary>
    public string InstructionAddendum { get; set; } = "";

    public string? PromptPresetId { get; set; }

    /// <summary>When true, adventure play side panel is collapsed to maximize chat width.</summary>
    public bool PlaySidePanelCollapsed { get; set; }

    /// <summary>Expanded play side panel width in device-independent pixels.</summary>
    public double PlaySidePanelWidth { get; set; } = 300;

    /// <summary>Session cockpit height in the play side panel (0 = use default on first layout).</summary>
    public double PlaySessionCockpitHeight { get; set; }

    /// <summary>When true, adventure play notes panel is collapsed to maximize chat width.</summary>
    public bool PlayNotesPanelCollapsed { get; set; }

    /// <summary>Expanded play notes panel width in device-independent pixels.</summary>
    public double PlayNotesPanelWidth { get; set; } = 240;

    /// <summary>Queue entity extraction after each accepted play turn (requires linked Project).</summary>
    public bool AutoExtractEntities { get; set; }

    public bool AutoProposeMemories { get; set; }

    public bool AutoUpdateSummary { get; set; }

    public int SummaryUpdateIntervalTurns { get; set; } = 5;

    public bool AutoContinuityCheck { get; set; }

    public string AttachmentOnlyPlaceholder { get; set; } = "[Attached file]";

    public bool InjectAttachmentGuidance { get; set; } = true;

    public AttachmentContextMode AttachmentContextMode { get; set; } = AttachmentContextMode.Auto;

    /// <summary>Per-tab placement: Reference, Warnings, State, Notes → Left, Right, Hidden.</summary>
    public Dictionary<string, string> PlayTabPlacement { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Active layout preset id (writer, gm, minimal) or null for custom.</summary>
    public string? PlayLayoutPresetId { get; set; }

    /// <summary>One-shot overrides injected into the next play packet.</summary>
    public PlayTurnOverrideSettings PlayTurnOverrides { get; set; } = new();

    /// <summary>Session-scoped narrator overrides keyed by <see cref="PlaySession.Id"/>.</summary>
    public Dictionary<string, PlaySessionNarratorOverrides> SessionNarratorOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-job utility overrides (response length, detail).</summary>
    public Dictionary<string, UtilityJobOverrideSettings> UtilityJobOverrides { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Play surface quick actions visibility: Visible, Hidden, InjectedOnly.</summary>
    public Dictionary<string, string> PlaySurfaceActions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool AutoSyncProjectInstructions { get; set; }

    /// <summary>Manual = copy/drag publish; ApiSync = programmatic source sync.</summary>
    public SourcePublishMode SourcePublishMode { get; set; } = SourcePublishMode.Manual;

    /// <summary>Default story-context feed for utility AI action job packets.</summary>
    public UtilityStoryContextSettings UtilityStoryContext { get; set; } = new();

    /// <summary>Where AI utility jobs run: separate utility thread or inline in the play thread.</summary>
    public UtilityDeliveryMode UtilityDeliveryMode { get; set; } = UtilityDeliveryMode.InlinePlayThread;

    /// <summary>When inline delivery is used, hide utility traffic in the play reading UI.</summary>
    public bool HideInlineUtilityDuringPlay { get; set; } = true;

    /// <summary>Peek toggle: show inline utility traffic in the play reading UI.</summary>
    public bool ShowInlineUtilityTraffic { get; set; }

    /// <summary>Hash of last manual utility scope to avoid duplicate bundled runs.</summary>
    public string? LastUtilityScopeHash { get; set; }

    /// <summary>When true, use section-based context injection (v2 sources packets).</summary>
    public bool UseSectionInjection { get; set; } = true;

    /// <summary>Export reviewed rolling summary to optional summary.md.</summary>
    public bool ExportSummarySource { get; set; }

    /// <summary>Footer hint when log.json diverges from the play thread and sync was skipped.</summary>
    public string? ThreadLogDriftHint { get; set; }

    /// <summary>Drift fingerprint the author dismissed; suppresses repeat sync prompts until drift changes.</summary>
    public string? ThreadLogDriftDismissedHash { get; set; }

    /// <summary>Play packet section includes, transcript depth, and injection preset.</summary>
    public PlayInjectionPolicy InjectionPolicy { get; set; } = new();

    /// <summary>Injection-first utility transport (CMD-326). Default legacy inline send.</summary>
    public PlayUtilityInjectionMode PlayUtilityInjectionMode { get; set; } = PlayUtilityInjectionMode.LegacyInlineSend;

    /// <summary>Max embedded utility sections per play send when <see cref="PlayUtilityInjectionMode"/> is InjectionFirst.</summary>
    public int MaxUtilitySectionsPerSend { get; set; } = 2;

    /// <summary>Lane selection between play injection and utility worker.</summary>
    public UtilityExecutionPolicy UtilityExecutionPolicy { get; set; } =
        UtilityExecutionPolicy.PlayInjectionPreferred;

    /// <summary>When true, auto jobs that exceed <see cref="MaxUtilitySectionsPerSend"/> spill to worker outbox.</summary>
    public bool AutoSpillToWorker { get; set; } = true;

    /// <summary>Developer-only: allow DomOnly utility sends when worker diagnostics enabled.</summary>
    public bool AllowDomOnlyUtilityDiagnostics { get; set; }

    /// <summary>CMD-392: lane-aware utility job context assembly (worker lane first).</summary>
    public bool UseUtilityJobContextAssembler { get; set; } = true;
}

public sealed class CharacterPortrayalRule
{
    /// <summary>Character name, faction, or story element (e.g. Mara, Crownward).</summary>
    public string Subject { get; set; } = "";

    /// <summary>What the narrator must avoid or emphasize for this subject.</summary>
    public string Rule { get; set; } = "";
}
