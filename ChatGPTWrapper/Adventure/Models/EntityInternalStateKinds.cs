namespace ChatGPTWrapper.Adventure.Models;

// --- Per-kind internal state (only populated fields are serialized) ---

public sealed class PlayerInternalState
{
    public PresenceStateBlock Presence { get; set; } = new();

    public IdentityStateBlock Identity { get; set; } = new();

    public EmotionalStateBlock Emotional { get; set; } = new();

    public MotivationStateBlock Motivation { get; set; } = new();

    public PhysicalStateBlock Physical { get; set; } = new();

    public KnowledgeStateBlock Knowledge { get; set; } = new();

    public EquipmentStateBlock Equipment { get; set; } = new();

    public ResourceStateBlock Resources { get; set; } = new();

    public TacticalStateBlock Tactical { get; set; } = new();

    public NarrativeFocusBlock Narrative { get; set; } = new();

    /// <summary>Moral or heroic standing in the story.</summary>
    public string MoralStanding { get; set; } = "";

    /// <summary>Session-level goals the player is pursuing.</summary>
    public List<string> SessionGoals { get; set; } = [];

    /// <summary>Choices or commitments that constrain future actions.</summary>
    public List<string> Commitments { get; set; } = [];

    public InternalFlagsBlock Flags { get; set; } = new();
}

public sealed class CompanionInternalState
{
    public PresenceStateBlock Presence { get; set; } = new();

    public IdentityStateBlock Identity { get; set; } = new();

    public EmotionalStateBlock Emotional { get; set; } = new();

    public MotivationStateBlock Motivation { get; set; } = new();

    public PhysicalStateBlock Physical { get; set; } = new();

    public KnowledgeStateBlock Knowledge { get; set; } = new();

    public EquipmentStateBlock Equipment { get; set; } = new();

    public SocialStateBlock Social { get; set; } = new();

    public TacticalStateBlock Tactical { get; set; } = new();

    public NarrativeFocusBlock Narrative { get; set; } = new();

    /// <summary>Loyalty to the player (wavering → unshakeable).</summary>
    public string Loyalty { get; set; } = "";

    /// <summary>Party role (healer, scout, muscle, voice).</summary>
    public string PartyRole { get; set; } = "";

    /// <summary>Companion morale as a group member.</summary>
    public string Morale { get; set; } = "";

    /// <summary>Signals they might leave, rebel, or betray.</summary>
    public List<string> DepartureRisk { get; set; } = [];

    /// <summary>Last meaningful shared moment with the player.</summary>
    public string LastBondingMoment { get; set; } = "";

    /// <summary>What they need from the player to stay committed.</summary>
    public List<string> NeedsFromPlayer { get; set; } = [];

    public InternalFlagsBlock Flags { get; set; } = new();
}

/// <summary>NPC / cast character — richest internal state profile.</summary>
public sealed class CharacterInternalState
{
    public PresenceStateBlock Presence { get; set; } = new();

    public IdentityStateBlock Identity { get; set; } = new();

    public EmotionalStateBlock Emotional { get; set; } = new();

    public MotivationStateBlock Motivation { get; set; } = new();

    public PhysicalStateBlock Physical { get; set; } = new();

    public KnowledgeStateBlock Knowledge { get; set; } = new();

    public EquipmentStateBlock Equipment { get; set; } = new();

    public SocialStateBlock Social { get; set; } = new();

    public TacticalStateBlock Tactical { get; set; } = new();

    public NarrativeFocusBlock Narrative { get; set; } = new();

    /// <summary>How visible their agenda is (hidden, suspected, known).</summary>
    public string AgendaVisibility { get; set; } = "";

    /// <summary>Typical routine or schedule if relevant.</summary>
    public string Routine { get; set; } = "";

    /// <summary>Speech / mannerism notes for consistent portrayal.</summary>
    public string VoiceNotes { get; set; } = "";

    /// <summary>Summary of last interaction with the player.</summary>
    public string LastPlayerInteraction { get; set; } = "";

    /// <summary>Threat they pose to the player (none → lethal).</summary>
    public string ThreatLevel { get; set; } = "";

    /// <summary>Leverage the player has in negotiation.</summary>
    public List<string> NegotiationLeverage { get; set; } = [];

    /// <summary>Capture, death, or removal status (free, captive, dead, missing).</summary>
    public string Availability { get; set; } = "";

    /// <summary>What would make them cooperate or oppose the player.</summary>
    public List<string> PressurePoints { get; set; } = [];

    public InternalFlagsBlock Flags { get; set; } = new();
}

public sealed class LocationInternalState
{
    public PresenceStateBlock Presence { get; set; } = new();

    /// <summary>Who or what is currently here.</summary>
    public List<string> Occupants { get; set; } = [];

