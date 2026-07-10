# Entity internal state model

**Status:** Comprehensive field model (schema v1) — 2026-07-04  
**Tracker:** [entity-internal-state-tracker.md](entity-internal-state-tracker.md)  
**Code:** `EntityInternalStateBlocks.cs`, `EntityInternalStateKinds.cs`, `EntityInternalStateDocument.cs`, `EntityInternalStateService.cs`  
**On disk:** `{adventure}/entity-state.json`

---

## Purpose

Separate **canon** (`entities.json`) from **mutable play state** per entity:

| Layer | File | Holds |
|-------|------|--------|
| Canon | `entities.json` | Stable identity: name, description, role, category, relationships |
| Internal state | `entity-state.json` | Mood, injuries, trust, quest progress, location occupancy, item condition, etc. |
| Session | `state.json` | Scene-level facts: current location, time, weather, party roster summary |

All internal state fields are **optional strings, lists, or dictionaries** — free-text friendly for narrative RPG use. Empty defaults serialize cleanly; AI and authors populate only what matters.

---

## Shared blocks

Used across cast entities (player, party, NPC, custom):

### EmotionalStateBlock

| Field | Purpose |
|-------|---------|
| `mood` | Primary emotional label |
| `fear` | Current fear focus |
| `stress` | Anxiety / pressure level |
| `confidence` | Self-assessed confidence |
| `hope` | Optimism about outcomes |
| `anger` | Irritation or rage |
| `grief` | Loss or mourning |
| `stability` | Emotional steadiness |
| `lastShift` | What recently changed mood |
| `triggers[]` | Reliable emotional triggers |
| `emotions[]` | Additional tagged emotions |

### MotivationStateBlock

| Field | Purpose |
|-------|---------|
| `goals[]` | Long-term goals |
| `immediateGoals[]` | Scene-level intent |
| `motivation` | Core drive summary |
| `obstacles[]` | Blockers |
| `values[]` | Non-negotiable values |
| `priorities[]` | Ordered when goals conflict |
| `commitment` | How committed to current goals |
| `internalConflicts[]` | Value/duty tensions |
| `hiddenAgenda` | Concealed intent |
| `needs[]` | What they need from others |
| `willingToSacrifice[]` | What they will give up |

### PhysicalStateBlock

| Field | Purpose |
|-------|---------|
| `condition` | Overall health status |
| `injuries[]` | Active wounds |
| `fatigue` | Exhaustion |
| `afflictions[]` | Poison, curse, illness |
| `recovery` | Healing timeline |
| `pain` | Pain level |
| `mobility` | Movement capability |
| `hunger` | Deprivation |
| `rest` | Sleep status |
| `impairments[]` | Lasting impairments |
| `vitalStatus` | Stable / critical / unconscious |
| `lastHarm` | How latest harm occurred |

### KnowledgeStateBlock

| Field | Purpose |
|-------|---------|
| `knows[]` | Confirmed facts |
| `suspects[]` | Unconfirmed beliefs |
| `secrets[]` | Hidden from others |
| `misbeliefs[]` | False beliefs |
| `recentlyLearned[]` | New knowledge in play |
| `forgotten[]` | Lost information |
| `rumors[]` | Heard rumors |
| `expertise[]` | Subject strengths/weaknesses |
| `sources[]` | Who/what informed them |
| `openQuestions[]` | Active questions |

### EquipmentStateBlock

| Field | Purpose |
|-------|---------|
| `equipped[]` | Worn/wielded items |
| `carried[]` | On person, not equipped |
| `wanted[]` | Sought items |
| `primaryWeapon` | Main weapon/tool |
| `armor` | Protective gear |
| `supplies[]` | Ammo, charges, consumables |
| `encumbrance` | Load level |
| `malfunctioning[]` | Broken gear |
| `hidden[]` | Concealed items |
| `recentlyLost[]` | Lost/stolen/discarded |
| `recentlyGained[]` | Recently acquired |

### SocialStateBlock

| Field | Purpose |
|-------|---------|
| `trustTowardPlayer` | Trust level |
| `disposition` | General stance |
| `allies[]` / `enemies[]` | Named relationships |
| `relationships{}` | Entity → stance map |
| `reputation` | Public reputation |
| `rumorsAbout[]` | Circulating rumors |
| `obligations[]` | Duties and vows |
| `favors[]` | Owed favors |
| `affiliations[]` | Faction membership |
| `loyaltyConflicts[]` | Competing loyalties |
| `impression` | Social presence |

