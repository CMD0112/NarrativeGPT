# AI Tools jobs — living review

**Status:** Living review (started 2026-07-04)  
**Index:** [**AI Tools review — document index**](ai-tools-review-index.md) — hub for all docs in this track  
**Purpose:** Capture the **current state** of every utility / generation job surfaced as **AI Tools** (Play settings → AI Tools tab) so we can review, revise, retire, or add jobs incrementally.

**Companion docs:**

| Doc | Role |
|-----|------|
| [utility-job-e2e-review.md](utility-job-e2e-review.md) | Pipeline / transport / context assembly (CMD-390) |
| [utility-inference-routing-tracker.md](utility-inference-routing-tracker.md) | ChatGPT vs Ollama Track A/B policy |
| [utility-source-file-io.md](utility-source-file-io.md) | Publish → pointer → scrape → delete file loop |
| [developer/utility-job-orchestration.md](../developer/utility-job-orchestration.md) | Dual-lane routing overview |
| [chat-file-io-api-attach-retirement.md](chat-file-io-api-attach-retirement.md) | API attach retired; DOM + source I/O canon |
| [**ai-tools-review-index.md**](ai-tools-review-index.md) | **Hub** — all docs in this review track |
| [entity-extract-update-workflow.md](entity-extract-update-workflow.md) | Extract vs update methodology; one-job two-section recommendation |
| [entity-internal-state-tracker.md](entity-internal-state-tracker.md) | Deferred spike — per-entity developing internal state |
| [continuity-check-redesign.md](continuity-check-redesign.md) | Input source I/O + continuity-brief; keep — refine |
| [memory-propose-refinement.md](memory-propose-refinement.md) | Memory prompt, baseline, fields; auto default on |
| [memory-update-workflow.md](memory-update-workflow.md) | Deferred `events` + `links` memory graph workflow |
| [update-summary-refinement.md](update-summary-refinement.md) | Digest prompt, dedup, auto on, memory index |
| [process-turn-review.md](process-turn-review.md) | Catch-all manual bundle — compose sibling legs |
| [ai-tools-design-segregation.md](ai-tools-design-segregation.md) | **Play vs design AI Tools — separate catalogs** |
| [propose-source-edits-review.md](propose-source-edits-review.md) | Canon markdown edits — design-only |
| [**ai-tools-backlog-tracker.md**](ai-tools-backlog-tracker.md) | Tier 1–3 enhancements (AIT-*); `update_state`; entity naming |
| [**ai-tools-context-matrix.md**](ai-tools-context-matrix.md) | Canonical per-job context matrix |
| [design-ai-tools-context.md](design-ai-tools-context.md) | Design catalog — per-job context |
| [update-state-workflow.md](update-state-workflow.md) | `update_state` — AIT-T1-A |
| [expand-entity-enhancement.md](expand-entity-enhancement.md) | `expand_entity`; no `update_entity` job id |

**Canonical code anchors:** `GenerationJobId`, `GenerationJobGuideService`, `GenerationJobHandlers`, `UtilityJobRouter`, `UtilityWorkerTransitionCatalog`, `UtilitySourceFileIoCatalog`, `UtilityJobPromptBuilder.ComparablePlayAiToolJobIds`.

---

## How to use this document