    /// <summary>Mood / feel of the place right now.</summary>
    public string Atmosphere { get; set; } = "";

    public string Lighting { get; set; } = "";

    public string NoiseLevel { get; set; } = "";

    public string Temperature { get; set; } = "";

    /// <summary>Distinct smells in the environment.</summary>
    public List<string> Smells { get; set; } = [];

    /// <summary>Audible background elements.</summary>
    public List<string> Sounds { get; set; } = [];

    /// <summary>Features discovered in play but not yet in canon description.</summary>
    public List<string> DiscoveredFeatures { get; set; } = [];

    /// <summary>Active hazards (fire, traps, weather).</summary>
    public List<string> ActiveHazards { get; set; } = [];

    public string SecurityLevel { get; set; } = "";

    /// <summary>Faction or entity controlling the location now.</summary>
    public string ControlledBy { get; set; } = "";

    public string LocalWeather { get; set; } = "";

    /// <summary>Areas blocked, locked, or sealed.</summary>
    public List<string> RestrictedAreas { get; set; } = [];

    /// <summary>Access requirements (key, password, invitation).</summary>
    public List<string> AccessRequirements { get; set; } = [];

    /// <summary>Recent events that changed the location.</summary>
    public List<string> RecentEvents { get; set; } = [];

    /// <summary>Notable items or props present now.</summary>
    public List<string> ItemsPresent { get; set; } = [];

    /// <summary>Resources available here (shelter, water, cover).</summary>
    public List<string> ResourcesAvailable { get; set; } = [];

    /// <summary>How crowded or empty the space is.</summary>
    public string PopulationDensity { get; set; } = "";

    /// <summary>Active quest hooks or leads tied to this place.</summary>
    public List<string> ActiveHooks { get; set; } = [];

    /// <summary>Damage, decay, or recent alterations.</summary>
    public string PhysicalCondition { get; set; } = "";

    /// <summary>Time-of-day effects currently relevant.</summary>
    public string TimeOfDayNote { get; set; } = "";

    public InternalFlagsBlock Flags { get; set; } = new();
}

public sealed class FactionInternalState
{
    public string Morale { get; set; } = "";

    public string Resources { get; set; } = "";

    public string Influence { get; set; } = "";

    public string StanceTowardPlayer { get; set; } = "";

    /// <summary>What the faction is actively doing in the story now.</summary>
    public List<string> ActiveOperations { get; set; } = [];

    public string InternalConflict { get; set; } = "";

    public List<string> KnownMembersPresent { get; set; } = [];

    /// <summary>Territory or holdings currently controlled.</summary>
    public List<string> Territory { get; set; } = [];

    /// <summary>Leadership status (stable, contested, decapitated).</summary>
    public string Leadership { get; set; } = "";

    /// <summary>Current alliances with other factions.</summary>
    public List<string> Alliances { get; set; } = [];

    /// <summary>Active rivalries or wars.</summary>
    public List<string> Rivalries { get; set; } = [];

    /// <summary>Recruitment or expansion activity.</summary>
    public string Recruitment { get; set; } = "";

    /// <summary>Secrets the faction guards.</summary>
    public List<string> FactionSecrets { get; set; } = [];

    /// <summary>Public image vs hidden agenda.</summary>
    public string PublicFace { get; set; } = "";

    /// <summary>Recent victories or defeats.</summary>
    public List<string> RecentOutcomes { get; set; } = [];

    /// <summary>Overall threat the faction poses.</summary>
    public string ThreatLevel { get; set; } = "";

    /// <summary>Player's rank or standing within the faction.</summary>
    public string PlayerStanding { get; set; } = "";

    public InternalFlagsBlock Flags { get; set; } = new();
}

public sealed class ConceptInternalState
{
    /// <summary>How well the player / party understands this (novice → expert).</summary>
    public string Understanding { get; set; } = "";

    public List<string> Misconceptions { get; set; } = [];

    public List<string> OpenQuestions { get; set; } = [];

    /// <summary>Concrete examples the player has encountered.</summary>
    public List<string> ExamplesSeen { get; set; } = [];

    /// <summary>How the concept has been applied in play.</summary>
    public List<string> Applications { get; set; } = [];

    /// <summary>Teaching or explanation progress if someone is learning it.</summary>
    public string TeachingProgress { get; set; } = "";

    /// <summary>Related concepts unlocked or linked in play.</summary>
    public List<string> RelatedConcepts { get; set; } = [];

    /// <summary>Specific confusion points remaining.</summary>
    public List<string> ConfusionPoints { get; set; } = [];

    /// <summary>Player headcanon vs confirmed canon.</summary>
    public string CanonVsHeadcanon { get; set; } = "";

