# Entity extract vs update — workflow design

**Status:** Design note (2026-07-04)  
**Index:** [ai-tools-review-index.md](ai-tools-review-index.md)  
**Parent review:** [ai-tools-jobs-review.md](ai-tools-jobs-review.md)  
**Related:** [utility-source-file-io.md](utility-source-file-io.md) · [entity-internal-state-tracker.md](entity-internal-state-tracker.md) · [expand-entity-enhancement.md](expand-entity-enhancement.md) · [ai-tools-backlog-tracker.md](ai-tools-backlog-tracker.md)

---

## Entity update naming

**There is no separate `update_entity` job id.** Canon updates from play use `extract_entities` → `updates[]`. Manual enrich uses `expand_entity`. See [expand-entity-enhancement.md](expand-entity-enhancement.md).

## Decisions (2026-07-04)

| Topic | Decision |
|-------|----------|
| `expand_entity` | **Keep** as manual-only job |
| `expand_entity` enhancement | Support **multi-select enrich** in one job (backlog — not blocking extract/update work) |
| `propose_entities_file` | **Defer** — design-level; full-file revision lane |
| `bootstrap_sections` | **Defer** — design-level; canon section bootstrap |
| Extract vs update | Need a **workflow**; **not necessarily separate jobs** |
| Internal entity state | **Out of scope** — `propose_entity_state` sub-action (AIT-T1-C) |
| Auto post-turn | **Default on** (`AutoExtractEntities = true`) |

---

## Problem

Authors need two distinct behaviors after play:

| Mode | Intent | Must not |
|------|--------|----------|
| **Extract** | Propose **new** world-model referents surfaced in the scoped exchange | Re-propose an entity that already exists in canon (that is an **update**) |
| **Update** | Revise **existing** entities when the exchange adds or changes facts | Invent duplicates; silently fork records |

Today `extract_entities` carries an optional `action: create \| update \| noop` field and apply logic honors `update` by name match — but:

- Default is `create`; matching names still **add duplicates** if action is not `update`.
- The prompt does not separate methodologies; the model sees a compact index **and** may receive full `entities.json` via source I/O without strict routing rules.
- `expand_entity` is the manual “enrich one entity” path; it shares the guide key but uses a different job core.

---

## Recommendation: one job, two capture sections

**Evolve `extract_entities` (name may stay or become `propose_entity_changes`) into a single post-turn job with a structured JSON object response:**

```json
{
  "extractions": [
    {
      "entityType": "place",
      "name": "Greyford Gate",
      "description": "…",
      "roleOrStatus": "…"
    }
  ],
  "updates": [
    {
      "id": "warden-greta",
      "entityType": "person",
      "name": "Warden Greta",
      "description": "…",
      "roleOrStatus": "…",
      "rationale": "Exchange establishes she is missing, not hostile."
    }
  ]
}
```

### Why one job (not two)

| Factor | One job | Two jobs (`extract_*` + `update_*`) |
|--------|---------|--------------------------------------|
| Auto post-turn | **One** worker round-trip | Two sends or complex scheduling |
| Source I/O | Already on `extract_entities` | Duplicate publish/pointer plumbing |
| Context assembly | One scope, one assembler pass | Mostly duplicated |
| Capture distinction | **Required** — split sections in one parser | **Required** — per-job parsers |
| Author manual runs | One “Entities (AI)” action covers both | Authors pick job or we add UI glue |
| Prompt clarity | Two methodologies in one guide | Simpler per-prompt, more jobs to maintain |

The incremental cost of two jobs is **not** two independent pipelines — it is largely the **same** publish → pointer → worker → capture work, plus section routing. That favors consolidating on one job for the automated path.

`expand_entity` remains the **manual, entity-targeted** shortcut (single or multi-select enrich) without re-running full scoped extraction.

---

## Methodology (per section)

### Extractions

- **Input baseline:** published `entities.json` (+ `scenario.json` when present) via [utility source file I/O](utility-source-file-io.md) — TASK-SCOPED pointers, not composer attach.
- **Instruction:** Propose only referents **not** already present in the published baseline (match on stable `id` when available, else normalized `name` + `entityType`).
- **Validation (wrapper):** Reject or reclassify extraction entries that collide with baseline; emit diagnostics when the model mis-files an update as an extraction.
- **Review:** Entity review queue, tagged as **create** proposals.

### Updates

- **Input baseline:** same published `entities.json` — model must retrieve full records for entities it updates.
- **Instruction:** Only entities **implicated** by the scoped exchange; merge new/changed facts; `rationale` cites exchange or source pointer.
- **Required fields:** stable `id` (preferred) or disambiguated `name`; **changed fields only** (partial merge).
- **Validation:** Entry must resolve to an existing record; reject unknown ids.
- **Review:** Same entity review queue, tagged as **update** proposals (apply path already supports `action: update` by name).

### Shared packet structure

Keep existing scope block + story context. Replace ambiguous single array contract with explicit:

```text
=== EXTRACTION JOB ===
Return one JSON object with keys: extractions (array), updates (array).
… methodology for each section …
```

Worker response contract and `IsSettledJobResponse` must accept the object shape (today `extract_entities` expects array at top level for some paths — migration item).

---

## `expand_entity` (manual)

| Now | Target |
|-----|--------|
| Single entity from reference panel | **Keep** manual-only |
| Shares guide with `extract_entities` | **Keep** shared instruction guide key |
| Job core: `=== EXPAND ENTITY JOB ===` | Unchanged until multi-select ships |

**Enhancement (backlog):** multi-select entities → one enrich job; response likely `updates` array with one object per selected entity (reuse update shape).

---

## Deferred (design cluster)

| Job | Revisit when |
|-----|----------------|
| `propose_entities_file` | Design / import workflows; full-file delimited output |
| `bootstrap_sections` | Pre-play or design-time canon section generation |

Do not block extract/update workflow on these.

---

## Implementation phases (suggested)

| Phase | Scope |
|-------|--------|
| **P0 — Contract** | Object response `{ extractions, updates }`; prompt + parser + settle rules; migration from flat array |
| **P1 — Baseline gate** | Require source publish for worker path; parser collision detection against published `entities.json` |
| **P2 — Review UX** | Create vs update badges in entity review queue; optional filter |
| **P3 — `expand_entity` multi-select** | Manual enrich N entities; reuse `updates` element shape |
| **P4 — Internal state** | Separate track — [entity-internal-state-tracker.md](entity-internal-state-tracker.md) |

---

## Decisions (2026-07-04)

| Topic | Decision |
|-------|----------|
| Flat array compatibility | **Yes** — transitional: `[...]` → `extractions` only + diagnostic |
| Id in proposals | **Id-first** on apply; `name` + `entityType` fallback |
| Update field shape | **Changed fields only** (partial merge) |
| Both sections each run | **Always** — empty arrays when none |
| Auto post-turn | **Default on** (`AutoExtractEntities = true`) |
| `process_turn` entity leg | **Mirror** `{ extractions, updates }` |

---

## Open questions

*None — locked 2026-07-04. See [ai-tools-backlog-tracker.md § Staged decisions](ai-tools-backlog-tracker.md#staged-decisions-locked-2026-07-04).*

---

*Last updated: 2026-07-04*
