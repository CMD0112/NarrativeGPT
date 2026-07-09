# Entity canon–state lifecycle

**Status:** Design canon (2026-07-04)  
**Linear:** [CMD-468](https://linear.app/cmd0112/issue/CMD-468) · Epic [CMD-465](https://linear.app/cmd0112/issue/CMD-465)  
**Related:** [canon-reference-paradigm.md](canon-reference-paradigm.md) · [entity-internal-state-model.md](entity-internal-state-model.md) · [entity-canon-change-paradigm.md](../user/entity-canon-change-paradigm.md)

---

## Purpose

Define how **canon profile** (`entities.json` + lore sources) and **mutable play state** (`entity-state.json`) interact during design, play, AI jobs, and author review — without merging layers on disk or in export paths.

This document is the authoritative mapping and lifecycle policy. Implementation issues (seed, guards, UI, jobs) reference tables here rather than duplicating rules.

---

## Three layers

| Layer | Storage | Mutability | Model reference | Primary consumers |
|-------|---------|------------|-----------------|-------------------|
| **Canon profile** | `entities.json` + sectioned lore (`cast.md`, …) | Durable; changes via import, entity extraction, reviewed JSON proposals, manual entity edit + source sync | `canon-format.md` | Design, extract/import jobs, play RAG, entity workspace |
| **Entity play state** | `entity-state.json` | Mutable every scene; AI proposals + manual edits | `entity-state-format.md` | `propose_entity_state`, entity state UI, play packet (CMD-473) |
| **Session state** | `state.json` | Scene/world facts (location, time, weather, roster summary) | Job guide only (`update_state`) | Play packet, continuity |

**Instruction contract** (boundaries, portrayal) is a fourth channel — `instructions-snippet.md` + Project custom instructions — not covered here.

---

## Mutability classes

| Class | Examples | Writable by | Promotion path |
|-------|----------|-------------|----------------|
| **Canon stable** | Name, description, role, personality, useInPlay, party abilities | Entity extraction, JSON import, entity edit → source sync, `propose_canon_evolution` (planned) | N/A — is canon |
| **State ephemeral** | mood, stress, currentLocation, trustTowardPlayer | `propose_entity_state`, manual state form | None — resets or diverges freely |
| **State sticky** | injuries, quest.progress, knows[], obligations[] | Same as ephemeral | **Optional promote** to canon when author marks durable (CMD-474 pipeline) |
| **Session** | current scene location, time of day | `update_state` | Promote to canon only via explicit entity/world edit |

---

## Seed on bind (CMD-466)

When an entity is **first bound** to play tracking (pin, party roster, explicit state record create):

1. Create `entity-state.json` record with `kindId` + `entityId`.
2. **Seed** selected state fields from canon where semantics overlap (see mapping table) — e.g. `presence.currentLocation` from last known canon location if present; `social.disposition` from canon Role tone — not a full copy of description.
3. Mark record `revision = 1`, `updatedAt = now`.
4. Never write seeded state back into `entities.json` automatically.

Seed is **one-shot** at bind; later canon edits do not auto-overwrite diverged state (author may **reset from canon** — CMD-472).

---

## Overlap mapping (canon → state)

Baseline overlaps to resolve in schema and prompts (CMD-469). State paths win at runtime for live play; canon wins for export/import identity.

| Canon field (entities.json) | State block.path | Seed? | Notes |
|-----------------------------|------------------|-------|-------|
| `description` | — | No | Canon only; state uses shorter situational fields |
| `role` / `status` | `social.disposition` | Soft | Seed disposition hint; state may diverge in scene |
| `personality` | `emotional.stability`, `emotional.mood` | Soft | Tone hint only |
| `useInPlay` | — | No | Author OOC guidance; not state |
| Party `abilities` | `tactical.tactics[]` | No | Canon lists capabilities; state lists current stance |
| Party `relationship` | `social.trustTowardPlayer` | Soft | Seed if relationship text parses to trust |
| Location name/description | `presence.currentLocation` (on entities) | Context | Location **entity** state holds occupants/atmosphere |
| Quest `status` (canon) | `quest.progress` (state) | Soft | Canon = design intent; state = live progress |

**Rule:** If the same fact could live in both layers, **pick one home** in the mapping table. Jobs and UI must not duplicate writes.

---

## AI job boundaries

| Job | Writes | Must not write | Guard (CMD-467) |
|-----|--------|----------------|-----------------|
| `extract_entities` | `entities.json` | `entity-state.json` | Reject state-shaped keys in extract patches |
| `propose_entity_state` | `entity-state.json` | Canon profile fields | Canon constraint block in prompt (CMD-470); apply-path validation |
| `propose_json_import` | scenario + entities | state | Schema boundary in apply |
| `propose_canon_evolution` (CMD-474) | Review queue → entities + optional source sync | state | Explicit promotion only |
| `update_state` | `state.json` | entities, entity-state | Session scope only |

`propose_entity_state` prompt includes:

1. **Canon profile (read-only)** — name, role, description excerpt per target  
2. **Entity state format reference** — `entity-state-format.md` inline or from disk  
3. Current state summary + scoped transcript  

---

## Promotion pipeline (canon evolution)

When play reveals a **durable** fact that belongs in canon (identity reveal, permanent injury affecting biography, faction membership):

```mermaid
flowchart LR
    Play[Play / state job] --> Detect[Divergence or author flag]
    Detect --> Propose[propose_canon_evolution]
    Propose --> Review[Proposal review queue]
    Review --> Apply[Apply to entities.json]
    Apply --> Sync[Entity edit source sync]
    Sync --> Export[Refresh export / cast.md]
```

- State record may keep ephemeral copy until author clears or resets.  
- Continuity job (CMD-471) warns when state and canon contradict without a queued promotion.

---

## Play read model (CMD-473)

Play packet **composes** profile + state for the narrator without merging files on disk:

- Entity index from canon (names, ids, pins)  
- State skim from `entity-state.json` for pinned/on-screen entities  
- No inline merge of state into `cast.md` export  

---

## Author UI (CMD-472)

| Action | Effect |
|--------|--------|
| **Promote to canon** | Opens canon evolution proposal from selected state fields |
| **Reset from canon** | Re-seeds mapped fields; clears diverged state blocks (confirm) |
| **Divergence badge** | Shows when state contradicts canon mapping row |

---

## Change checklist

When adding a canon field or state block:

1. Update mapping table in this doc (overlap row or explicit “canon only / state only”).  
2. Update `canon-schema.json` **or** `EntityInternalStateKinds` — not both for the same fact.  
3. Regenerate reference files (`canon-format.md` / `entity-state-format.md`).  
4. Update job guides and seed logic if bind/promotion affected.  
5. Add/adjust tests for apply-path guards.

---

## Related implementation issues

| Issue | Deliverable |
|-------|-------------|
| [CMD-477](https://linear.app/cmd0112/issue/CMD-477) | Design Sources **Generate reference files** |
| [CMD-469](https://linear.app/cmd0112/issue/CMD-469) | Schema notes for baseline vs live labels |
| [CMD-466](https://linear.app/cmd0112/issue/CMD-466) | Seed state from canon on bind |
| [CMD-467](https://linear.app/cmd0112/issue/CMD-467) | Apply-path boundary guards |
| [CMD-470](https://linear.app/cmd0112/issue/CMD-470) | State job canon constraint block |
| [CMD-474](https://linear.app/cmd0112/issue/CMD-474) | `propose_canon_evolution` job |
| [CMD-471](https://linear.app/cmd0112/issue/CMD-471) | Continuity cross-layer warnings |
| [CMD-472](https://linear.app/cmd0112/issue/CMD-472) | Entity UI promote/reset/badges |
| [CMD-473](https://linear.app/cmd0112/issue/CMD-473) | Play packet composed read model |

---

*Last updated: 2026-07-04*
