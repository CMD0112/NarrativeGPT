# Data Model Reference

All persistence is **JSON files** on disk. No database. Schema version: `AdventureJson.SchemaVersion` (see `Adventure/AdventureJson.cs`).

---

## On-disk layout

Root: `%LocalAppData%\ChatGPTWrapper\`

```
ChatGPTWrapper/
├── ui-chrome.json                 # Browse UI settings
├── WebView2UserData/              # Browser profile (cookies)
├── styles/
│   └── user-overrides.css         # Optional user CSS
├── adventures/
│   └── {guid}/
│       ├── adventure.json         # AdventureMetadata
│       ├── scenario.json
│       ├── log.json
│       ├── summary.json
│       ├── state.json
│       ├── memory.json
│       ├── entities.json
│       ├── cards.json
│       ├── continuity.json
│       ├── prompt-history.json
│       ├── utility-exchanges.json
│       ├── design-workspace.json  # Adventure design wizard state (when Status=Designing)
│       ├── source-manifest.json
│       ├── notes.txt
│       └── sources/               # Exported markdown for Project sync
├── libraries/
│   ├── scenarios/index.json + {guid}.json
│   ├── worlds/
│   ├── characters/
│   ├── presets/
│   └── templates/
├── backups/                       # Adventure backup zips
└── (diagnostic logs)              # See troubleshooting.md
```

`AppDirectories.cs` defines all paths. Tests may set `AppDirectories.TestRootOverride`.

---

## AdventureBundle (aggregate root)

`Adventure/Models/AdventureBundle.cs` — loaded/saved by `AdventureStore`.

| Property | Type | File |
|----------|------|------|
| `Metadata` | `AdventureMetadata` | `adventure.json` |
| `Scenario` | `ScenarioDocument` | `scenario.json` |
| `Log` | `LogDocument` | `log.json` |
| `Summary` | `SummaryDocument` | `summary.json` |
| `State` | `StateDocument` | `state.json` |
| `Memory` | `MemoryDocument` | `memory.json` |
| `Entities` | `EntitiesDocument` | `entities.json` |
| `Cards` | `CardsDocument` | `cards.json` |
| `Continuity` | `ContinuityDocument` | `continuity.json` |
| `PromptHistory` | `PromptHistoryDocument` | `prompt-history.json` |
| `UtilityExchanges` | `UtilityExchangesDocument` | `utility-exchanges.json` |
| `DesignWorkspace` | `AdventureDesignWorkspace` | `design-workspace.json` |
| `Notes` | `string` | `notes.txt` |
| `SourceManifest` | `SourceManifest` | `source-manifest.json` |
| `ContinuationQueue` | `List<string>` | (in memory; saved with bundle ops) |
| `CurrentSessionId` | `Guid?` | runtime session pointer |

### Canonical play record

| Source | Role |
|--------|------|
| `log.json` | **Authoritative** accepted play history (`TurnStatus.Accepted` turns with player/narrator text, packet hash, alternates) |
| `thread-metadata.json` | ChatGPT thread message ordinals and DOM slot mapping (`dom:N`, `turn:{id}:user/assistant`) |
| ChatGPT thread | Live display and model-facing transcript; not the source of truth for accepted history |
| `prompt-history.json` | Audit of merged packets sent (not a substitute for `log.json`) |

On Send, `TurnTimelineService.AcceptTurn` appends to `log.json` automatically. Legacy manual review (`ResponseReviewDialog`) exists for debug/fallback only.

---

## AdventureMetadata

`adventure.json` — identity, linking, utility sessions, settings.

| Field | Type | Description |
|-------|------|-------------|
| `schemaVersion` | `int` | Migration marker |
| `id` | `Guid` | Adventure id (folder name) |
| `title` | `string` | Display title |
| `genre` | `string` | Genre tag |
| `scenarioSummary` | `string` | Short summary |
| `createdAt` | `DateTimeOffset` | Creation time |
| `lastPlayedAt` | `DateTimeOffset` | Last play activity |
| `status` | `AdventureStatus` | `Active`, `Paused`, `Completed` |
| `archived` | `bool` | Hidden from default dashboard list |
| `tags` | `string[]` | User tags |
| `linkedConversationId` | `string?` | ChatGPT play conversation id |
| `linkedProjectId` | `string?` | ChatGPT Project (gizmo) id |
| `linkedProjectHint` | `string?` | Display hint for project |
| `pinnedPlayTabKey` | `string?` | WebView tab key for play |
| `pinnedPlayTabTitle` | `string?` | Tab title |
| `pinnedUtilityTabKey` | `string?` | Utility jobs tab key |
| `pinnedUtilityTabTitle` | `string?` | Utility tab title |
| `projectLink` | `ProjectLink?` | Extended link metadata |
| `utilitySessions` | `dict<string, GenerationUtilitySession>` | Per-job utility threads |
| `utilitySessionArchive` | `GenerationUtilitySessionArchive[]` | Rotated sessions |
| `entityUtility` | `EntityUtilitySession?` | Legacy — migrated on load |
| `entityUtilityArchive` | legacy archive | Migrated on load |
| `entityExtractGuideSyncedVersion` | `int` | Legacy guide version |
| `guideSyncedVersions` | `dict<string, int>` | Per-job guide seed versions |
| `utilityJobLastErrors` | `dict<string, string>` | Persisted job errors |
| `lastProjectInstructionsSyncedAt` | `DateTimeOffset?` | Instruction sync timestamp |
| `lastProjectInstructionsSyncedHash` | `string?` | Instruction content hash |
| `instructionsManuallyPublishedAt` | `DateTimeOffset?` | Manual publish time |
| `instructionsManuallyPublishedHash` | `string?` | Manual publish hash |
| `utilityJobGuideOverrides` | `dict<string, UtilityJobGuideOverride>` | Custom job instructions |
| `settings` | `AdventureSettings` | Play/sync/automation settings |

### AdventureSettings

| Field | Default | Description |
|-------|---------|-------------|
| `maxPacketChars` | 28000 | Packet size limit |
| `adventureAutomationEnabled` | true | DOM automation for play |
| `offerStartOnPlay` | true | Prompt to start adventure |
| `forceFatPackets` | false | Always inline lore |
| `useContextTags` | true | `[[cgw:…]]` packet markers |
| `tone`, `perspective`, `tense`, `detailLevel`, `violenceLevel`, `difficulty` | see code | Narrator contract |
| `contentBoundaries` | `[]` | Global content boundaries (one per line) |
| `characterPortrayalRules` | `[]` | Per-subject portrayal rules (`CharacterPortrayalRule`: `subject`, `rule`) |
| `instructionAddendum` | `""` | Optional extra narrator-contract text |
| `promptPresetId` | null | Library preset reference |
| `playSidePanelCollapsed` | false | Play UI layout |
| `playSidePanelWidth` | 300 | Panel width (DIP) |
| `autoExtractEntities` | false | Post-turn entity job |
| `autoProposeMemories` | false | Post-turn memory job |
| `autoUpdateSummary` | false | Periodic summary job |
| `summaryUpdateIntervalTurns` | 5 | Summary interval |
| `autoContinuityCheck` | false | Continuity job |
| `autoSyncProjectInstructions` | false | Push instructions via API |
| `sourcePublishMode` | `Manual` | `Manual` or `ApiSync` |
| `utilityStoryContext` | object | Story context for utility jobs |
| `utilityDeliveryMode` | `SeparateThread` | `SeparateThread` or `Inline` |
| `hideInlineUtilityDuringPlay` | true | Hide utility traffic in UI |
| `showInlineUtilityTraffic` | false | Peek utility traffic |
| `lastUtilityScopeHash` | null | Dedup bundled utility runs |

### Enums

- `AdventureStatus`: `Active`, `Paused`, `Completed`
- `SourcePublishMode`: `Manual`, `ApiSync`
- `UtilityDeliveryMode`: `SeparateThread`, `Inline` (see `UtilityDeliveryMode.cs`)

---

## ScenarioDocument

| Field | Description |
|-------|-------------|
| `setting` | World/setting description |
| `playerRole` | Who the player is |
| `genre`, `tone` | Style metadata |
| `openingSituation` | Starting scenario |
| `majorConflicts` | Central conflicts |
| `startingConstraints` | Limits at start |
| `plotEssentials` | Must-not-forget plot |
| `worldRules` | Physics/magic/society rules |
| `authorsNote` | Style note (no new facts) |
| `sourceEditReviewQueue` | AI-proposed source edits pending review |

---

## LogDocument

| Field | Description |
|-------|-------------|
| `turns` | `TurnRecord[]` — story timeline |
| `sessions` | `PlaySession[]` — play session groupings |
| `chapters` | `StoryChapter[]` — chapter markers |

### TurnRecord

| Field | Description |
|-------|-------------|
| `id` | Turn guid |
| `index` | Monotonic turn index |
| `at` | Timestamp |
| `playerText` | Player input (Do/Say/Story) |
| `narratorText` | Accepted narrator response |
| `status` | `TurnStatus` enum |
| `parentTurnId` | Branch parent |
| `attempts` | `ResponseAttempt[]` — retries/regenerates |
| `sessionId`, `chapterId` | Grouping |
| `promptPacketHash` | Hash of sent packet |

### TurnStatus

`Pending` → `AwaitingResponse` → `Review` → `Accepted` / `Rejected`

### ResponseAttempt

| Field | Description |
|-------|-------------|
| `narratorText` | Captured text for this attempt |
| `accepted` | Whether this attempt was accepted |
| `fromRegenerate` | From regenerate action |

### PlaySession

`id`, `startedAt`, `endedAt`, `turnIds[]`

### StoryChapter

`id`, `title`, `startTurnIndex`

---

## StateDocument

Tracker fields: `currentLocation`, `playerCondition`, `activeThreats`, `openObjectives`, `unresolvedMysteries`, `recentConsequences`, `mapNotes`.

Nested:

- **SceneState** — `location`, `participants`, `immediateConflict`, `atmosphere`, `availableExits`, `visibleClues`, `activeDangers`
- **TimeState** — `inWorldTime`, `deadlines`, `scheduledConsequences`

---

## SummaryDocument

| Field | Description |
|-------|-------------|
| `rollingSummary` | Accepted rolling summary text |
| `pendingReview` | AI proposal awaiting review |
| `proposedSummary` | Proposed replacement text |

---

## MemoryDocument

| Field | Description |
|-------|-------------|
| `entries` | `MemoryEntry[]` — accepted memories |
| `reviewQueue` | Proposed memories pending review |

### MemoryEntry

`id`, `text`, `pinned`, `tags[]`, `outcome`, `anchor` (`MemoryAnchor`), `createdAt`

---

## EntitiesDocument

Structured trackers:

| Collection | Entry type |
|------------|------------|
| `player` | `PlayerCharacterSheet` |
| `characters` | `CharacterEntry` |
| `party` | `CompanionEntry` |
| `locations` | `LocationEntry` |
| `inventory` | `InventoryEntry` |
| `quests` | `QuestEntry` |
| `factions` | `FactionEntry` |
| `concepts` | `ConceptEntry` |
| `relationships` | `RelationshipEntry` |
| `mysteries` | `MysteryEntry` |
| `conflicts` | `ConflictEntry` |
| `consequences` | `ConsequenceEntry` |
| `reviewQueue` | `EntityReviewItem` — AI proposals |

Common entry fields: `id`, `name`, `description`, `status`, `tags`, `pinned` (where applicable).

---

## CardsDocument

| Field | Description |
|-------|-------------|
| `cards` | `StoryCard[]` |
| `reviewQueue` | `CardReviewItem[]` |

### StoryCard

`id`, `name`, `type` (`StoryCardType` enum), `triggers[]`, `content`, `enabled`, `tags[]`

**StoryCardType:** `Character`, `Place`, `Faction`, `Item`, `Rule`, `Creature`, `Organization`, `Lore`

---

## ContinuityDocument

| Field | Description |
|-------|-------------|
| `warnings` | `ContinuityWarningEntry[]` |
| `lastCheckedAt` | Last continuity check time |

---

## PromptHistoryDocument

Stores sent prompt packet history for debugging/review (sections, hashes, timestamps). See `Adventure/Models/PromptHistoryDocument.cs`.

---

## UtilityExchangesDocument

Records utility job exchanges (`UtilityExchangeRecord[]`) — job id, prompts, responses, timestamps.

---

## Source manifest

`source-manifest.json` — see also [user-projects-and-sync.md](user-projects-and-sync.md).

### SourceManifest

| Field | Description |
|-------|-------------|
| `schemaVersion` | Current: 3 |
| `synced` | All entries `InSync` |
| `lastRemoteSyncAt` | Last successful sync |
| `apiProfileVersion` | API profile fingerprint |
| `lastKnownDuplicateRemotes` | Duplicate remote file count |
| `entries` | `SourceManifestEntry[]` |

### SourceManifestEntry

| Field | Description |
|-------|-------------|
| `relativePath` | Path under `sources/` |
| `localSha256` / `sha256` | Local content hash |
| `remoteSha256` | Remote content hash |
| `baselineSha256` | Last agreed baseline |
| `syncState` | `SourceSyncState` enum |
| `plannedAction` | `SourceSyncAction` enum |
| `remoteFileId`, `remoteFileName` | ChatGPT file refs |
| `lastPushedAt`, `lastPulledAt` | Sync timestamps |
| `manuallyPublishedAt`, `manuallyPublishedSha256` | Manual mode tracking |
| `lastRemoteProbedAt`, `remoteProbeMatch` | Remote probe metadata |

### Sync enums

**SourceSyncState:** `InSync`, `LocalNewer`, `RemoteNewer`, `Conflict`, `LocalOnly`, `MissingRemote`, `RemoteOnly`

**SourceSyncAction:** `Skip`, `Pull`, `PushReplace`, `NeedsResolution`

**SourceConflictResolution:** `None`, `KeepLocal`, `KeepRemote`, `Skip`

---

## ProjectLink

| Field | Description |
|-------|-------------|
| `gizmoId` | Project id |
| `canonicalUrl` | Project URL |
| `playConversationId` | Bound play conversation |
| `lastSyncedAt`, `linkedAt` | Timestamps |

---

## Utility sessions

### GenerationUtilitySession

Per utility job thread: `conversationId`, `sequence`, `seedVersion`, `jobCount`, `consecutiveParseFailures`, `createdAt`, `lastUsedAt`

### GenerationJobId constants

| ID | Purpose |
|----|---------|
| `extract_entities` | Entity extraction |
| `expand_entity` | Expand single entity |
| `propose_memories` | Memory proposals |
| `update_summary` | Rolling summary update |
| `bootstrap_lore` | Initial lore/cards |
| `expand_story_card` | Expand story card |
| `continuity_check` | Continuity warnings |
| `propose_source_edits` | Source file edit proposals |
| `process_turn` | Legacy bundled turn processing |
| `generate_recap` | Obsolete — local recap only |

---

## UiChromeSettings

`ui-chrome.json` — see [user-guide.md](user-guide.md#ui-chrome-persistence).

---

## Libraries

`LibraryStore` kinds: `Scenarios`, `Worlds`, `Characters`, `Presets`, `Templates`

Each kind: `index.json` (`LibraryIndexFile` with `LibraryItem[]`) + `{guid}.json` item payloads.

**LibraryItem:** `id`, `name`, `genre`, `tone`, `tags`, `updatedAt`

---

## Migrations

`AdventureMetadataMigration` runs on every `AdventureStore.Load`:

| Method | Purpose |
|--------|---------|
| `MigrateUtilitySessions` | `EntityUtility` → `UtilitySessions` dict |
| `MigrateGuideSyncedVersions` | Legacy single version → per-job dict |
| `EnsureSettingsDefaults` | Default utility story context settings |
| `MigrateSourcePublishMode` | Schema 2→3 publish mode defaults |

`SourceManifestHelper.MigrateManifest` handles manifest schema bumps.

---

## Related documentation

- [Adventure Panel §6–7](adventure-panel.md)
- [Services Reference](services-reference.md)
- [Instruction vs Sources Paradigm](instruction-sources-paradigm.md)