### TacticalStateBlock

| Field | Purpose |
|-------|---------|
| `alertness` | Awareness level |
| `combatReadiness` | Ready to fight |
| `tactics[]` | Preferred tactics |
| `combatIntent` | Flee / negotiate / attack |
| `position` | Cover and terrain |
| `threatAssessment` | Threat read |
| `escapeOptions[]` | Fallback plans |
| `combatAllies[]` | Coordinated allies |
| `lastCombatAction` | Last fight action |
| `posture` | Pursuing / fleeing / holding |

### PresenceStateBlock

| Field | Purpose |
|-------|---------|
| `currentLocation` | Where they are now |
| `isPresent` | On-screen in scene |
| `activity` | Current action |
| `with[]` | Who they are with |
| `visibility` | Hidden / spotted / disguised |
| `lastSeen` | Last sighting if off-screen |
| `travelStatus` | Movement status |
| `appearanceNote` | Visible appearance |
| `sceneRole` | Focus / supporting / background |

### IdentityStateBlock

| Field | Purpose |
|-------|---------|
| `isDisguised` | Using false identity |
| `coverIdentity` | Alias in use |
| `recognizedBy[]` | Who knows true identity |
| `wantedStatus` | Legal/hunted status |
| `credentials[]` | Titles claimed |
| `publicFace` | How others see them |

### ResourceStateBlock (player)

| Field | Purpose |
|-------|---------|
| `wealth` | Currency / buying power |
| `supplies` | Food, water, rations |
| `magic` | Mana / spell reserves |
| `stamina` | Exertion reserve |
| `health` | HP summary |
| `custom{}` | Named resource pools |

### NarrativeFocusBlock

| Field | Purpose |
|-------|---------|
| `arcStage` | Personal arc position |
| `spotlight` | Narrative focus level |
| `foreshadowing[]` | Setup threads |
| `callbacks[]` | Earlier beat references |
| `lastMajorBeat` | Last big story moment |
| `openThreads[]` | Unresolved threads |

### InternalFlagsBlock

| Field | Purpose |
|-------|---------|
| `flags{}` | Boolean flags |
| `tags{}` | String tags |
| `counters{}` | Named integer counters |
| `timestamps{}` | Named turn/time markers |
| `notes` | Freeform author notes |

---

## Per-kind profiles

### Player (`PlayerInternalState`)

Blocks: presence, identity, emotional, motivation, physical, knowledge, equipment, resources, tactical, narrative, flags.

Extra: `moralStanding`, `sessionGoals[]`, `commitments[]`.

### Party companion (`CompanionInternalState`)

Full cast blocks including social and narrative.

Extra: `loyalty`, `partyRole`, `morale`, `departureRisk[]`, `lastBondingMoment`, `needsFromPlayer[]`.

### NPC (`CharacterInternalState`)

Same blocks as companion.

Extra: `agendaVisibility`, `routine`, `voiceNotes`, `lastPlayerInteraction`, `threatLevel`, `negotiationLeverage[]`, `availability`, `pressurePoints[]`.

### Location (`LocationInternalState`)

Presence + `occupants[]`, `atmosphere`, `lighting`, `noiseLevel`, `temperature`, `smells[]`, `sounds[]`, `discoveredFeatures[]`, `activeHazards[]`, `securityLevel`, `controlledBy`, `localWeather`, `restrictedAreas[]`, `accessRequirements[]`, `recentEvents[]`, `itemsPresent[]`, `resourcesAvailable[]`, `populationDensity`, `activeHooks[]`, `physicalCondition`, `timeOfDayNote`, flags.

### Faction (`FactionInternalState`)

`morale`, `resources`, `influence`, `stanceTowardPlayer`, `activeOperations[]`, `internalConflict`, `knownMembersPresent[]`, `territory[]`, `leadership`, `alliances[]`, `rivalries[]`, `recruitment`, `factionSecrets[]`, `publicFace`, `recentOutcomes[]`, `threatLevel`, `playerStanding`, flags.

### Concept (`ConceptInternalState`)