    public InternalFlagsBlock Flags { get; set; } = new();
}

public sealed class QuestInternalState
{
    /// <summary>Free-text or percentage progress summary.</summary>
    public string Progress { get; set; } = "";

    public List<string> ActiveObjectives { get; set; } = [];

    public List<string> CompletedObjectives { get; set; } = [];

    public List<string> FailedObjectives { get; set; } = [];

    public List<string> Blockers { get; set; } = [];

    public string Urgency { get; set; } = "";

    public string Deadline { get; set; } = "";

    public List<string> RelatedEntityRefs { get; set; } = [];

    /// <summary>Play status (active, paused, abandoned).</summary>
    public string Status { get; set; } = "";

    /// <summary>Whether rewards have been claimed.</summary>
    public bool RewardClaimed { get; set; }

    /// <summary>Who gave or owns the quest.</summary>
    public string QuestGiver { get; set; } = "";

    /// <summary>Where the quest started or was accepted.</summary>
    public string OriginLocation { get; set; } = "";

    /// <summary>Hint for the next recommended step.</summary>
    public string NextStep { get; set; } = "";

    /// <summary>Branch or moral choices made during the quest.</summary>
    public List<string> ChoicesMade { get; set; } = [];

    /// <summary>Hidden objectives not yet revealed.</summary>
    public List<string> HiddenObjectives { get; set; } = [];

    /// <summary>Conditions that would fail the quest.</summary>
    public List<string> FailureConditions { get; set; } = [];

    /// <summary>Promised or expected rewards.</summary>
    public List<string> Rewards { get; set; } = [];

    public InternalFlagsBlock Flags { get; set; } = new();
}

public sealed class MysteryInternalState
{
    /// <summary>Clues discovered during play (may extend canon Clues field).</summary>
    public List<string> DiscoveredClues { get; set; } = [];

    public List<string> WorkingTheories { get; set; } = [];

    public string Confidence { get; set; } = "";

    public List<string> RedHerrings { get; set; } = [];

    public bool ResolvedInPlay { get; set; }

    /// <summary>Theories ruled out during investigation.</summary>
    public List<string> RuledOutTheories { get; set; } = [];

    /// <summary>Named suspects or persons of interest.</summary>
    public List<string> Suspects { get; set; } = [];

    /// <summary>Key witnesses and what they know.</summary>
    public List<string> Witnesses { get; set; } = [];

    /// <summary>When and where the last clue was found.</summary>
    public string LastClueFound { get; set; } = "";

    /// <summary>Stakes if the mystery stays unresolved.</summary>
    public string Stakes { get; set; } = "";

    /// <summary>Partial answer or revelation so far.</summary>
    public string PartialAnswer { get; set; } = "";

    public InternalFlagsBlock Flags { get; set; } = new();
}

public sealed class ConflictInternalState
{
    public string Escalation { get; set; } = "";

    public string Tempers { get; set; } = "";

    public List<string> Casualties { get; set; } = [];

    public List<string> ActiveFronts { get; set; } = [];

    /// <summary>Factions, groups, or individuals on each side.</summary>
    public List<string> Sides { get; set; } = [];

    /// <summary>Which side is currently gaining or losing.</summary>
    public string Momentum { get; set; } = "";

    /// <summary>Negotiation or ceasefire status.</summary>
    public string NegotiationStatus { get; set; } = "";

    /// <summary>Strategic objectives per side (free text entries).</summary>
    public List<string> StrategicObjectives { get; set; } = [];

    /// <summary>Impact on civilians or bystanders.</summary>
    public string CivilianImpact { get; set; } = "";

    /// <summary>How involved the player is (observer → driving force).</summary>
    public string PlayerInvolvement { get; set; } = "";

    /// <summary>Recent turning points in the conflict.</summary>
    public List<string> TurningPoints { get; set; } = [];

    public InternalFlagsBlock Flags { get; set; } = new();
}

public sealed class ConsequenceInternalState
{
    public string Severity { get; set; } = "";

    /// <summary>Countdown or trigger proximity.</summary>
    public string Countdown { get; set; } = "";

    public bool Triggered { get; set; }

    public string TriggeredWhen { get; set; } = "";

    /// <summary>Which trigger conditions are partially met.</summary>
    public List<string> PartialTriggers { get; set; } = [];

    /// <summary>Attempts to prevent or mitigate the consequence.</summary>
    public List<string> MitigationAttempts { get; set; } = [];

    /// <summary>Entities that would be affected.</summary>
    public List<string> AffectedEntities { get; set; } = [];

    /// <summary>Whether the outcome can still be reversed.</summary>
    public string Reversibility { get; set; } = "";