1. Pick a job from the [master inventory](#master-inventory).
2. Walk prompts, apply path, review UX, and routing against acceptance criteria in [Per-job review template](#per-job-review-template).
3. Update **Review status** and **Open questions / decisions** for that row.
4. Append a line to [Review log](#review-log) when a decision lands or scope changes.

**Review status legend:** `Not started` · `In review` · `Keep as-is` · `Keep — refine later` · `Tentative keep` · `Needs change` · `Pending review (Design)` · `Pending decision` · `Keep internal` · `Draft new variant` · `Retire` · `Deferred` · `Deferred — catch-all candidate`

---

## Pass 1 decisions (2026-07-04)

| Job / group | Decision |
|-------------|----------|
| `update_summary` | **Keep** — earmark refinement (prompt, interval, context window) |
| `continuity_check` | **Keep — refine** — [continuity-check-redesign.md](continuity-check-redesign.md) |
| `bootstrap_lore` + `expand_story_card` | **Retire** — story cards verified legacy vs entities/section injection |
| `propose_source_edits` | **Keep — refine** (design-only) |
| `propose_json_import` | **Keep — refine** (design-only) |
| Design cluster | **Spec'd** — [design-ai-tools-context.md](design-ai-tools-context.md) |
| Play vs design UI | **Segregate entirely** — play AI Tools tab = post-turn jobs only (2026-07-04) |
| Auto defaults (new settings) | Extract, memories, state, summary **on**; continuity **on debounced** — [backlog tracker](ai-tools-backlog-tracker.md) |
| Infra (`synthesize_source`, `utility_worker_ping`) | **Keep internal** — not author AI Tools jobs |
| `generate_recap` | **Retire** (already marked) |

### Story cards vs entities — verification (2026-07-04)

**Conclusion: story cards are legacy; retire card AI Tools jobs.**

| Evidence | Source |
|----------|--------|
| Docs label story cards **(legacy)** — migrate to sections | [instruction-sources-paradigm.md](../user/instruction-sources-paradigm.md) |
| `StoryCardMigrationService` migrates enabled cards → entities + `context-index.json`, disables cards | `StoryCardMigrationService.cs` |
| `story-cards.md` removed from export; UI warns to delete from ChatGPT Project | `SectionedExportTests`, `PlayPromptInjectionDialog.Sources.cs` |
| Replacement model | **Entities** + section injection + context index triggers |

**Follow-up when retiring jobs:** remove/hide Play “Generate cards”, design `LaunchBootstrapLore`, Cards tab AI actions, catalog entry `bootstrap_lore`; keep migration path for existing `cards.json`. Runtime `TriggerStoryCards` may remain until cards UI fully removed.

### Infra jobs (not author AI Tools)

| Job | What it does | Who triggers it |
|-----|----------------|-----------------|
| **`utility_worker_ping`** | Sends a minimal utility-tagged probe to the worker conversation; validates registration, push/pull, and JSON response contract | Threads hub / worker setup (`UtilityWorkerCoordinator`) |
| **`synthesize_source`** | Runs a one-off prompt to draft or rewrite a source file from the source editor (`SourceSynthesisService` → `RunSynthesizeSourceJobAsync`); returns display text, not review queue | Source editor “synthesize” actions |

Neither appears in the AI Tools job catalog; no retirement planned.

**Out of scope for this review track** — documented only; no detailed job review planned.

---

## Executive snapshot (2026-07-04)

| Bucket | Count | Notes |
|--------|------:|-------|
| Job IDs in `GenerationJobId` | 18 | Includes obsolete `generate_recap`, internal `utility_worker_ping` |
| Listed in `GenerationJobId.All` | 13 | Public “generation” set — excludes design, draft, synthesize, ping, recap |
| **AI Tools catalog** (`EditablePlayUtilityJobIds` / `EditableDesignUtilityJobIds`) | 6 play + 9 design | Play settings → Job catalog (play tab); design surface separate |
| Auto post-turn (`GenerationJobScheduler`) | 5 | extract → memories → state → summary (interval) → continuity (debounced) |
| Worker-transition jobs | 12 | Worker-only when transition flag applies; blocks pinned-worker fallback |
| Design-thread routed | 5 | Router checks design set **before** worker transition |
| Source file I/O (`UtilitySourceFileIoCatalog`) | 4 | Always ephemeral utility chat |
| Dual-run comparable (`ComparablePlayAiToolJobIds`) | 12 | Local Ollama QA leg — **not** production default |

**Production posture:** ChatGPT utility worker (or ephemeral per-job chat) owns play AI Tools proposals. Local inference is QA / opt-in only ([utility-inference-routing-tracker.md](utility-inference-routing-tracker.md)).

---

## Play vs design segregation (2026-07-04)

**Decision:** Design-mode AI Tool jobs are **fully segregated** from Play settings → AI Tools.

| Catalog | Jobs | Doc |
|---------|------|-----|
| **Play AI Tools** | `process_turn`, `extract_entities`, `propose_memories`, `update_state`, `update_summary`, `continuity_check` | [ai-tools-design-segregation.md](ai-tools-design-segregation.md) |
| **Design AI Tools** | `design_adventure`, `design_extract_step`, `draft_framework`, `propose_json_import`, `propose_source_edits`, `propose_entities_file`, `bootstrap_sections`, `expand_section`, `audit_canon` | Same |

**Remove from play:** `propose_entities_file`, `propose_source_edits`, `propose_json_import`, `design_adventure`, `design_extract_step`, `bootstrap_lore` (retired).

**Implementation:** Split `EditableUtilityJobIds` → play + design lists; new design AI Tools UI surface.

---

## Master inventory

### Play AI Tools

| Job ID | Display label | Catalog category | In play catalog | Review category | Response shape | Seed v | Worker transition | Source file I/O | Dual-run | Auto post-turn | Review status | Notes |
|--------|---------------|------------------|-----------------|-----------------|----------------|-------:|-------------------|-----------------|----------|----------------|---------------|-------|
| `process_turn` | Process exchange (AI) | Post-turn | Yes | Entity + Memory | JSON object | 1 | Yes | — | Yes | No | Keep — refine | [process-turn-review.md](process-turn-review.md) |
| `extract_entities` | Entities (AI) | Post-turn | Yes | Entity | JSON array → object planned | 2 | Yes | Yes (input) | Yes | Yes (**default on**) | Needs change | [entity-extract-update-workflow.md](entity-extract-update-workflow.md) |
| `propose_memories` | Memories (AI) | Post-turn | Yes | Memory | JSON array | 2 | Yes | — | Yes | Yes (**default on**) | Keep — refine | [memory-propose-refinement.md](memory-propose-refinement.md) |
| `update_summary` | Story digest (AI) | Post-turn | Yes | Summary | Plain text | 1 | Yes | —* | Yes | Yes (interval, **default on**) | Keep — refine | [update-summary-refinement.md](update-summary-refinement.md) |
| `continuity_check` | Continuity (AI) | Post-turn | Yes | ContinuityWarning | JSON object | 1 | Yes | —* | Yes | Yes (**debounced**) | Keep — refine | [continuity-check-redesign.md](continuity-check-redesign.md) |
| `update_state` | Session state (AI) | Post-turn | **Planned** | State | JSON object | — | Yes | state (input) | — | Yes (**default on**) | **Accepted — new** | [update-state-workflow.md](update-state-workflow.md) (AIT-T1-A) |
| `expand_entity` | Expand entity (AI) | — | **No** (sub-action) | Entity | JSON array | 2 | Yes | Yes | Yes | No | Keep manual | [expand-entity-enhancement.md](expand-entity-enhancement.md) |

### Design AI Tools

| Job ID | Display label | In design catalog | Review category | Response shape | Worker | Design lane | Source file I/O | Review status | Notes |
|--------|---------------|-------------------|-----------------|----------------|--------|-------------|-----------------|---------------|-------|
| `design_adventure` | Adventure design (AI) | Yes | — | Conversational + extract | No | **Yes** | — | Keep — refine | [design-ai-tools-context.md](design-ai-tools-context.md) |
| `design_extract_step` | Design extract (AI) | Yes | — | JSON object | No | **Yes** | — | Keep — refine | [design-ai-tools-context.md](design-ai-tools-context.md) |
| `draft_framework` | Draft framework | **No** (adjacent helper) | — | Plain | No | **Yes** | — | Keep internal | [design-ai-tools-context.md](design-ai-tools-context.md) |
| `propose_json_import` | JSON import (AI) | Yes | JsonImport | JSON object | No | **Yes** | — | Keep — refine | [design-ai-tools-context.md](design-ai-tools-context.md) |
| `propose_source_edits` | Source edits (AI) | Yes | SourceEdit | JSON array | Yes | **Yes** | Yes | Keep — refine | [propose-source-edits-review.md](propose-source-edits-review.md) |
| `propose_entities_file` | Entities file (AI) | Yes | Entity (file) | JSON object | Yes | — | Yes | Keep — refine | [design-ai-tools-context.md](design-ai-tools-context.md) |
| `bootstrap_sections` | Canon sections (AI) | Yes | Entity (sections) | JSON array | Yes | — | — | Keep — refine | [design-ai-tools-context.md](design-ai-tools-context.md) |
| `expand_section` | Expand section (AI) | Sub-action | Entity | JSON array | Yes | — | — | Keep — refine | [design-ai-tools-context.md](design-ai-tools-context.md) |
| `audit_canon` | Canon audit (AI) | **Planned** | — | JSON warnings | Yes | **Yes** | Input | **Accepted — new** | [audit-canon-workflow.md](audit-canon-workflow.md) (AIT-T2-D) |

### Retired / internal (neither catalog)

| Job ID | Notes |
|--------|-------|
| `bootstrap_lore` / `expand_story_card` | **Retire** — legacy cards |
| `generate_recap` | **Retire** |
| `synthesize_source` / `utility_worker_ping` | Keep internal |
### Guide key aliasing (`GetUtilityJobId`)

Expand / design extract jobs reuse instruction overrides from their parent job:

| Job ID | Guide / override key |
|--------|----------------------|
| `expand_story_card` | `bootstrap_lore` |
| `expand_section` | `bootstrap_sections` |
| `expand_entity` | `extract_entities` |
| `design_extract_step` | `design_adventure` |

---

## UI & trigger map

### Play settings → AI Tools tab

| Surface | Jobs / behavior |
|---------|-----------------|
| **After each turn** checkboxes | `extract_entities`, `propose_memories`, `update_state` *(planned)*, `update_summary` (interval), `continuity_check` |
| **Job catalog** list | **`EditablePlayUtilityJobIds`** (target **6** jobs when `update_state` ships) — guides, overrides, story-context feed |
| **Run selected action…** | Play catalog jobs only |
| **QA / dual-run** | `ComparablePlayAiToolJobIds` — **play jobs only** (no design ids) |

### Design workspace → AI Tools (new)

| Surface | Jobs / behavior |
|---------|-----------------|
| **Design AI Tools** tab/panel | **`EditableDesignUtilityJobIds`** — guides, run selected action |
| Design thread | `design_adventure`, `design_extract_step`, `draft_framework`, `propose_json_import`, `propose_source_edits` |
| Bootstrap / import menus | `bootstrap_sections`, `propose_json_import`, `propose_entities_file` |

**Removed from play:** design jobs, `propose_entities_file`, `propose_source_edits`, `propose_json_import`, `bootstrap_lore`. Play Sources **Edit with AI** → design workspace ([segregation](ai-tools-design-segregation.md)).

### Other entry points (not in catalog list)

| Entry point | Job ID |
|-------------|--------|
| Play injection / Story tab — Suggest memories | `propose_memories` |
| Play injection / Story tab — Refresh summary | `update_summary` |
| Play injection / Story tab — Generate cards | `bootstrap_lore` |
| Entity reference panel — Expand entity | `expand_entity` |
| Entity reference panel — Expand entity | `expand_entity` |
| Entity reference panel — Revise entities.json | `propose_entities_file` → **design only** after segregation |
| Play header — Run continuity check | `continuity_check` |
| Design workspace | All **Design AI Tools** — see [segregation](ai-tools-design-segregation.md) |
| Source editor — synthesize | `synthesize_source` (via `RunSynthesizeSourceJobAsync`) |
| Threads / worker setup | `utility_worker_ping` |

### Attachment-aware manual launch

`UtilityJobAttachmentLaunchService` supports staged reference files for: `extract_entities`, `expand_entity`, `propose_entities_file`, `propose_json_import`, `propose_source_edits`, `continuity_check`, `synthesize_source`.

---

## Routing quick reference

```
Trigger → UtilityJobRouter
  ├─ Design jobs → DesignThread
  ├─ Worker-transition jobs → WorkerOutbox (blocked if worker unavailable)
  ├─ Dual-run + comparable job → WorkerOutbox (ChatGPT leg)
  ├─ Policy WorkerOnly / WorkerPreferred → WorkerOutbox or Blocked
  └─ Auto post-turn → PlayInjection | WorkerOutbox | PlayLegacyInline
```

| Catalog / group | Typical lane (production) |
|-----------------|---------------------------|
| Worker-transition play jobs | Ephemeral utility chat (recommended) or pinned worker |
| `propose_entities_file` + source I/O jobs | Ephemeral + [utility source file I/O](utility-source-file-io.md) |
| Design jobs | Pinned design thread |
| Auto post-turn (non-transition legacy) | Play injection bundled or inline — **most post-turn jobs are transitioned** |

**Heavy jobs** (`process_turn`, `continuity_check`): prefer worker when policy allows.

---

## Known gaps & review candidates

| # | Topic | Detail | Suggested review action |
|---|-------|--------|-------------------------|
| G1 | **Catalog coverage** | `expand_*`, `bootstrap_sections` not in AI Tools catalog | Add to catalog or document as intentional sub-actions only |
| G2 | **`propose_source_edits` dual home** | Was in play + design catalog | **Resolved** — design-only ([ai-tools-design-segregation.md](ai-tools-design-segregation.md)) |
| G2b | **Catalog segregation** | 11 jobs in one play list | Split play (5) vs design (8) catalogs |
| G3 | **`propose_entities_file` off dual-run list** | File-loop job excluded from `ComparablePlayAiToolJobIds` | Confirm intentional (transport differs) |
| G4 | **`generate_recap` obsolete** | Still in `GenerationJobId`; UI says “no AI” | Remove dead constant / menu copy cleanup |
| G5 | **`synthesize_source` / `draft_framework` invisible** | No catalog entry; internal helpers | Keep internal or promote to catalog |
| G6 | **Field I/O on `extract_entities`** | In source I/O catalog since CMD-441 | Validate prompt + publish path vs attach staging |
| G7 | **Track A promotion** | No job passed dual-run gates (2026-06-29) | Per-job promotion criteria in routing tracker |

---

## Per-job review template

Copy this block under each job section as we review.

```markdown
### `<job_id>` — <display label>

**Review status:** Not started  
**Reviewer / date:** —

#### Purpose (author-facing)
- What problem does this job solve?
- What does the author see in the review queue?

#### Prompt & contract
- Instruction guide (`BuildDefaultInstructionBody`) adequate?
- Job packet / context assembly gaps?
- Response schema matches parser (`IsSettledJobResponse`)?

#### Routing & transport
- Correct lane (worker / ephemeral / design / injection)?
- Source file I/O needed? Attachment staging?

#### Inference
- ChatGPT-only production default OK?
- Dual-run / local leg behavior?

#### Open questions / decisions
- 

#### Acceptance criteria (if changing)
- [ ] 
```

---

## Per-job notes (fill as we go)

### Post-turn cluster

#### `process_turn`
- **Review status:** **Keep — refine** (2026-07-04)
- **Direction:** [process-turn-review.md](process-turn-review.md) — **manual-only**; compose sibling legs; **remove summary leg**; stay in AI Tools catalog.
- Never auto-scheduled.

**Discussion prompts (resolved 2026-07-04):**
1. **Keep vs retire** — **Keep manual** for now.
2. **Summary leg** — **Remove** from bundle.
3. **Catalog** — **Stay in AI Tools**.
4. **Auto** — **Never** (from recommendation).
5. **Composition** — Compose memory + entity sibling prompts (implementation).

#### `extract_entities`
- **Review status:** Needs change (2026-07-04)
- **Direction:** One job, two capture sections (`extractions` + `updates`) — [entity-extract-update-workflow.md](entity-extract-update-workflow.md).
- Baseline via source file → ephemeral pointer (`entities.json`); parser must not treat baseline matches as extractions.
- **Deferred:** internal state — `propose_entity_state` sub-action (AIT-T1-C).

**Discussion prompts (resolved 2026-07-04):**
1. **Redundancy** — **Keep both** — compact index in story block for routing; authoritative `entities.json` via SIO.
2. **Source I/O vs attach** — **SIO canonical** on worker; DOM attach for manual reference files only.
3. **Auto default** — **On** (`AutoExtractEntities = true`).
4. **`propose_entities_file` vs array** — Per-turn **array/queue** in play; **full file** in design (`propose_entities_file`).
5. **`process_turn` overlap** — **Mirror** `{ extractions, updates }`.

#### `update_state`
- **Review status:** **Accepted — new** (AIT-T1-A)
- **Direction:** [update-state-workflow.md](update-state-workflow.md) — `state.json` proposals; auto **on**; after memories, before summary.
- **Not in code** — planned catalog row + `GenerationJobId.UpdateState`.

#### `expand_entity`
- **Review status:** Keep manual (2026-07-04)
- **Enhancement backlog:** Multi-select enrich (AIT-T3-04) — [expand-entity-enhancement.md](expand-entity-enhancement.md).
- Shares guide/session key with `extract_entities`; not the primary extract/update workflow.

#### `propose_memories`
- **Review status:** **Keep — refine** (2026-07-04)
- **Direction:** [memory-propose-refinement.md](memory-propose-refinement.md) — expanded guide, memory baseline, field taxonomy; **auto default on**.
- **Deferred:** link/continuation workflow — [memory-update-workflow.md](memory-update-workflow.md).
- P0 stays append-only JSON array; graph/`links` section is backlog.

**Discussion prompts (resolved 2026-07-04):**
1. **Memory baseline** — **Hybrid** inline index + optional `memory.json` publish when large.
2. **Extract vs amend** — **Append-only P0**; link/`relatesTo` workflow deferred.
3. **Auto default** — **On** for new settings.
4. **`process_turn` overlap** — **Match** this job core when catch-all runs.
5. **Tags / pinned / outcome** — **Keep**; closed tag vocabulary; sparse pin; `outcome` for open threads.

#### `update_summary`
- **Review status:** **Keep — refine** (2026-07-04)
- **Direction:** [update-summary-refinement.md](update-summary-refinement.md) — auto **default on**, 150–250 word guide target, memory-since-revision index, P1 `summary.json` publish, dedup alignment.
- Plain-text digest → Summary review queue; interval default **5** turns.

**Discussion prompts (resolved 2026-07-04):**
1. **Auto default** — **On** for new settings.
2. **Digest length** — **150–250 words** fixed in guide (P0).
3. **Memory index** — **Yes** — accepted memories since last digest revision.
4. **Source I/O** — **P1** — publish `summary.json` when digest > ~2k chars.
5. **`process_turn`** — Summary leg **off** by default.

#### `continuity_check`
- **Review status:** **Keep — refine** (2026-07-04)
- Idea is solid; prompt/job design needs rework for source pointers + cross-job context.
- Auto runs **last** in post-turn scheduler (after extract, memories, optional summary) — good for seeing same-turn proposals in review queue.
- Apply: clears warnings → local `ContinuityService.Analyze` → merge AI warnings; **dismiss-only** + **Resolve with AI** (AIT-T2-F).
- **Gap today:** not in `UtilitySourceFileIoCatalog`; job core inlines SUMMARY/STATE/ENTITY INDEX/RECENT TURNS while lore channel also injects slices — duplication and stale inline risk.
- Attachment launch (`scenario.json`, `state.json`) is DOM attach only — not the publish→pointer canon.

**Refinement direction:** [continuity-check-redesign.md](continuity-check-redesign.md)

**Discussion prompts (resolved 2026-07-04):**
1. **Auto post-turn** — **On with debounce** (`AutoContinuityCheck = true`).
2. **Context duplication** — **Slim JC** + SIO + dedup per [continuity-check-redesign.md](continuity-check-redesign.md).
3. **Local vs AI** — **Hybrid** — keep `ContinuityService.Analyze` + worker AI.
4. **Track B** — **Hybrid** for P0; do not move full semantic continuity local.
5. **Warning semantics** — **Dismiss-only** + **Resolve with AI** hub action.
6. **Scope** — **Session-wide** auto; exchange-scoped manual → P3 backlog.
7. **Source I/O scope** — Publish list per redesign (`entities`, `scenario`, `state`, `summary`, canon md).
8. **Cross-job inputs** — Entity, memory, summary, source-edit pending + `UtilityJobResultStore`.

### Design AI Tools (detail)

#### `propose_entities_file`
- **Keep — refine** — design catalog; full-file loop — [design-ai-tools-context.md](design-ai-tools-context.md).

#### `bootstrap_sections` / `expand_section`
- **Keep — refine** — [design-ai-tools-context.md](design-ai-tools-context.md).

#### `design_adventure` / `design_extract_step` / `draft_framework`
- **Keep — refine** — context in [design-ai-tools-context.md](design-ai-tools-context.md); `draft_framework` adjacent helper only.

#### `propose_json_import`
- **Keep — refine** — [design-ai-tools-context.md](design-ai-tools-context.md).

#### `audit_canon`
- **Accepted — new** (AIT-T2-D) — [audit-canon-workflow.md](audit-canon-workflow.md).

#### `propose_source_edits`
- **Review status:** **Keep — refine** (2026-07-04) — **Design AI Tools only**
- **Direction:** [propose-source-edits-review.md](propose-source-edits-review.md) · [ai-tools-design-segregation.md](ai-tools-design-segregation.md)
- Remove from play catalog and Play Sources “Edit with AI”.
- Source I/O canonical path on design thread / worker.

**Discussion prompts (resolved):**
1. **cast.md** — **Remove** from targets.
2. **Inline fallback** — **Retire** production; keep `ForLocalInference` only.

### Pre-play design cluster

*All jobs in **Design AI Tools** catalog — context in [design-ai-tools-context.md](design-ai-tools-context.md).*

#### `design_adventure` / `design_extract_step` / `draft_framework`
- **Keep — refine** — see design context doc.

#### `propose_json_import`
- **Keep — refine** — see design context doc.

#### `propose_entities_file` / `bootstrap_sections` / `expand_section`
- **Keep — refine** — see design context doc.

### Retired

#### `bootstrap_lore` / `expand_story_card`
- **Retire** (2026-07-04) — legacy story cards; code/UI cleanup deferred.

### Internal / infra

#### `utility_worker_ping`
- Worker setup probe — **keep internal**. See [Infra jobs](#infra-jobs-not-author-ai-tools).

#### `synthesize_source`
- Source editor synthesis helper — **keep internal**. See [Infra jobs](#infra-jobs-not-author-ai-tools).

#### `generate_recap`
- **Retire** — obsolete; local digest only in UI.

---

## Remaining jobs — high-level summaries (pass 1)

*For high-level keep / change / defer / retire decisions. Detailed review follows after decisions land.*

### Post-turn (not yet deep-reviewed)

| Job | One-line purpose | Auto? | Key shape | Decision hooks |
|-----|------------------|-------|-----------|----------------|
| **`update_summary`** | Refresh rolling story digest (plain text) | Yes (every N turns, default 5) | Plain text → Summary review queue | Overlap with `process_turn` optional summary leg; wider context window (8 pairs) than memories/entities |
| **`continuity_check`** | Narrative consistency warnings (no canon writes) | Yes | JSON `{ warnings: [...] }` → ContinuityWarning hub | Heavy job; embeds summary/state/entity index/recent turns in job core; read-only vs proposals |

### World & canon (play)

| Job | One-line purpose | In catalog? | Key shape | Decision hooks |
|-----|------------------|-------------|-----------|----------------|
| **`bootstrap_lore`** | Generate keyword-triggered story cards from scenario | Yes | JSON array of cards | Distinct from entity index; manual via Story tab “Generate cards”; no scoped exchange |
| **`expand_story_card`** | Enrich one existing card | No | JSON array (shares `bootstrap_lore` guide) | Catalog gap — sub-action only? |
| **`propose_source_edits`** | Propose markdown edits to world/plot/cast/instructions | Yes | JSON array of edit ops | **Dual home:** play worker *or* design thread; source file I/O path when `runId` set |
| **`propose_json_import`** | Import scenario fields + entities from sources | Yes | JSON object (multi-part files + optional diff) | Design lane only; seed v3; attachment staging supported |

### Design cluster (defer detailed review)

| Job | One-line purpose | In catalog? | Lane | Decision hooks |
|-----|------------------|-------------|------|----------------|
| **`bootstrap_sections`** | Bootstrap canon sections (cast/world/plot entities) | No | Worker | **Deferred** with design cluster; overlaps entity/section concepts |
| **`expand_section`** | Expand one canon section | No | Worker | Shares `bootstrap_sections` guide; catalog gap |
| **`design_adventure`** | Pre-play design conversation | Yes | Design thread | Not play proposals — conversational |
| **`design_extract_step`** | Extract structured design fields from design chat | Yes | Design thread | JSON object; shares `design_adventure` guide key |
| **`draft_framework`** | Draft scenario/source outlines | No | Design thread | Internal helper; writes `DraftSourcePath` |
| **`propose_entities_file`** | Full `entities.json` revision via file loop | Yes | Ephemeral + scrape | **Deferred** — design/file I/O; overlaps extract/update when file is canonical |

### Manual / infra

| Job | One-line purpose | Decision hooks |
|-----|------------------|----------------|
| **`expand_entity`** | **Decided:** keep manual; multi-select enrich backlog | See extract/update workflow |
| **`synthesize_source`** | Synthesize markdown into a source file from editor | Internal only — promote to catalog? |
| **`utility_worker_ping`** | Worker capability / registration probe | Keep internal |
| **`generate_recap`** | Obsolete AI recap | **Retire** — local digest only in UI |

---

## Review log

| Date | Job(s) | Decision / notes |
|------|--------|------------------|
| 2026-07-04 | *(all)* | Document created from codebase inventory |
| 2026-07-04 | `process_turn` | Review started — bundled job; defer memory/entity/summary contract review to sibling jobs |
| 2026-07-04 | `process_turn` | Earmarked as **catch-all candidate**; final keep/merge/retire decision deferred until remaining jobs reviewed |
| 2026-07-04 | `extract_entities` | Review started — canonical entity job; expand/file/bundle deferred to siblings |
| 2026-07-04 | `extract_entities` / `expand_entity` | **One job, two sections** (`extractions`/`updates`) recommended; `expand_entity` stays manual + multi-select backlog |
| 2026-07-04 | `propose_entities_file`, `bootstrap_sections` | Deferred — design cluster |
| 2026-07-04 | Entity internal state | Spike doc created — out of scope for extract/update P0 |
| 2026-07-04 | `propose_memories` | Review started — canonical memory job |
| 2026-07-04 | Pass 1 | Discussion prompts captured for `process_turn`, `extract_entities`, `propose_memories`; high-level summaries added for remaining jobs |
| 2026-07-04 | Pass 1 decisions | `update_summary` keep+refine; `continuity_check` tentative; card jobs **retire** (verified legacy); design cluster pending review; infra keep internal |
| 2026-07-04 | `continuity_check` | Detailed review started — tentative keep; prompts captured |
| 2026-07-04 | `continuity_check` | **Keep — refine**; [continuity-check-redesign.md](continuity-check-redesign.md) captured |
| 2026-07-04 | Pass-1 (CMD-449) | **Implemented** on branch `cmd-449-ai-tools-pass1` — slices 1–5; 1370 unit tests pass; manual play-session QA pending |
| 2026-07-04 | `propose_memories` | Detailed review resumed — preliminary **Keep** + memory baseline refinement |
| 2026-07-04 | `propose_memories` | **Keep — refine**; [memory-propose-refinement.md](memory-propose-refinement.md); [memory-update-workflow.md](memory-update-workflow.md) deferred |
| 2026-07-04 | `update_summary` | **Keep — refine**; chat decisions in [update-summary-refinement.md](update-summary-refinement.md) |
| 2026-07-04 | `process_turn` | **Keep — refine**; manual-only, remove summary leg, stay in AI Tools — [process-turn-review.md](process-turn-review.md) |
| 2026-07-04 | *(policy)* | **Play vs design segregation** — [ai-tools-design-segregation.md](ai-tools-design-segregation.md) |
| 2026-07-04 | `propose_source_edits` | **Design-only**; keep — refine |
| 2026-07-04 | *(backlog)* | Tier 1–3 accepted — [ai-tools-backlog-tracker.md](ai-tools-backlog-tracker.md); `update_state` naming; context matrix + design context docs |
| 2026-07-04 | *(staged decisions)* | All open questions locked — auto defaults, entity/continuity/source-edit resolutions; see backlog tracker § Staged decisions |

---

*Last updated: 2026-07-04*
