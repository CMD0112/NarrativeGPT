namespace ChatGPTWrapper.Adventure.Models;

/// <summary>Cross-cutting emotional / psychological state shared by cast entities.</summary>
public sealed class EmotionalStateBlock
{
    /// <summary>Primary mood label (e.g. wary, hopeful, furious).</summary>
    public string Mood { get; set; } = "";

    /// <summary>What they are afraid of right now.</summary>
    public string Fear { get; set; } = "";

    /// <summary>Stress / anxiety level (free text or low/medium/high).</summary>
    public string Stress { get; set; } = "";

    /// <summary>Self-assessed confidence.</summary>
    public string Confidence { get; set; } = "";

    /// <summary>Hope or optimism about outcomes.</summary>
    public string Hope { get; set; } = "";

    /// <summary>Current anger or irritation level.</summary>
    public string Anger { get; set; } = "";

    /// <summary>Grief, loss, or mourning in play.</summary>
    public string Grief { get; set; } = "";

    /// <summary>Emotional stability (steady, volatile, breaking).</summary>
    public string Stability { get; set; } = "";

    /// <summary>What recently shifted their emotional state.</summary>
    public string LastShift { get; set; } = "";

    /// <summary>Triggers that reliably provoke strong reactions.</summary>
    public List<string> Triggers { get; set; } = [];

    /// <summary>Additional tagged emotions (e.g. guilt, excitement, shame).</summary>
    public List<string> Emotions { get; set; } = [];
}

/// <summary>Goals, drives, and immediate intent.</summary>
public sealed class MotivationStateBlock
{
    /// <summary>Longer-term goals active in play.</summary>
    public List<string> Goals { get; set; } = [];

    /// <summary>What they are trying to accomplish this scene or exchange.</summary>
    public List<string> ImmediateGoals { get; set; } = [];

    /// <summary>Core motivation summary (why they act).</summary>
    public string Motivation { get; set; } = "";

    /// <summary>Obstacles blocking their goals.</summary>
    public List<string> Obstacles { get; set; } = [];

    /// <summary>Values they will not compromise (honor, family, profit).</summary>
    public List<string> Values { get; set; } = [];

    /// <summary>Ordered priorities when goals conflict.</summary>
    public List<string> Priorities { get; set; } = [];

    /// <summary>How committed they are to current goals (tentative → obsessive).</summary>
    public string Commitment { get; set; } = "";

    /// <summary>Internal value or duty conflicts.</summary>
    public List<string> InternalConflicts { get; set; } = [];

    /// <summary>Hidden agenda not yet visible to the player.</summary>
    public string HiddenAgenda { get; set; } = "";

    /// <summary>What they need from others to proceed.</summary>
    public List<string> Needs { get; set; } = [];

    /// <summary>What they are willing to sacrifice.</summary>
    public List<string> WillingToSacrifice { get; set; } = [];
}

/// <summary>Physical condition, injuries, and fatigue.</summary>
public sealed class PhysicalStateBlock
{
    /// <summary>Overall condition (healthy, wounded, dying, etc.).</summary>
    public string Condition { get; set; } = "";

    /// <summary>Active injuries or wounds.</summary>
    public List<string> Injuries { get; set; } = [];

    /// <summary>Fatigue / exhaustion level.</summary>
    public string Fatigue { get; set; } = "";

    /// <summary>Non-injury afflictions (poison, curse, illness).</summary>
    public List<string> Afflictions { get; set; } = [];

    /// <summary>Recovery notes or expected healing timeline.</summary>
    public string Recovery { get; set; } = "";

    /// <summary>Pain level or description.</summary>
    public string Pain { get; set; } = "";

    /// <summary>Mobility (able-bodied, limping, immobilized).</summary>
    public string Mobility { get; set; } = "";

    /// <summary>Hunger, thirst, or deprivation.</summary>
    public string Hunger { get; set; } = "";

    /// <summary>Sleep debt or rest status.</summary>
    public string Rest { get; set; } = "";

    /// <summary>Long-term impairments or disabilities relevant to play.</summary>
    public List<string> Impairments { get; set; } = [];

    /// <summary>Vital status summary (stable, critical, unconscious).</summary>
    public string VitalStatus { get; set; } = "";

    /// <summary>How the most recent injury or ailment occurred.</summary>
    public string LastHarm { get; set; } = "";
}

/// <summary>What the entity knows, believes, and hides.</summary>
public sealed class KnowledgeStateBlock
{
    /// <summary>Facts they know to be true.</summary>
    public List<string> Knows { get; set; } = [];

