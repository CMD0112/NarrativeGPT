# ADR: Thread-canonical play and message edit invalidation

**Status:** Accepted (2026-06-21)  
**Related:** [CMD-164](https://linear.app/cmd0112/issue/CMD-164), [CMD-46](https://linear.app/cmd0112/issue/CMD-46), [CMD-187](https://linear.app/cmd0112/issue/CMD-187), [CMD-188](https://linear.app/cmd0112/issue/CMD-188)

**Refinement epics (2026-06-25):** [CMD-348](https://linear.app/cmd0112/issue/CMD-348) (user message edit reliability across view modes) · [CMD-349](https://linear.app/cmd0112/issue/CMD-349) (narrator revision via composer prompt + message hiding)

## Context

The wrapper stores accepted play turns in `log.json` and per-message records in `thread-metadata.json`, but authors read and edit narrative in the **ChatGPT play thread**. Local-only **Edit turn** / **Undo** mutated wrapper records without changing the thread, causing divergence.

## Decision

1. **Canonical narrative source:** The linked ChatGPT play thread is authoritative for what the model and author see.
2. **Derived cache:** `log.json` and `thread-metadata.json` are derived caches reconciled via Send, edit callbacks, and **Sync from thread**.
3. **Turn pairing:** A play turn is an alternating player (user) + narrator (assistant) pair. Utility messages, injected `[[cgw:]]` context, and design/instruction traffic are excluded (`ConversationStreamParser` filters).
4. **Edit surfaces:** Continuous and Weave overlays expose context-menu edit; legacy `EditTurnDialog` is retired.

## Edit invalidation contract (symmetric)

Editing **either** side of turn *N* invalidates the same timeline tail as native ChatGPT edit:

| Event | ChatGPT thread | Wrapper |
|-------|----------------|---------|
| Edit user message at turn *N* | Overlay-first: in-place native edit in Continuous/Weave ([user-message-edit-adr.md](user-message-edit-adr.md)); peek/native inline in Native view | Update player text; supersede turn *N* metadata; **trim log turns with Index > N**; supersede metadata for turns *N+1…* |
| Edit narrator at turn *N* | **Composer revision primary** ([narrator-revision-adr.md](narrator-revision-adr.md)); native assistant edit not attempted | Update narrator text from captured replacement; same tail invalidation; revision linkage in `thread-metadata.json` |
| Regenerate turn *N* | Native regenerate | Archive prior narrator; same tail invalidation |

**Turn ordinal for markers:** Scoped accepted turn **Index + 1** (1-based) in `[[cgw:invalidation turn="N"]]` and packet meta.

**Transcript assembly:** `PlayTurnScopeService.GetPacketContextTurns` reflects trimmed log after edit; superseded `thread-metadata` messages are excluded via `ActiveMessages`.

## Implementation map

| Layer | Responsibility |
|-------|------------------|
| `cgw-transcript-interactions.js` | Context menu, surrogate edit, native edit automation, bridge posts |
| `TurnInvalidationService` | Resolve turn by `logTurnIndex`, apply text, supersede tail, trim log |
| `ThreadMetadataService` | `BuildLogTurnLinkMap` pushed to WebView |
| `MainWindow.TurnInvalidation` | Bridge handler |

## Consequences

- Authors must use thread-native edit (overlay context menu) instead of local Edit turn.
- Composer revision is primary for narrator edits; **Hide edit prompts** (revision artifacts) uses persisted metadata ([CMD-349](https://linear.app/cmd0112/issue/CMD-349)).
- Overlay-off edits require manual ChatGPT edit + **Sync from thread** or automatic reconcile on next load.
