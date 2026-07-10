# AI Tools — play vs design segregation

**Status:** Design decision (2026-07-04)  
**Index:** [ai-tools-review-index.md](ai-tools-review-index.md)  
**Parent review:** [ai-tools-jobs-review.md](ai-tools-jobs-review.md)

---

## Decision (2026-07-04)

**Segregate design-mode AI Tool jobs entirely** from the Play settings **AI Tools** tab and play-scoped catalogs.

| Surface | Owns |
|---------|------|
| **Play AI Tools** | Post-turn and play-session utility jobs only |
| **Design AI Tools** | Pre-play design thread, import, bootstrap, full-file revision, canon markdown authoring |

**Play catalog size:** **6** jobs when `update_state` ships (5 until then). **Design:** **8** today + **`audit_canon`** (AIT-T2-D) planned.

No job id appears in **both** editable catalogs. Shared `GenerationJobId` constants may remain; **author-facing surfaces and guide defaults** split by mode.

---

## Rationale

| Problem today | Segregation fix |
|---------------|-----------------|
| `EditableUtilityJobIds` mixes 11 jobs across post-turn, file I/O, world/canon, and pre-play design | Play catalog lists **play jobs only** (~5) |
| `propose_source_edits` / `propose_json_import` in play catalog **and** design workspace | **Design catalog only**; play uses entity/sync pipeline for in-session canon |
| `ComparablePlayAiToolJobIds` includes design jobs for dual-run QA | **Play-comparable list** excludes design ids |
| Authors confuse “run during play” vs “shape adventure before play” | Clear mode boundary in UI and docs |
| `UtilityJobRouter.DesignJobs` already routes design lane | Align UI with existing router split |

---

## Job assignment

### Play AI Tools (catalog + post-turn auto)

| Job ID | Display label | Auto? | Notes |
|--------|---------------|-------|-------|
| `process_turn` | Process exchange (AI) | No | Manual composition — [process-turn-review.md](process-turn-review.md) |
| `extract_entities` | Entities (AI) | **Yes** | [entity-extract-update-workflow.md](entity-extract-update-workflow.md) |
| `propose_memories` | Memories (AI) | **Yes** | [memory-propose-refinement.md](memory-propose-refinement.md) |
| `update_state` | Session state (AI) | **Yes** | [update-state-workflow.md](update-state-workflow.md) (planned) |
| `update_summary` | Story digest (AI) | **Yes** (interval 5) | [update-summary-refinement.md](update-summary-refinement.md) |
| `continuity_check` | Continuity (AI) | **Yes** (debounced) | [continuity-check-redesign.md](continuity-check-redesign.md) |

**Sub-actions (play UI, not catalog rows):** `expand_entity`, `propose_entity_state` *(AIT-T1-C planned)* (entity panel).

**Retired from play:** `bootstrap_lore`, `expand_story_card`.

### Design AI Tools (new catalog — design workspace / design settings)

| Job ID | Display label | Lane | Notes |
|--------|---------------|------|-------|
| `design_adventure` | Adventure design (AI) | Design thread | Conversational pre-play |
| `design_extract_step` | Design extract (AI) | Design thread | Structured field extract |
| `draft_framework` | Draft framework | Design thread | Helper — promote to catalog or keep adjacent |
| `propose_json_import` | JSON import (AI) | Design thread | Scenario + entity import |
| `propose_source_edits` | Source edits (AI) | Design thread + source I/O | [propose-source-edits-review.md](propose-source-edits-review.md) — **design-only** |
| `propose_entities_file` | Entities file (AI) | Ephemeral + source I/O | Full `entities.json` revision |
| `bootstrap_sections` | Canon sections (AI) | Worker / design | Section bootstrap |
| `expand_section` | Expand section (AI) | Worker / design | Sub-action; shares guide with bootstrap |

**Retired from design:** `bootstrap_lore`, `expand_story_card` (legacy cards).

### Internal (neither catalog)

