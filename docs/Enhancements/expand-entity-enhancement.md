# Expand entity — enhancement & naming

**Status:** Design note (2026-07-04)  
**Index:** [ai-tools-review-index.md](ai-tools-review-index.md)  
**Tracker:** [ai-tools-backlog-tracker.md](ai-tools-backlog-tracker.md) · AIT-T3-04  
**Parent:** [entity-extract-update-workflow.md](entity-extract-update-workflow.md)

---

## Naming: there is no `update_entity` job

| Author intent | Job / surface | Response shape |
|---------------|---------------|----------------|
| Auto post-turn: new referents | `extract_entities` → `extractions[]` | [entity-extract-update-workflow.md](entity-extract-update-workflow.md) |
| Auto post-turn: revise existing canon | `extract_entities` → `updates[]` | Same — **not** a separate job id |
| Manual: enrich selected entity(ies) | `expand_entity` sub-action | `updates[]` (multi-select backlog) |
| Design: full file rewrite | `propose_entities_file` | Delimited `entities.json` |
| Internal state (mood, flags) | `propose_entity_state` (AIT-T1-C) | [entity-internal-state-tracker.md](entity-internal-state-tracker.md) |

**Do not add `update_entity` to `GenerationJobId`** unless the one-job-two-section decision is explicitly reversed.

---

## `expand_entity` today

| Aspect | Current |
|--------|---------|
| Trigger | Entity reference panel — single entity |
| Guide key | `extract_entities` |
| Job core | `=== EXPAND ENTITY JOB ===` + target record |
| SIO | `entities.json` input |
| Response | JSON array (one object) |
| Catalog | Not a catalog row — sub-action only |

---

## Enhancement: multi-select enrich (AIT-T3-04)

**Status:** Track (AIT-T3-04) — implement after `extract_entities` dual-section P0

### Target behavior

1. Author selects **one or more** entities in reference panel
2. Single worker round-trip
3. Response reuses **`updates[]` element shape** from extract workflow:

```json
{
  "updates": [
    {
      "id": "warden-greta",
      "entityType": "person",
      "name": "Warden Greta",
      "description": "…",
      "roleOrStatus": "…",
      "rationale": "Author requested enrich from panel."
    }
  ]
}
```

### Context

Same as single `expand_entity` — no transcript; target records inline or via SIO; canon slice for target section when small.

### Apply

Reuse entity review queue — tag as **update** proposals; same apply path as `extract_entities.updates[]`.

---

## Relationship to `extract_entities`

| | `extract_entities` | `expand_entity` |
|--|-------------------|-----------------|
| Scope | Last exchange (auto or manual catalog) | Author-selected entity ids |
| Sections | `extractions` + `updates` | `updates` only (typical) |
| Baseline gate | Required on worker | Required on worker |
| Auto | **On** (`AutoExtractEntities = true`) | **Never** |

Shared: instruction guide methodology for **updates** section; SIO publish list; parser element schema.

---

## Implementation priority

| P | Item |
|---|------|
| P0 | Ship `extract_entities` `{ extractions, updates }` first |
| P1 | Multi-select UI in entity reference panel |
| P1 | Response parser accepts `updates[]` object wrapper for expand path |
| P2 | Optional: unify `BuildExpandEntityPrompt` with update section builder |

---

*Last updated: 2026-07-04*
