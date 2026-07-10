# Memory update / link — workflow design (deferred)

**Status:** Enhancement backlog (AIT-T1-B) — deferred post-P0  
**Index:** [ai-tools-review-index.md](ai-tools-review-index.md)  
**Tracker:** [ai-tools-backlog-tracker.md](ai-tools-backlog-tracker.md)  
**Parent:** [memory-propose-refinement.md](memory-propose-refinement.md)  
**Not in scope for:** Memory propose P0 (append-only array)

---

## Decision (locked)

| Topic | Decision |
|-------|----------|
| New job id | **No** — evolve `propose_memories` with `{ events, links }` sections |
| Priority | P2 — after memory baseline P0 |
| Baseline requirement | Stable memory `id` in baseline for `relatesTo` targets |

---

## Problem

P0 `propose_memories` is **append-only**: each proposal is a new `MemoryEntry`. That fits most events, but play often needs **later memories that connect to earlier ones** without rewriting history:

- Turn 12: “Greta blocked the gate.”
- Turn 28: “Greta was missing from her post — connects to the gate confrontation.”

Entity **updates** merge new facts into an existing record by `id`. Memory “amendments” are usually **not** in-place text edits — they are **new events that reference prior events** (elaboration, payoff, contradiction surfaced later).

---

## Mental model

| Pattern | Entity workflow | Memory workflow (proposed) |
|---------|-----------------|---------------------------|
| New fact | `extractions` → create | `events` → new memory (P0 today) |
| Revise canon record | `updates` by `id` | Rare: `supersedes` / retract (author explicit) |
| Later connection | N/A (same record) | `links` / `continuations` → new memory + `relatesTo` ids |
| Open thread | `roleOrStatus` on entity | `outcome` on event memory (P0) |

---

## Recommendation: dual-section response (future)

Evolve `propose_memories` (or add sibling job — prefer **one job** for same scheduler reasons as entities):

```json
{
  "events": [
    {
      "text": "The party found Greta's badge discarded near the inner wall.",
      "tags": ["discovery", "revelation"],
      "anchor": { "pairOffset": 0 },
      "outcome": "Suggests Greta left voluntarily after the gate incident."
    }
  ],
  "links": [
    {
      "text": "Greta's disappearance now reads as flight, not kidnapping.",
      "relatesTo": ["mem-uuid-gate-block"],
      "linkKind": "elaborates",
      "rationale": "Turn 28 discovery reframes turn 12 gate memory."
    }
  ]
}
```

### `linkKind` vocabulary (draft)

| Kind | Meaning |
|------|---------|
| `elaborates` | Adds detail or interpretation; does not invalidate prior memory |
| `follows` | Direct narrative sequel to prior event |
| `contradicts` | Surfaces tension with prior memory (continuity signal — may feed continuity check) |
| `resolves` | Closes a thread opened in `outcome` of prior memory |
| `supersedes` | Author-facing retraction — rare; may mark prior memory deprecated in UI |

**Not** the same as editing `text` on an accepted entry — links are new rows with graph metadata.

---

## Data model gaps (why deferred)

| Gap | Notes |
|-----|-------|
| `MemoryEntry` | No `RelatesTo`, `LinkKind`, or deprecation flag |
| Review UI | List/detail does not show graph edges |
| Play injection | Only **pinned** memories inject today — links do not surface unless pinned or future “thread” injection |
| Apply path | `ApplyMemoryArray` flat append only |
| Baseline | Index needs stable memory `id` in baseline for `relatesTo` targets |

P0 baseline work ([memory-propose-refinement.md](memory-propose-refinement.md)) should expose **`id` in baseline lines** to prepare for this.

---

## Prompt methodology (when implemented)

1. Retrieve baseline (includes ids).
2. Propose new `events` only for beats not already recorded.
3. Propose `links` when this exchange **reframes, continues, or resolves** a prior memory — cite `relatesTo` ids from baseline.
4. Do not use `links` to duplicate an `events` entry.
5. `supersedes` only when exchange explicitly retcons prior narration (flag for author review).

---

## UI / continuity hooks

- Review hub: show “Relates to: [memory title]” on link proposals.
- Continuity check brief: include memory graph edges for contradiction detection.
- Optional: “memory threads” view (outcome → resolving memory).

---

## Triggers to revisit

- Memory propose P0 (baseline + guide) shipped
- Author feedback: duplicate memories that differ only in framing
- Playtests where later turns need explicit ties to earlier events without pinning everything

---

## Related

- [entity-extract-update-workflow.md](entity-extract-update-workflow.md) — parallel pattern for entities
- [continuity-check-redesign.md](continuity-check-redesign.md) — `recentAcceptedMemories` / pending in brief

---

*Last updated: 2026-07-04*