    /// <summary>Whether the player knows this is impending.</summary>
    public string PlayerAwareness { get; set; } = "";

    /// <summary>Downstream effects if triggered.</summary>
    public List<string> CascadeEffects { get; set; } = [];

    public InternalFlagsBlock Flags { get; set; } = new();
}

public sealed class ItemInternalState
{
    public PresenceStateBlock Presence { get; set; } = new();

    public string Condition { get; set; } = "";

    public string Durability { get; set; } = "";

    /// <summary>Entity name or id currently holding or wearing this item.</summary>
    public string HeldBy { get; set; } = "";

    public string StoredAt { get; set; } = "";

    public bool IsEquipped { get; set; }

    /// <summary>Uses remaining for consumables / charged items.</summary>
    public string Charges { get; set; } = "";

    public string ActivationState { get; set; } = "";

    /// <summary>Item subtype: weapon, armor, tool, key, document, consumable, etc.</summary>
    public string Category { get; set; } = "";

    /// <summary>Approximate weight or bulk.</summary>
    public string Weight { get; set; } = "";

    /// <summary>Estimated value or rarity.</summary>
    public string Value { get; set; } = "";

    /// <summary>Cursed, blessed, or supernatural status.</summary>
    public string MagicalStatus { get; set; } = "";

    /// <summary>Whether true nature is identified (unidentified, partial, known).</summary>
    public string Identification { get; set; } = "";

    /// <summary>Active modifiers or effects.</summary>
    public List<string> ActiveEffects { get; set; } = [];

    /// <summary>Attunement, binding, or ownership lock.</summary>
    public string Binding { get; set; } = "";

    /// <summary>When and how it was last used.</summary>
    public string LastUsed { get; set; } = "";

    /// <summary>Notable prior owners or history.</summary>
    public string OwnershipHistory { get; set; } = "";

    /// <summary>Whether it is a quest-critical item.</summary>
    public bool IsQuestItem { get; set; }

    /// <summary>Hidden properties discovered in play.</summary>
    public List<string> DiscoveredProperties { get; set; } = [];

    /// <summary>Components if used in crafting.</summary>
    public List<string> Components { get; set; } = [];

    public InternalFlagsBlock Flags { get; set; } = new();
}

public sealed class VehicleInternalState
{
    public PresenceStateBlock Presence { get; set; } = new();

    public string Condition { get; set; } = "";

    public string FuelOrSupplies { get; set; } = "";

    public string Destination { get; set; } = "";

    public bool InTransit { get; set; }

    public List<string> Crew { get; set; } = [];

    public List<string> Passengers { get; set; } = [];

    public List<string> Cargo { get; set; } = [];

    public string CrewMorale { get; set; } = "";

    /// <summary>Current speed or pace.</summary>
    public string Speed { get; set; } = "";

    /// <summary>Localized damage (hull, engine, sails, legs).</summary>
    public List<string> DamageZones { get; set; } = [];

    /// <summary>Weapons or defensive systems status.</summary>
    public List<string> Armaments { get; set; } = [];

    /// <summary>Navigation or steering status.</summary>
    public string Navigation { get; set; } = "";

    /// <summary>Whether being pursued or chasing something.</summary>
    public string PursuitStatus { get; set; } = "";

    /// <summary>Docked, anchored, moored, or parked status.</summary>
    public string MooringStatus { get; set; } = "";

    /// <summary>Maintenance or repair needed.</summary>
    public List<string> MaintenanceNeeded { get; set; } = [];

    /// <summary>Legal ownership or registration.</summary>
    public string Registration { get; set; } = "";

    /// <summary>Threats specifically targeting the vehicle.</summary>
    public List<string> Threats { get; set; } = [];

    public InternalFlagsBlock Flags { get; set; } = new();
}

public sealed class CustomInternalState
{
    /// <summary>Echo of canon <see cref="CustomEntry.Kind"/> for context.</summary>
    public string CustomKind { get; set; } = "";

    public PresenceStateBlock Presence { get; set; } = new();

    public IdentityStateBlock Identity { get; set; } = new();

    public EmotionalStateBlock Emotional { get; set; } = new();

    public MotivationStateBlock Motivation { get; set; } = new();

    public PhysicalStateBlock Physical { get; set; } = new();

    public KnowledgeStateBlock Knowledge { get; set; } = new();

    public EquipmentStateBlock Equipment { get; set; } = new();

    public SocialStateBlock Social { get; set; } = new();

    public TacticalStateBlock Tactical { get; set; } = new();

    public NarrativeFocusBlock Narrative { get; set; } = new();

    /// <summary>Author-defined fields not covered by shared blocks.</summary>
    public Dictionary<string, string> ExtendedFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public InternalFlagsBlock Flags { get; set; } = new();
}
