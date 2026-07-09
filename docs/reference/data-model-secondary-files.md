# Data Model — Secondary Files & Indexes

Companion to [data-model-reference.md](data-model-reference.md). Documents **satellite persistence** under each adventure and global diagnostic paths — files not loaded as part of `AdventureBundle` but critical for sync, utility jobs, pointers, and diagnostics.

*Last synced with code: 2026-07-01.*

---

## Adventure folder extensions

Beyond the [bundle files](data-model-reference.md#adventurebundle-aggregate-root), each `adventures/{guid}/` may contain:

```
adventures/{guid}/
├── context-index.json           # ContextIndexDocument — pointer triggers
├── source-history.json          # SourceFileHistoryDocument — export archives index
├── source-manifest.json         # SourceManifest (+ Sections[], CanonChangeNotify)
├── sources/
│   ├── *.md                     # Exportable lore files
│   ├── .history/{file}/{ts}-{sha8}.md
│   └── .project-mirror/         # Probe download cache
│       └── probe-meta.json
├── utility-outbox.json          # Worker-lane queue (UtilityOutboxEntry[])
├── utility-results/             # Per-run JSON artifacts
├── utility-results-index.json   # UtilityJobResultStore index
├── utility-parse-log.jsonl      # Parse diagnostics (optional)
├── thread-logs/
│   └── {threadEntryId}/
│       ├── manifest.json
│       ├── events.jsonl
│       ├── raw/*.json
│       ├── projections/*.json
│       ├── rolling.jsonl
│       ├── snapshots/*.json
│       └── dumps/*.json
├── design-workspace.json        # AdventureDesignWorkspace (design wizard)
└── thread-metadata.json         # Legacy — migrated on load
```

---

## Global config root

`%LocalAppData%\ChatGPTWrapper\` (`AppDirectories.cs`):

| Path | Purpose |
|------|---------|
| `ui-chrome.json` | Browse UI chrome preferences |
| `WebView2UserData/` | Main browser profile (session cookies) |
| `WebView2UserData-AttachWorker/` | Attachment worker profile |
| `AutomationBrowser/` | Headless/automation browser profile |
| `adventures/` | Adventure folders (override via `WrapperSettingsStore`) |
| `libraries/` | Shared scenario/world/character libraries |
| `backups/` | Adventure backup zips |
| `play-send-runs/` | Per-run play-send diagnostic JSON (`PlaySendTrace`) |
| `play-send-trace.jsonl` | Legacy play-send trace log |

---

## context-index.json

**Model:** `ContextIndexDocument` · **Service:** pointer resolution / `ContextPointerResolver`

| Field | Type | Description |
|-------|------|-------------|
| `schemaVersion` | `int` | Currently `1` |
| `entries` | `ContextIndexEntry[]` | Indexed concepts |

**ContextIndexEntry**

| Field | Type | Description |
|-------|------|-------------|
| `id` | `string` | Stable entry id |
| `target` | `string` | Pointer target (section id, entity ref, etc.) |
| `kind` | `string` | e.g. `concept` |
| `triggers` | `string[]` | Lexical triggers for retrieval |

Used when building `[[cgw:sources]]` pointer blocks and local semantic retrieval (SVA-01).

---

## source-manifest.json (extensions)

Full file list: [data-model-reference.md](data-model-reference.md). Additional fields on `SourceManifest` and entries:

### SourceManifest (root)

| Field | Type | Description |
|-------|------|-------------|
| `canonChangeNotify` | `CanonChangeNotifyState` | Active canon-drift banner for author |
| `pendingEntityChangePlans` | `EntityChangePlan[]` | Queued entity rename/sync plans (CMD-232) |

**CanonChangeNotifyState**

| Field | Type |
|-------|------|
| `active` | `bool` |
| `setAt` | `DateTimeOffset?` |
| `triggerSummary` | `string?` |
| `hints` | `CanonChangeHint[]` |
| `unresolvedDrift` | `bool` |

### SourceManifestEntry — sections

| Field | Type | Description |
|-------|------|-------------|
| `sections` | `SectionManifestEntry[]` | Parsed section index per lore file |
| `publishedSectionHashes` | `dict<string,string>` | Per-section publish fingerprints |

**SectionManifestEntry** (`SectionManifestEntry.cs`)

| Field | Type | Description |
|-------|------|-------------|
| `id` | `string` | Section id (used in pointers) |
| `kind` | `string` | Section kind |
| `title` | `string` | Heading title |
| `aliases` | `string[]` | Alternate names |
| `bodyCache` | `string` | Cached section body for injection |
| `keyPhrase` | `string?` | Retrieval hint |
| `sourceEntityId` | `string?` | Linked entity id |
| `pinned` | `bool` | Always-retrieve flag |
| `machineId(fileName)` | method | Stable machine id `{file}#{id}` |

Critical for [injection-policy-adr.md](../adr/injection-policy-adr.md) reference-first assembly.

---

## source-history.json

**Model:** `SourceHistoryDocument` · **Service:** `SourceFileHistoryService`

| Field | Type |
|-------|------|
| `schemaVersion` | `int` (1) |
| `entries` | `SourceFileHistoryEntry[]` |

**SourceFileHistoryEntry**

| Field | Type |
|-------|------|
| `relativePath` | `string` — path under `sources/` |
| `archivedAt` | `DateTimeOffset` |
| `sha256` | `string` |
| `archiveRelativePath` | `string` — under `sources/.history/` |
| `reason` | `string` — e.g. `export` |

---

## sources/.project-mirror/

**Service:** `ProjectSourceProbeService` — download-only probe before compare UI.

| File | Model | Purpose |
|------|-------|---------|
| `probe-meta.json` | `ProbeMetaDocument` | Last probe timestamps, file ids, match state |
| mirrored files | raw | Cached remote content for diff |

Does not mutate ChatGPT Project; used by Source Compare dialog.

---

## utility-outbox.json

**Model:** `UtilityOutboxEntry[]` · **Service:** `UtilityOutboxService`  
**ADR:** [utility-worker-lane-adr.md](../adr/utility-worker-lane-adr.md)

Worker-lane queue for background utility jobs (push prompt → pull response on utility thread).

| Field | Type | Description |
|-------|------|-------------|
| `runId` | `Guid` | Correlates with `utility-results/` |
| `jobId` | `string` | `GenerationJobId` string |
| `channel` | `UtilityExecutionChannel` | Manual vs auto background |
| `state` | `UtilityJobRunState` | Queued → complete / error |
| `lane` | `string` | `UtilityLane.Worker` default |
| `linkedTurnId` / `turnIndex` | `Guid?` / `int?` | Play turn correlation |
| `entityId` / `entityKind` / `cardId` | optional | Job context |
| `sentMessageId` / `assistantMessageId` | `string?` | ChatGPT message ids |
| `streamComplete` | `bool` | SSE finished |
| `partialAssistantText` | `string?` | Truncation recovery |
| `pushError` / `pullError` | `string?` | Lane errors |
| `promptHash` | `string?` | Dedup / audit |
| `queuedAt` / `pushedAt` / `completedAt` | timestamps | Lifecycle |
| `attachments` | `UtilityOutboxAttachment[]?` | Reference files for worker |
| `userPrompt` | `string?` | Frozen prompt text |
| `attachmentReferenceNote` | `string?` | Manifest note for attachments |

---

## utility-results/

**Model:** `UtilityJobRunRecord` · **Store:** `UtilityJobResultStore`

Per-run JSON under `utility-results/{runId}.json` plus `utility-results-index.json` mapping `jobId → runIds`.

| Field | Type | Description |
|-------|------|-------------|
| `runId` | `Guid` |
| `jobId` | `string` |
| `schemaVersion` | `int` |
| `trigger` | `string` | What invoked the job |
| `linkedTurnIndex` | `int?` |
| `conversationId` | `string?` |
| `promptHash` | `string?` |
| `rawResponse` | `string?` | Assistant text before parse |
| `parsedPayload` | `string?` | Normalized JSON |
| `proposalIds` | `Guid[]` | Review queue items |
| `proposalCount` | `int` |
| `error` | `string?` |
| `capturedAt` | `DateTimeOffset` |
| `sentMessageId` / `assistantMessageId` | `string?` |
| `state` | `UtilityJobRunState` |
| `lane` | `string` |
| `streamComplete` | `bool` |
| `pushError` / `pullError` | `string?` |
| `pushedAt` / `reviewResolvedAt` | timestamps |
| `contextManifest` | `UtilityContextManifestRecord?` | CMD-390 context assembly |
| `linkedFlightRecordId` | `Guid?` | SVA-03 flight recorder link |
| `dualRunGroupId` | `Guid?` | Track A/B inference comparison |

---

## design-workspace.json

**Model:** `AdventureDesignWorkspace` — active when adventure status is **Designing**.

| Field | Type |
|-------|------|
| `schemaVersion` | `int` |
| `currentStep` | `AdventureDesignStep` |
| `steps` | `dict<string, DesignStepState>` |
| `createdAt` / `updatedAt` | `DateTimeOffset` |
| `launchBootstrapLore` / `launchStartPlay` | `bool` |
| `sourceFilesPrompted` | `dict<string, DesignSourceFilePromptState>` |
| `pendingBootstrapNotice` | `string?` |

**DesignStepState** includes `fields`, `freeformDraft`, `chatMessages`, `pendingProposals`, `stepSeedSent`, etc.

---

## Thread registry (summary)

Full ADR: [adventure-thread-registry.md](adventure-thread-registry.md). Stored on `AdventureMetadata.threadRegistry` (schema 6).

**AdventureThreadEntry**

| Field | Type |
|-------|------|
| `id` | `Guid` — matches `thread-logs/{id}/` |
| `kind` | `AdventureThreadKind` — Play, Design, UtilityWorker, … |
| `label` | `string` |
| `conversationId` | `string` |
| `bindingTrust` | `PlayThreadBindingTrust` |
| `rejectedConversationId` | `string?` |
| `pinnedTabKey` / `title` / `url` | tab pin metadata |
| `designJobState` | `DesignThreadJobState?` |
| `status` | `Active` / `Archived` |
| `createdAt` / `archivedAt` | timestamps |
| `acceptedTurnCountAtArchive` | `int?` |

Legacy fields on `adventure.json` (`linkedConversationId`, `pinnedPlayTab*`, `utilitySessions`) are **stripped at schema 6** — see [data-model-reference.md](data-model-reference.md#adventuremetadata).

---

## Diagnostic logs

| File | Purpose |
|------|---------|
| `utility-parse-log.jsonl` | Utility response parse failures |
| `thread-logs/.../dumps/` | Manual full conversation JSON exports |
| `play-send-runs/{runId}.json` | Play send pipeline step trace |
| `play-send-trace.jsonl` | Legacy consolidated play-send log |

See [troubleshooting.md](../user/troubleshooting.md) and [thread-conversation-log.md](../developer/thread-conversation-log.md).

---

## Related

- [api-and-data-models-index.md](api-and-data-models-index.md)
- [data-model-reference.md](data-model-reference.md)
- [entity-canon-change-paradigm.md](../user/entity-canon-change-paradigm.md)
- [utility-job-orchestration.md](../developer/utility-job-orchestration.md)
