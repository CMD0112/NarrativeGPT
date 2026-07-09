# AI Tools review — document index

**Status:** Maintained hub (session started 2026-07-04)  
**Purpose:** Single entry point for the job-by-job AI Tools review and all design notes produced in that session.

> **Start here.** For the full job table, prompts, and review log → [ai-tools-jobs-review.md](ai-tools-jobs-review.md).

---

## Quick navigation

| I want to… | Go to |
|------------|--------|
| See all jobs and decisions | [Living review](ai-tools-jobs-review.md) |
| Track backlog & staged decisions | [Backlog tracker](ai-tools-backlog-tracker.md) § [Staged decisions](ai-tools-backlog-tracker.md#staged-decisions-locked-2026-07-04) |
| Implementation plan & Linear issues | [Implementation plan](ai-tools-implementation-plan.md) · epic [CMD-449](https://linear.app/cmd0112/issue/CMD-449) |
| Context requirements per job | [Context matrix](ai-tools-context-matrix.md) |
| Understand play vs design split | [Design segregation](ai-tools-design-segregation.md) |
| Implement entity extract/update | [Entity workflow](entity-extract-update-workflow.md) |
| Entity update naming (`updates[]` vs `update_entity`) | [Expand entity enhancement](expand-entity-enhancement.md) |
| Implement a specific play job | [Play job docs](#play-ai-tools--design-docs) below |
| Implement design-mode jobs | [Design context](design-ai-tools-context.md) |
| Source file I/O mechanics | [utility-source-file-io.md](utility-source-file-io.md) |

---

## Documents produced (this review session)

**Hubs & policy:** 2 hubs, 1 policy, 1 backlog tracker, 1 context matrix, 1 design context doc.  
**Play refinements:** 6 job docs + 2 entity/memory backlog spikes.  
**Accepted backlog (2026-07-04):** Tier 1–3 workflow docs — see [backlog tracker](ai-tools-backlog-tracker.md).

```
docs/Enhancements/
├── ai-tools-review-index.md          ← YOU ARE HERE (hub / catalog)
├── ai-tools-jobs-review.md           ← living review (inventory + log)
├── ai-tools-backlog-tracker.md       ← Tier 1–3 + enhancement tracker (AIT-*)
├── ai-tools-context-matrix.md        ← canonical per-job context matrix
├── ai-tools-design-segregation.md    ← policy: play vs design catalogs
├── design-ai-tools-context.md        ← design job context (8 jobs + backlog)
│
├── entity-extract-update-workflow.md ← extract_entities (needs change)
├── expand-entity-enhancement.md      ← expand_entity; no update_entity job id
├── entity-internal-state-tracker.md  ← propose_entity_state (AIT-T1-C)
├── memory-propose-refinement.md      ← propose_memories (keep — refine)
├── memory-update-workflow.md         ← events + links (AIT-T1-B)
├── update-summary-refinement.md      ← update_summary (keep — refine)
├── update-state-workflow.md          ← update_state (AIT-T1-A) NEW
├── continuity-check-redesign.md      ← continuity_check (keep — refine)
├── process-turn-review.md            ← process_turn (keep — refine, manual)
├── propose-source-edits-review.md    ← propose_source_edits (design-only)
├── audit-canon-workflow.md           ← audit_canon (AIT-T2-D)
├── refresh-context-index-workflow.md ← refresh_context_index (AIT-T2-E)
└── resolve-continuity-warning-workflow.md ← AIT-T2-F (complex)
```

---

## Document catalog

### Hubs & policy

| File | Type | Summary |
|------|------|---------|
| [ai-tools-review-index.md](ai-tools-review-index.md) | **Hub** | This index — navigation, catalog, job→doc map |
| [ai-tools-jobs-review.md](ai-tools-jobs-review.md) | **Living review** | Master inventory (play + design tables), UI map, routing, per-job notes, [review log](ai-tools-jobs-review.md#review-log) |
| [ai-tools-backlog-tracker.md](ai-tools-backlog-tracker.md) | **Tracker** | Tier 1–3 accepted items (AIT-*); catalog impact; entity naming |
| [ai-tools-context-matrix.md](ai-tools-context-matrix.md) | **Canon** | Lane-aware context matrix — supersedes draft in utility-job-context-assembly |
| [ai-tools-design-segregation.md](ai-tools-design-segregation.md) | **Policy** | Play AI Tools (6 target) vs Design AI Tools (8+1 planned); UI migration |
| [design-ai-tools-context.md](design-ai-tools-context.md) | **Design canon** | Per-job context for all design catalog jobs |

### Play AI Tools — design docs

| File | Job ID | Decision | Key topics |
|------|--------|----------|------------|
| [entity-extract-update-workflow.md](entity-extract-update-workflow.md) | `extract_entities` | **Needs change** | One job, `{ extractions, updates }`; source I/O baseline |
| [expand-entity-enhancement.md](expand-entity-enhancement.md) | `expand_entity` | **Track** | Multi-select enrich; clarifies no `update_entity` job id |
| [update-state-workflow.md](update-state-workflow.md) | `update_state` | **Accepted — new** | AIT-T1-A; `state.json` proposals from play |
| [memory-propose-refinement.md](memory-propose-refinement.md) | `propose_memories` | **Keep — refine** | Memory baseline (hybrid); tag taxonomy; auto **on** |
| [update-summary-refinement.md](update-summary-refinement.md) | `update_summary` | **Keep — refine** | 150–250 words; memory-since-revision index; auto **on**; P1 `summary.json` |
| [continuity-check-redesign.md](continuity-check-redesign.md) | `continuity_check` | **Keep — refine** | Input source I/O; `continuity-brief.json`; scheduling |
| [process-turn-review.md](process-turn-review.md) | `process_turn` | **Keep — refine** | Manual-only; compose sibling legs; remove summary leg |

### Design AI Tools — design docs

| File | Job ID(s) | Decision | Key topics |
|------|-----------|----------|------------|
| [design-ai-tools-context.md](design-ai-tools-context.md) | All design catalog jobs | **Canon** | Per-job context; replaces pending-doc list |
| [propose-source-edits-review.md](propose-source-edits-review.md) | `propose_source_edits` | **Keep — refine** | Design-only; publish/pointer/scrape; remove play Sources AI |
| [audit-canon-workflow.md](audit-canon-workflow.md) | `audit_canon` | **Accepted — new** | AIT-T2-D; pre-play canon audit |
| [refresh-context-index-workflow.md](refresh-context-index-workflow.md) | `refresh_context_index` | **Accepted** | AIT-T2-E; context-index maintenance |

### Backlog & deferred spikes

| File | Relates to | Status |
|------|------------|--------|
| [ai-tools-backlog-tracker.md](ai-tools-backlog-tracker.md) | All tiers | **Master tracker** — AIT-T1-A through AIT-T3-04 |
| [memory-update-workflow.md](memory-update-workflow.md) | `propose_memories` | AIT-T1-B — `{ events, links }` graph |
| [entity-internal-state-tracker.md](entity-internal-state-tracker.md) | `propose_entity_state` | AIT-T1-C — per-entity internal state |
| [resolve-continuity-warning-workflow.md](resolve-continuity-warning-workflow.md) | Continuity hub | AIT-T2-F — composition job; high complexity |

### Related canon (pre-existing; not authored in this session)

| File | Relevance to AI Tools review |
|------|------------------------------|
| [utility-source-file-io.md](utility-source-file-io.md) | Publish → pointer → scrape — entity, source-edit, continuity inputs |
| [utility-job-e2e-review.md](utility-job-e2e-review.md) | Pipeline / transport / context assembly (CMD-390) |
| [utility-inference-routing-tracker.md](utility-inference-routing-tracker.md) | ChatGPT vs Ollama Track A/B |
| [utility-job-context-assembly.md](utility-job-context-assembly.md) | Assembler v1 — see [ai-tools-context-matrix.md](ai-tools-context-matrix.md) for current matrix |
| [chat-file-io-api-attach-retirement.md](chat-file-io-api-attach-retirement.md) | DOM attach vs source I/O boundary |
| [utility-worker-attachment-delivery.md](utility-worker-attachment-delivery.md) | Manual reference file attach |
| [developer/utility-job-orchestration.md](../developer/utility-job-orchestration.md) | Dual-lane routing overview |

---

## Job → document map

### Play catalog (target: 5 jobs → 6 with `update_state`)

| Job ID | Label | Review status | Design doc |
|--------|-------|---------------|------------|
| `process_turn` | Process exchange (AI) | Keep — refine | [process-turn-review.md](process-turn-review.md) |
| `extract_entities` | Entities (AI) | Needs change | [entity-extract-update-workflow.md](entity-extract-update-workflow.md) |
| `propose_memories` | Memories (AI) | Keep — refine | [memory-propose-refinement.md](memory-propose-refinement.md) · [memory-update-workflow.md](memory-update-workflow.md) (AIT-T1-B) |
| `update_state` | Session state (AI) | **Accepted — new** | [update-state-workflow.md](update-state-workflow.md) (AIT-T1-A) |
| `update_summary` | Story digest (AI) | Keep — refine | [update-summary-refinement.md](update-summary-refinement.md) |
| `continuity_check` | Continuity (AI) | Keep — refine | [continuity-check-redesign.md](continuity-check-redesign.md) |
| `expand_entity` | Expand entity (AI) | Keep manual | [expand-entity-enhancement.md](expand-entity-enhancement.md) |
| `propose_entity_state` | Entity state (AI) | Planned sub-action | [entity-internal-state-tracker.md](entity-internal-state-tracker.md) (AIT-T1-C) |

### Design catalog (target: 8 jobs)

| Job ID | Label | Review status | Design doc |
|--------|-------|---------------|------------|
| `design_adventure` | Adventure design (AI) | Keep — refine | [design-ai-tools-context.md](design-ai-tools-context.md) |
| `design_extract_step` | Design extract (AI) | Keep — refine | [design-ai-tools-context.md](design-ai-tools-context.md) |
| `draft_framework` | Draft framework | Keep internal (adjacent) | [design-ai-tools-context.md](design-ai-tools-context.md) |
| `propose_json_import` | JSON import (AI) | Keep — refine | [design-ai-tools-context.md](design-ai-tools-context.md) |
| `propose_source_edits` | Source edits (AI) | Keep — refine | [propose-source-edits-review.md](propose-source-edits-review.md) |
| `propose_entities_file` | Entities file (AI) | Keep — refine | [design-ai-tools-context.md](design-ai-tools-context.md) |
| `bootstrap_sections` | Canon sections (AI) | Keep — refine | [design-ai-tools-context.md](design-ai-tools-context.md) |
| `expand_section` | Expand section (AI) | Keep — refine | [design-ai-tools-context.md](design-ai-tools-context.md) |
| `audit_canon` | Canon audit (AI) | **Accepted — new** | [audit-canon-workflow.md](audit-canon-workflow.md) (AIT-T2-D) |

### Retired / internal

| Job ID | Status |
|--------|--------|
| `bootstrap_lore`, `expand_story_card` | **Retire** (legacy story cards) |
| `generate_recap` | **Retire** |
| `utility_worker_ping`, `synthesize_source` | Keep internal |

---

## Decisions snapshot (2026-07-04)

| Area | Decision |
|------|----------|
| **Segregation** | [Play vs design catalogs entirely separate](ai-tools-design-segregation.md) |
| **Story cards** | Retire `bootstrap_lore` / `expand_story_card` |
| **Entities** | One job, dual-section extract/update; auto **on**; no `update_entity` id |
| **Memories** | Keep — refine; auto on; link workflow AIT-T1-B deferred |
| **State** | **`update_state`** accepted (AIT-T1-A); auto on |
| **Summary** | Keep — refine; auto on; 150–250 words |
| **Continuity** | Keep — refine; auto on **debounced**; SIO + brief; Resolve with AI (AIT-T2-F) |
| **Process turn** | Keep manual; compose legs; no summary; stay in play catalog |
| **Source edits** | Design-only; `cast.md` removed; SIO canonical |
| **Backlog** | Tier 1–3 locked — [ai-tools-backlog-tracker.md](ai-tools-backlog-tracker.md) § Staged decisions |
| **Context** | [ai-tools-context-matrix.md](ai-tools-context-matrix.md) canonical |

Full log: [ai-tools-jobs-review.md § Review log](ai-tools-jobs-review.md#review-log).

---

## Suggested reading order

1. [ai-tools-design-segregation.md](ai-tools-design-segregation.md) — scope boundary  
2. [ai-tools-jobs-review.md](ai-tools-jobs-review.md) — inventory + pass 1 decisions  
3. Play jobs (any order): entity → memory → summary → continuity → process_turn  
4. [ai-tools-context-matrix.md](ai-tools-context-matrix.md) — context per job  
5. Design: [design-ai-tools-context.md](design-ai-tools-context.md) · [propose-source-edits-review.md](propose-source-edits-review.md)  
6. Backlog: [ai-tools-backlog-tracker.md](ai-tools-backlog-tracker.md)

---

## Code anchors

| Area | Location |
|------|----------|
| Job IDs | `ChatGPTWrapper/Adventure/Models/GenerationJobId.cs` |
| Play catalog (today) | `GenerationJobGuideService.EditableUtilityJobIds` |
| Prompts / apply | `GenerationJobHandlers.cs`, `EntityExtractionService.cs`, `RecapService.cs` |
| Routing | `UtilityJobRouter.cs`, `UtilityWorkerTransitionCatalog.cs` |
| Source I/O catalog | `UtilitySourceFileIoCatalog.cs` |
| Play AI Tools UI | `PlayPromptInjectionDialog.xaml` → AI Tools tab |

---

## Maintaining this index

1. New review-track doc → add to [Document catalog](#document-catalog) and [Files produced](#documents-produced-this-review-session).  
2. Job status change → update [Job → document map](#job--document-map) and [living review log](ai-tools-jobs-review.md#review-log).  
3. New policy → add under Hubs & policy; link from [ai-tools-jobs-review.md](ai-tools-jobs-review.md) companion table.  
4. Update `docs/INDEX.md` Enhancements section when adding files.

---

## Session log

| Date | Change |
|------|--------|
| 2026-07-04 | Review track started; [ai-tools-jobs-review.md](ai-tools-jobs-review.md) created |
| 2026-07-04 | [entity-extract-update-workflow.md](entity-extract-update-workflow.md), [entity-internal-state-tracker.md](entity-internal-state-tracker.md) |
| 2026-07-04 | Pass 1 decisions; card jobs retire |
| 2026-07-04 | [continuity-check-redesign.md](continuity-check-redesign.md) |
| 2026-07-04 | [memory-propose-refinement.md](memory-propose-refinement.md), [memory-update-workflow.md](memory-update-workflow.md) |
| 2026-07-04 | [update-summary-refinement.md](update-summary-refinement.md) |
| 2026-07-04 | [process-turn-review.md](process-turn-review.md) |
| 2026-07-04 | [propose-source-edits-review.md](propose-source-edits-review.md) |
| 2026-07-04 | [ai-tools-design-segregation.md](ai-tools-design-segregation.md) — play/design split |
| 2026-07-04 | Index reorganized — full catalog + job map |
| 2026-07-04 | Backlog tracker, context matrix, design context doc; Tier 1–3 workflow docs; `update_state` accepted |
| 2026-07-04 | **Staged decisions locked** — all TBDs resolved; see [backlog tracker § Staged decisions](ai-tools-backlog-tracker.md#staged-decisions-locked-2026-07-04) |

---

*Last updated: 2026-07-04*
