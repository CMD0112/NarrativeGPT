# Session state (`update_state`) — workflow design

**Status:** Accepted backlog (AIT-T1-A) — not started  
**Index:** [ai-tools-review-index.md](ai-tools-review-index.md)  
**Tracker:** [ai-tools-backlog-tracker.md](ai-tools-backlog-tracker.md)  
**Context:** [ai-tools-context-matrix.md](ai-tools-context-matrix.md)  
**Related:** [continuity-check-redesign.md](continuity-check-redesign.md) · [strategic-value-additions-tracker.md](strategic-value-additions-tracker.md) (SVA-02)

---

## Decision (2026-07-04)

| Topic | Decision |
|-------|----------|
| Job id | **`update_state`** |
| Catalog | Play AI Tools — post-turn |
| Auto | **`true`** for new settings (`AutoUpdateState`) |
| Review | State proposal queue (new or extend state editor) |
| `objectives` merge | **Append/remove delta** — remove only when exchange explicitly closes an objective |
| `flags` merge | Shallow merge |
| `location` / `time` | Replace when proposed |
| SVA-02 narrator blocks | **Parallel track** — does not block this job |

---

## Problem

`state.json` drives play packets (location, objectives, flags) and is **read** by `continuity_check`, but **no AI Tool proposes updates** from play. Authors edit state manually or rely on narrator prose without structured capture.

---

## Role

Propose **structured deltas** to `state.json` from the scoped exchange — session facts that are neither entity definitions nor discrete memory events nor rolling digest prose.

| Artifact | Owns |
|----------|------|
| `update_state` | Location, objectives, flags, elapsed time, scene tags |
| `extract_entities` | Durable referents (people, places, items as entities) |
| `propose_memories` | Episodic “what happened” bullets |
| `update_summary` | Compressed narrative digest |

---

## Response contract (draft)

JSON object — partial update only; omitted keys unchanged:

```json
{
  "location": "Greyford Gate — inner courtyard",
  "objectives": ["Obtain the writ", "Find Warden Greta"],
  "objectivesRemove": ["Deliver the letter"],
  "flags": { "gateOpen": true, "gretaMissing": true },
  "time": "evening, day 3",
  "rationale": "Exchange establishes party passed the gate; Greta absent from post."
}
```

Empty object `{}` valid when exchange does not affect state.

**Objectives:** prefer `objectives` (add/replace list entries) + optional `objectivesRemove` for explicit closures — avoids ambiguous full-array replacement.

---

## Context (see matrix)

| Layer | Content |
|-------|---------|
| Story block | 1–2 turn pairs; compact entity index (who is present) |
| SIO input | Published `state.json` (authoritative baseline) |
| JC | `=== STATE UPDATE JOB ===` + scope + retrieve instruction |
| Dedup | Omit exchange when SB has transcript |

**Scheduler position:** After `propose_memories`, before `update_summary` (state informs digest and continuity).

---

## Prompt architecture (target)

### Instruction guide

```
You propose updates to session state (location, objectives, flags, time) from scoped play.

State vs other artifacts:
- State (this job): where the party is, what they are trying to do, boolean flags, time hints.
- Entities (other job): durable referents — not current room name if already a place entity unless state tracks "current location" separately.
- Memories (other job): episodic events — not standing location/objective lists.
- Summary (other job): narrative prose — not structured state fields.

Retrieve published state.json before proposing. Merge with exchange; output only changed fields.
Use objectivesRemove when an objective is explicitly completed or abandoned.
```

### Validation (wrapper)

- Reject location/objective changes that contradict accepted `state.json` without rationale
- Merge apply: shallow merge for `flags`; replace strings for `location` / `time`; apply `objectives` / `objectivesRemove` delta

---

## Apply path (expected)

1. Parse JSON object → `StateReviewService.QueueProposal` (new) or extend existing state editor
2. Author accept → merge into `StateDocument` → optional sources export on publish
3. Continuity brief includes pending state proposals when implemented

---

## Source file I/O

| Direction | File |
|-----------|------|
| Input | `state.json` via publish → pointer |
| Output | JSON in assistant reply (no scrape) |

Add `update_state` to `UtilitySourceFileIoCatalog` (input only).

---

## Implementation priority

| P | Item |
|---|------|
| P1 | `GenerationJobId.UpdateState` constant + catalog row |
| P1 | Handler, guide, apply + review queue |
| P1 | SIO input publish; assembler profile row |
| P1 | `GenerationJobScheduler` slot after memories |
| P1 | `AutoUpdateState = true` default on `AdventureMetadata.Settings` |
| P2 | Continuity brief `pendingProposals.state` section |
| P2 | Link from continuity warning → `resolve_continuity_warning` (AIT-T2-F) |

---

*Last updated: 2026-07-04*