    /// <summary>Things they suspect but have not confirmed.</summary>
    public List<string> Suspects { get; set; } = [];

    /// <summary>Secrets they are actively hiding (author / AI only).</summary>
    public List<string> Secrets { get; set; } = [];

    /// <summary>False beliefs or misinformation they hold.</summary>
    public List<string> Misbeliefs { get; set; } = [];

    /// <summary>Facts learned recently in play.</summary>
    public List<string> RecentlyLearned { get; set; } = [];

    /// <summary>Information they forgot, lost, or had taken.</summary>
    public List<string> Forgotten { get; set; } = [];

    /// <summary>Rumors they have heard (may be false).</summary>
    public List<string> Rumors { get; set; } = [];

    /// <summary>Subject areas where they are expert or ignorant.</summary>
    public List<string> Expertise { get; set; } = [];

    /// <summary>Who or what informed their knowledge.</summary>
    public List<string> Sources { get; set; } = [];

    /// <summary>Questions they are actively trying to answer.</summary>
    public List<string> OpenQuestions { get; set; } = [];
}

/// <summary>Equipment, inventory on person, and gear intent.</summary>
public sealed class EquipmentStateBlock
{
    /// <summary>Currently equipped items (names or ids).</summary>
    public List<string> Equipped { get; set; } = [];

    /// <summary>Carried but not equipped.</summary>
    public List<string> Carried { get; set; } = [];

    /// <summary>Items they want / are seeking.</summary>
    public List<string> Wanted { get; set; } = [];

    /// <summary>Primary weapon or tool in hand.</summary>
    public string PrimaryWeapon { get; set; } = "";

    /// <summary>Armor or protective gear summary.</summary>
    public string Armor { get; set; } = "";

    /// <summary>Ammunition, charges, or consumable stock on person.</summary>
    public List<string> Supplies { get; set; } = [];

    /// <summary>Encumbrance or load (light, overburdened).</summary>
    public string Encumbrance { get; set; } = "";

    /// <summary>Broken, jammed, or unreliable gear.</summary>
    public List<string> Malfunctioning { get; set; } = [];

    /// <summary>Concealed items not obvious to observers.</summary>
    public List<string> Hidden { get; set; } = [];

    /// <summary>Items recently lost, stolen, or discarded.</summary>
    public List<string> RecentlyLost { get; set; } = [];

    /// <summary>Items recently acquired.</summary>
    public List<string> RecentlyGained { get; set; } = [];
}

/// <summary>Social stance, trust, and relationship posture.</summary>
public sealed class SocialStateBlock
{
    /// <summary>Trust toward the player (free text or numeric phrase).</summary>
    public string TrustTowardPlayer { get; set; } = "";

    /// <summary>General disposition (friendly, hostile, neutral, wary).</summary>
    public string Disposition { get; set; } = "";

    /// <summary>Named allies or supporters.</summary>
    public List<string> Allies { get; set; } = [];

    /// <summary>Named enemies or rivals.</summary>
    public List<string> Enemies { get; set; } = [];

    /// <summary>Entity name or id → stance label (loyal, suspicious, etc.).</summary>
    public Dictionary<string, string> Relationships { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Public reputation or how others describe them.</summary>
    public string Reputation { get; set; } = "";

    /// <summary>Rumors circulating about them.</summary>
    public List<string> RumorsAbout { get; set; } = [];

    /// <summary>Duties, vows, or obligations binding them.</summary>
    public List<string> Obligations { get; set; } = [];

    /// <summary>Favors owed to or by others.</summary>
    public List<string> Favors { get; set; } = [];

    /// <summary>Active faction or group affiliations.</summary>
    public List<string> Affiliations { get; set; } = [];

    /// <summary>Competing loyalties creating tension.</summary>
    public List<string> LoyaltyConflicts { get; set; } = [];

    /// <summary>First impression or social presence (imposing, forgettable).</summary>
    public string Impression { get; set; } = "";
}

/// <summary>Alertness, combat posture, and tactical notes.</summary>
public sealed class TacticalStateBlock
{
    public string Alertness { get; set; } = "";

    public string CombatReadiness { get; set; } = "";

    public List<string> Tactics { get; set; } = [];

    /// <summary>Current intent in a conflict (flee, negotiate, attack).</summary>
    public string CombatIntent { get; set; } = "";

    /// <summary>Cover, position, or terrain advantage.</summary>
    public string Position { get; set; } = "";