| Job ID | Role |
|--------|------|
| `synthesize_source` | Source editor synthesis helper |
| `utility_worker_ping` | Worker probe |
| `generate_recap` | Retire |

---

## UI migration

### Play settings → AI Tools tab (today → target)

| Today (`EditableUtilityJobIds`) | Target |
|----------------------------------|--------|
| 11 mixed jobs | **6 play jobs** when `update_state` ships (`EditablePlayUtilityJobIds`) |
| Design + file I/O + source edit rows | **Removed** from list |
| `bootstrap_lore` | **Removed** (retired) |

### Design workspace (new)

| Surface | Behavior |
|---------|----------|
| **Design → AI Tools** tab (or panel) | `EditableDesignUtilityJobIds` — guides, overrides, run selected action |
| Design thread | Primary transport for conversational + extract jobs |
| Import / bootstrap actions | Stay on design view menus; link to catalog entries |

### Play settings — remove or reroute

| Today | Target |
|-------|--------|
| Sources → **Edit sources with AI** (`propose_source_edits`) | **Remove** from play settings — open Design AI Tools or entity canon pipeline |
| Entity panel → **Revise entities.json** (`propose_entities_file`) | **Design only** — hide from play entity panel or show “open in design” |
| AI Tools → `propose_json_import`, `design_*` | **Removed** from play catalog |

Play-time canon changes during active session:

- **Entities / events / digest** → post-turn play jobs + review queues → existing sources sync on accept
- **Markdown source files** → design-mode `propose_source_edits` when author switches to design context, not mid-play catalog

---

## Code touchpoints (implementation)

| Area | Change |
|------|--------|
| `GenerationJobGuideService` | Split `EditableUtilityJobIds` → `EditablePlayUtilityJobIds` + `EditableDesignUtilityJobIds`; category helpers per mode |
| `PlayPromptInjectionDialog` | `BindAiActions()` uses play list only |
| `AdventureDesignView` (+ settings) | New design AI Tools binder |
| `UtilityJobPromptBuilder.ComparablePlayAiToolJobIds` | Remove design job ids |
| `EntityReferencePanel` | `ShowAiActions` — drop `propose_entities_file` from play (or design-gated) |
| `PlayPromptInjectionDialog.EditSourcesAi_Click` | Remove or redirect to design |
| Tests | `GenerationJobGuideTests`, catalog metadata per mode |
| Docs | [ai-tools-jobs-review.md](ai-tools-jobs-review.md) inventory split |

**No new job ids required** for P0 segregation — surface + catalog split only.

Optional P2: rename categories in play UI (“Post-turn” only); design categories (“Pre-play”, “Import”, “File I/O”, “Canon sources”).

---

## Router alignment (already correct)

```text
UtilityJobRouter.DesignJobs → DesignThread
  design_adventure, design_extract_step, draft_framework,
  propose_json_import, propose_source_edits
```

Worker-transition jobs (`bootstrap_sections`, `propose_entities_file`, …) run on **design-initiated** dispatch when listed only in design catalog.

---

## Review doc impacts

| Doc | Update |
|-----|--------|
| [propose-source-edits-review.md](propose-source-edits-review.md) | **Design-only**; drop play-context P1 |
| [entity-extract-update-workflow.md](entity-extract-update-workflow.md) | Play path for entity canon; not `propose_entities_file` during play |
| [process-turn-review.md](process-turn-review.md) | Unchanged — play manual job |
| Design cluster jobs | Review under **Design AI Tools** track, not play pass |

---

## Implementation priority

| P | Item |
|---|------|
| P0 | Split editable job id lists; play AI Tools tab shows **6** jobs when `update_state` ships (5 until then) |
| P0 | Remove design jobs from `ComparablePlayAiToolJobIds` |
| P1 | Design AI Tools UI surface + guide editing |
| P1 | Reroute/remove play Sources “Edit with AI” |
| P2 | Entity panel `propose_entities_file` → design entry only |

---

*Last updated: 2026-07-04*
