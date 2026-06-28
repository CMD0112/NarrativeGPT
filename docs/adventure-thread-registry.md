# Adventure thread registry (CMD-221)

Architecture decision record for multi-thread pin management.

## Decision

Replace singleton play/design/utility pin fields with a **persisted thread registry** on `AdventureMetadata`. **Schema version 6 (CMD-253)** retires legacy singleton fields from steady-state JSON; binding reads and writes go through the registry only.

- **Free-form labels** on design entries (e.g. "Cast", "Framework") — not fixed role slots.
- **Registry is source of truth** — `threadRegistry`, `activeThreadIds`, and per-entry `designJobState`.
- **Design conversation** — active design entry's `ConversationId` and `DesignJobState` (job counters formerly in `UtilitySessions[design_adventure]`).
- **Play conversation** — active play entry's `ConversationId` and pin triple.
- **Utility tab (retired CMD-248/CMD-253)** — play utility jobs run inline on the play thread; design jobs use the design thread. `AdventureThreadKind.Utility` entries are purged on schema-6 migration.
- **`PlayThreadArchive`** — migrated into archived play registry entries; legacy array cleared on save at schema 6.

## Author UX

**Threads hub** (`AdventureThreadManagerDialog`) is the primary surface for project link, play/design pins, handoff, and inline delivery toggles. Play and design cockpits show a compact **Connection** line (`FormatConnectionSummary`) that opens the hub.

## Model

Code: `ChatGPTWrapper/Adventure/Models/AdventureMetadata.cs`, `Adventure/Services/AdventureThreadRegistryService.cs`

| Type | Role |
|------|------|
| `AdventureThreadKind` | `Play`, `Design` (`Utility` legacy — migrated away) |
| `AdventureThreadStatus` | `Active`, `Archived` |
| `AdventureThreadEntry` | Id, kind, label, conversationId, pin triple, status, timestamps |
| `ThreadRegistry` | List of entries on metadata |
| `ActiveThreadIds` | Map kind name → entry Guid |

## Migration (on load)

| Legacy source | Registry target |
|---------------|-----------------|
| `LinkedConversationId` + `PinnedPlayTab*` | Active Play entry |
| `PlayThreadArchive[]` | Archived Play entries |
| `UtilitySessions[design_adventure]` + `PinnedDesignTab*` | Active Design entry (label `"Design"`) |
| `PinnedUtilityTab*` | Cleared on load (CMD-248); not recreated |

**Schema 6 (CMD-253):** After one-way migration, legacy singleton pin fields (`linkedConversationId`, `pinnedPlayTab*`, `pinnedDesignTab*`, `playThreadArchive`, `utilitySessions[design_adventure]`, `projectLink.playConversationId`) are stripped from saved JSON. Old builds can still read adventures until the first save on a schema-6 build.

## Service layer

| Service | Role |
|---------|------|
| `AdventureThreadRegistryService` | CRUD, active selection, archive, `FormatConnectionSummary` |
| `ThreadWebViewResolver` | Runtime WebView selection (`ActivePin`, `RestoreAfterRestart`) |
| `ThreadTabBindingService` | Tab key/title mapping |
| `ThreadTabRestoreService` | Cold-start tab restore |

## Service API

| Method | Role |
|--------|------|
| `EnsureMigrated` | Idempotent legacy → registry migration |
| `GetActiveEntry` / `GetActiveConversationId` | Resolve active thread |
| `BindActiveConversation` | Atomic conversation bind on active entry |
| `ListEntries` | Thread hub lists |
| `RegisterEntry` | New thread slot |
| `SetActivePin` | Switch active; play scope notification |
| `UpdatePinFromWebView` | Bind tab to entry |
| `ArchiveEntry` | Archive; guard active |
| `ReleaseActiveThread` | Rotation prelude |
| `FormatConnectionSummary` | Cockpit / shell connection line |
| `FormatThreadStatus` | Per-kind status lines |

## Consumers

- `PlayTabPinService`, `DesignTabPinService` — pin/read via registry
- `PlayThreadRotationService`, `DesignThreadRotationService` — archive via registry
- `AdventureNavigationService`, `AdventureDesignContextService`, `GenerationJobService`
- `PlayTurnScopeService`, `PlayHandoffService`, `ProjectChatDraftService`
- `AdventureThreadManagerDialog` — **Threads hub** (primary author surface)

## Related

- [CMD-221](https://linear.app/cmd0112/issue/CMD-221) — epic
- [data-model-reference.md](data-model-reference.md) — persistence fields
- [services-reference.md](services-reference.md) — service index