    /// <summary>How they assess current threats.</summary>
    public string ThreatAssessment { get; set; } = "";

    /// <summary>Escape routes or fallback plans considered.</summary>
    public List<string> EscapeOptions { get; set; } = [];

    /// <summary>Allies coordinating with in a fight or chase.</summary>
    public List<string> CombatAllies { get; set; } = [];

    /// <summary>Last significant action taken in conflict.</summary>
    public string LastCombatAction { get; set; } = "";

    /// <summary>Whether they are pursuing, fleeing, or holding.</summary>
    public string Posture { get; set; } = "";
}

/// <summary>Scene presence and narrative focus for any entity.</summary>
public sealed class PresenceStateBlock
{
    /// <summary>Where the entity is right now (may differ from canon default location).</summary>
    public string CurrentLocation { get; set; } = "";

    /// <summary>Whether they are on-screen / active in the current scene.</summary>
    public bool IsPresent { get; set; }

    /// <summary>What they are doing right now.</summary>
    public string Activity { get; set; } = "";

    /// <summary>Who they are with or observing.</summary>
    public List<string> With { get; set; } = [];

    /// <summary>Visibility to others (hidden, spotted, disguised).</summary>
    public string Visibility { get; set; } = "";

    /// <summary>When or where they were last seen if off-screen.</summary>
    public string LastSeen { get; set; } = "";

    /// <summary>Travel or movement status (stationary, en route, fleeing).</summary>
    public string TravelStatus { get; set; } = "";

    /// <summary>Current appearance note (disguise, wounds visible, etc.).</summary>
    public string AppearanceNote { get; set; } = "";

    /// <summary>Scene role (focus, supporting, background, offstage).</summary>
    public string SceneRole { get; set; } = "";
}

/// <summary>Identity, cover, and recognition in play.</summary>
public sealed class IdentityStateBlock
{
    /// <summary>Whether they are using a disguise or false identity.</summary>
    public bool IsDisguised { get; set; }

    /// <summary>Cover name or alias in use.</summary>
    public string CoverIdentity { get; set; } = "";

    /// <summary>Who recognizes their true identity.</summary>
    public List<string> RecognizedBy { get; set; } = [];

    /// <summary>Wanted, hunted, or legal status.</summary>
    public string WantedStatus { get; set; } = "";

    /// <summary>Credentials, titles, or rank currently claimed.</summary>
    public List<string> Credentials { get; set; } = [];

    /// <summary>How the player knows them vs how others know them.</summary>
    public string PublicFace { get; set; } = "";
}

/// <summary>Resource pools and material assets (cast entities).</summary>
public sealed class ResourceStateBlock
{
    /// <summary>Wealth, currency, or buying power.</summary>
    public string Wealth { get; set; } = "";

    /// <summary>General supplies (food, water, rations).</summary>
    public string Supplies { get; set; } = "";

    /// <summary>Magical or supernatural reserves (mana, spell slots).</summary>
    public string Magic { get; set; } = "";

    /// <summary>Stamina or exertion reserve.</summary>
    public string Stamina { get; set; } = "";

    /// <summary>Health / HP summary if tracked narratively.</summary>
    public string Health { get; set; } = "";

    /// <summary>Named resource → amount or status.</summary>
    public Dictionary<string, string> Custom { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Story arc position and narrative spotlight.</summary>
public sealed class NarrativeFocusBlock
{
    /// <summary>Where they sit in their personal arc (setup, crisis, resolution).</summary>
    public string ArcStage { get; set; } = "";

    /// <summary>How much narrative spotlight they have now (none → central).</summary>
    public string Spotlight { get; set; } = "";

    /// <summary>Foreshadowing or setup threads involving them.</summary>
    public List<string> Foreshadowing { get; set; } = [];

    /// <summary>Callbacks to earlier story beats.</summary>
    public List<string> Callbacks { get; set; } = [];

    /// <summary>Last major story beat involving this entity.</summary>
    public string LastMajorBeat { get; set; } = "";

    /// <summary>Unresolved threads tied to them.</summary>
    public List<string> OpenThreads { get; set; } = [];
}

/// <summary>Freeform flags and author notes that do not fit typed fields.</summary>
public sealed class InternalFlagsBlock
{
    public Dictionary<string, bool> Flags { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Named counters (e.g. warnings received, betrayals).</summary>
    public Dictionary<string, int> Counters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Named timestamps or turn markers for state changes.</summary>
    public Dictionary<string, string> Timestamps { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string Notes { get; set; } = "";
}
