# ADR: Narrator revision via composer prompt and message hiding

**Status:** Accepted (2026-06-25)  
**Issue:** [CMD-352](https://linear.app/cmd0112/issue/CMD-352)  
**Epic:** [CMD-349](https://linear.app/cmd0112/issue/CMD-349)  
**Plan:** [play-message-edit-refinement-plan.md](play-message-edit-refinement-plan.md)

## Context

ChatGPT does not durably support native assistant inline edit. The wrapper's first batch tried `tryNativeEdit` for **Edit response** before composer fallback, leaving revision clutter (original assistant + revision user prompt visible).

Hiding used ephemeral `sessionStorage` (`__cgwRevisionHideQueue`) keyed by prompt prefix — lost on reload and occasionally hid wrong user lines.

## Decision

### Primary transport

**Composer revision is primary** for **Edit response** in all transcript view modes. `tryNativeAssistantEdit` is not attempted before composer send.

Flow: surrogate panel → `buildRevisionPrompt` → composer send → replacement assistant capture → `postTurnInvalidated`.

### Revision prompt contract

First line: `[[cgw:invalidation turn="N"]]` where *N* is the scoped accepted turn index + 1 (existing packet convention).

Body template (normative prefix for hide matching):

```
For play turn N only: disregard your prior assistant reply for this turn and any later play turns in the thread. Output ONLY the replacement narrator text below with no preamble or commentary.
```

Optional disambiguation: append `(Player line: "<snippet>")` when registry provides `playerSnippet`.

The surrogate panel text is appended after a blank line.

**Anti-patterns (forbidden):** "ignore everything above", "forget the story", embedding full transcript.

### Message taxonomy (`ThreadMessageRecord`)

| Field | Purpose |
|-------|---------|
| `MessageKind` | `play_user`, `play_assistant`, `narrator_revision_prompt`, `narrator_original`, `narrator_replacement`, utility kinds |
| `RevisionGroupId` | UUID linking original + prompt + replacement for one revision |
| `SupersedesMessageId` | Prior `MessageId` this record replaces |
| `HiddenInDisplay` | Existing — `true` for revision prompt and superseded original |
| `LinkedTurnId` | Existing — `TurnRecord.Id` |

### When `postTurnInvalidated` fires

**On replacement assistant capture**, not on composer send.

`submitComposerRevision` sets `__cgwPendingComposerRevision`. When native streaming ends (`noteStreamingLifecycle`), the latest assistant turn text is captured and posted with `reason: composer_revision`.

`TurnInvalidationService` records linkage via `ThreadMetadataService.RecordNarratorComposerRevision`.

### Hiding rule (default on)

When `HideAssistantEditArtifacts` is enabled (`__cgwHideAssistantEditArtifacts`):

- Hide turns with `MessageKind` of `narrator_original` or `narrator_revision_prompt`.
- Hide user turns whose text starts with the revision prompt prefix or contains the turn-scoped revision marker.
- Hide original assistant DOM turn id recorded at revision start.

Metadata-driven entries are pushed as `__cgwRevisionHideEntries` from C# (`ThreadMetadataService.BuildRevisionHideEntries`) and merged with ephemeral session queue during transition.

### Relationship to CMD-46 / CMD-331

- Tail invalidation semantics unchanged ([CMD-46](https://linear.app/cmd0112/issue/CMD-46)).
- Utility hiding ([CMD-331](https://linear.app/cmd0112/issue/CMD-331)) shares `HiddenInDisplay` patterns but separate code paths in v1.

## Implementation map

| Layer | Responsibility |
|-------|----------------|
| `continuous-transcript-view.js` | Primary composer path, deferred invalidation, metadata hide filter |
| `cgw-packet-display.js` | Packet transcript hide parity |
| `weave-transcript-view.js` | Inherited via shared segment builder |
| `ThreadMetadataService` | `RecordNarratorComposerRevision`, `BuildRevisionHideEntries` |
| `TurnInvalidationService` | `composer_revision` reason handling |
| `ChatGptAdventureBridgeInjection` | `ApplyRevisionHideEntriesAsync` |
| `MainWindow.TurnInvalidation` | Push hide entries on load and after invalidation |

## Consequences

- Authors see replacement narrator text only (when hide setting on).
- Revision linkage survives reload via `thread-metadata.json`.
- Model may paraphrase; captured assistant text is canonical in `log.json`.
