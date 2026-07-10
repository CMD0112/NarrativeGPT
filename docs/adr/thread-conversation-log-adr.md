# ADR: Thread conversation log (per-registry-thread)

**Status:** Accepted (2026-06-29)  
**Supersedes (over time):** `log.json`, `thread-metadata.json` as local play record  
**Related:** [play-thread-canonical-adr.md](play-thread-canonical-adr.md)  
**Developer guide:** [thread-conversation-log.md](../developer/thread-conversation-log.md)

## Context

The wrapper stored accepted play turns in `log.json` and a message index in `thread-metadata.json`. Edits trimmed the log tail and soft-superseded metadata, losing edit history. Storage was per-adventure, not per registry thread. Multiple thread kinds (Play, Design, UtilityWorker) share one adventure folder but had no unified logging model.

## Decision

1. **Per-thread storage:** Each [`AdventureThreadEntry`](../reference/adventure-thread-registry.md) gets `thread-logs/{threadEntryId}/` under the adventure directory.
2. **Rolling append-only log:** `rolling.jsonl` records every message on the active branch; superseded messages remain with `status: superseded` and audit lines.
3. **Stable indexing:** `branchIndex` (active-branch position), `nodeId` (mapping key), `messageId` (ChatGPT id when present), monotonic `ordinal`.
4. **API-first sync:** `ConversationBranchExtractor` walks the active mapping branch; rolling sync runs after send, invalidation, session load, and manual dump.
5. **Manual dump:** Authors can write raw conversation JSON to `dumps/{timestamp}-conversation.json`.
6. **Replacement path:** Consumers migrate to read active branch from thread log; legacy files are retired in later phases.

## On-disk layout

```
adventures/{adventureId}/thread-logs/{threadEntryId}/
  manifest.json
  rolling.jsonl
  dumps/
```

## Consequences

- Edit history is preserved in JSONL (superseded entries + audit lines).
- All registry kinds (Play, Design, UtilityWorker) use the same logging service.
- `prompt-history.json` remains the per-send packet flight recorder (orthogonal).
- DOM-only capture uses synthetic `dom:{n}` node ids when API fetch fails.
