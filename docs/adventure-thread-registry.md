# Adventure thread registry (CMD-221)

Architecture decision record for multi-thread pin management.

## Decision

Replace singleton play/design/utility pin fields with a **persisted thread registry** on `AdventureMetadata`, while keeping **one active pin per `AdventureThreadKind`** and syncing legacy singleton fields from active entries during rollout.

- **Free-form labels** on design entries (e.g. "Cast", "Framework") — not fixed role slots.
- **Registry is source of truth** after migration; `LinkedConversationId`, `PinnedPlayTab*`, `PinnedDesignTab*`, etc. are derived from active entries until all consumers read the registry directly.
- **Design conversation** — active design entry's `ConversationId`; `UtilitySessions[design_adventure]` is a job-counter shim only (CMD-248).
- **Play conversation** — active play entry syncs to `LinkedConversationId` and `ProjectLink.PlayConversationId`.
- **Utility tab (retired CMD-248)** — play utility jobs run inline on the play thread; design jobs use the design thread. Legacy `PinnedUtilityTab*` and `AdventureThreadKind.Utility` entries are cleared on migration and no longer created.
- **`PlayThreadArchive`** — migrated into archived play registry entries; field retained read-only.

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

## Service API

| Method | Role |
|--------|------|
| `EnsureMigrated` | Idempotent migration + legacy sync |
| `GetActiveEntry` / `GetActiveConversationId` | Resolve active thread |
| `ListEntries` | Thread manager lists |
| `RegisterEntry` | New thread slot |
| `SetActivePin` | Switch active; sync legacy + play scope |
| `UpdatePinFromWebView` | Bind tab to entry |
| `ArchiveEntry` | Archive; guard active |
| `ReleaseActiveThread` | Rotation prelude |
| `SyncLegacyFields` | Push active → singleton fields |
| `FormatThreadStatus` | UI status lines |

## Consumers

- `PlayTabPinService`, `DesignTabPinService` — pin/read via registry
- `PlayThreadRotationService`, `DesignThreadRotationService` — archive via registry
- `AdventureNavigationService`, `AdventureDesignContextService`, `GenerationJobService`
- `PlayTurnScopeService`, `PlayHandoffService`, `ProjectChatDraftService`
- `AdventureThreadManagerDialog` — author-facing manager

## Related

- [CMD-221](https://linear.app/cmd0112/issue/CMD-221) — epic
- [data-model-reference.md](data-model-reference.md) — persistence fields
- [services-reference.md](services-reference.md) — service index