`understanding`, `misconceptions[]`, `openQuestions[]`, `examplesSeen[]`, `applications[]`, `teachingProgress`, `relatedConcepts[]`, `confusionPoints[]`, `canonVsHeadcanon`, flags.

### Quest (`QuestInternalState`)

`progress`, objective lists, `blockers[]`, `urgency`, `deadline`, `relatedEntityRefs[]`, `status`, `rewardClaimed`, `questGiver`, `originLocation`, `nextStep`, `choicesMade[]`, `hiddenObjectives[]`, `failureConditions[]`, `rewards[]`, flags.

### Mystery (`MysteryInternalState`)

`discoveredClues[]`, `workingTheories[]`, `confidence`, `redHerrings[]`, `resolvedInPlay`, `ruledOutTheories[]`, `suspects[]`, `witnesses[]`, `lastClueFound`, `stakes`, `partialAnswer`, flags.

### Conflict (`ConflictInternalState`)

`escalation`, `tempers`, `casualties[]`, `activeFronts[]`, `sides[]`, `momentum`, `negotiationStatus`, `strategicObjectives[]`, `civilianImpact`, `playerInvolvement`, `turningPoints[]`, flags.

### Consequence (`ConsequenceInternalState`)

`severity`, `countdown`, `triggered`, `triggeredWhen`, `partialTriggers[]`, `mitigationAttempts[]`, `affectedEntities[]`, `reversibility`, `playerAwareness`, `cascadeEffects[]`, flags.

### Item (`ItemInternalState`)

Presence + `condition`, `durability`, `heldBy`, `storedAt`, `isEquipped`, `charges`, `activationState`, `category`, `weight`, `value`, `magicalStatus`, `identification`, `activeEffects[]`, `binding`, `lastUsed`, `ownershipHistory`, `isQuestItem`, `discoveredProperties[]`, `components[]`, flags.

### Vehicle (`VehicleInternalState`)

Presence + `condition`, `fuelOrSupplies`, `destination`, `inTransit`, `crew[]`, `passengers[]`, `cargo[]`, `crewMorale`, `speed`, `damageZones[]`, `armaments[]`, `navigation`, `pursuitStatus`, `mooringStatus`, `maintenanceNeeded[]`, `registration`, `threats[]`, flags.

### Custom (`CustomInternalState`)

All cast blocks + `customKind`, `extendedFields{}`, flags — for author-defined entity types.

---

## Service API

See [entity-internal-state-tracker.md](entity-internal-state-tracker.md) for implementation status.

---

## Entity editor (Internal tab)

**UX principles:**

- **Separate from Profile** — canon fields stay on Profile; mutable state on Internal tab.
- **Collapsible sections** — grouped by block (Emotional, Physical, Social, etc.); sections with data auto-expand.
- **Hide empty sections** — default hides noise; “Show empty sections” reveals the full schema.
- **Field types** — strings (single/multi-line), booleans (checkbox), lists (one item per line), dictionaries (`key: value` per line).
- **Header skim** — mood / condition / progress summary on entity dialog header; link jumps to Internal tab.
- **Schema-driven** — fields discovered via reflection from `EntityInternalStateKinds` types (`EntityInternalStateSchema`).

Code: `EntityInternalStateFormHost`, `EntityInternalStateEditMapper`, `EntityEditDialog` Internal tab.

---

## AI jobs

| Job | Role |
|-----|------|
| `propose_entity_state` | Patch internal state from scoped exchange → `entity-state.json` review queue |
| `extract_entities` / `expand_entity` | Canon only — prompts explicitly exclude internal/psychological state |
| `update_state` | Session-level `state.json` (location, objectives, flags) — not per-entity internal state |

`propose_entity_state` publishes `entity-state.json`, includes target entity summaries + field hints, returns `{ patches: [...] }`.

---

## Related

- [entity-internal-state-tracker.md](entity-internal-state-tracker.md)
- [entity-extract-update-workflow.md](entity-extract-update-workflow.md)
- [canon-reference-paradigm.md](canon-reference-paradigm.md) — reference doc taxonomy and change integration
- [entity-canon-state-lifecycle.md](entity-canon-state-lifecycle.md) *(planned — CMD-468)* — canon vs state mapping and promotion

---

*Last updated: 2026-07-04*
