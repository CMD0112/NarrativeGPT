# AI Tools — context matrix (canonical)

**Status:** Design canon (2026-07-04) — supersedes draft table in [utility-job-context-assembly.md](utility-job-context-assembly.md)  
**Index:** [ai-tools-review-index.md](ai-tools-review-index.md)  
**Backlog:** [ai-tools-backlog-tracker.md](ai-tools-backlog-tracker.md)  
**Implementation:** `UtilityJobContextAssembler`, `UtilityStoryContextProfiles`, `GenerationJobHandlers`

Unified **lane-aware** context requirements for every utility / AI Tools job. Locked settings: [ai-tools-backlog-tracker.md § Staged decisions](ai-tools-backlog-tracker.md#staged-decisions-locked-2026-07-04).

---

## Cross-cutting rules

| Rule | Detail |
|------|--------|
| **Worker solo** | Self-contained story block + worker lore; never assume play thread visibility |
| **Play bundled** | Delta-only vs `PlayPacketContextSnapshot`; omit transcript/summary already in narrator packet |
| **Source I/O** | Authoritative JSON/markdown via publish → TASK-SCOPED pointer — not DOM attach for programmatic jobs |
| **Job core** | Task-specific baselines only — avoid triple-inlining summary + state + transcript when story block or SIO covers them |
| **Dedup** | `UtilityStoryContextDedup` + handler consolidation — see [continuity-check-redesign.md](continuity-check-redesign.md) |

**SB** = assembler story context block · **JC** = job-core-only sections · **SIO** = `UtilitySourceFileIoCatalog` input/output

---

## Play AI Tools — current + accepted

| Job ID | Transcript (SB) | Summary | State | Entity index | Memory baseline | SIO inputs | Canon slices | Cross-job brief | JC sections |
|--------|:---------------:|:-------:|:-----:|:------------:|:---------------:|:----------:|:------------:|:---------------:|-------------|
| `process_turn` | 2 pairs | — | — | — | Yes | entities, scenario | Mentioned | — | Compose memory + entity legs |
| `extract_entities` | 1 pair | — | — | Compact | — | entities, scenario | Mentioned | — | Dual-section contract + scope |
| `propose_memories` | 1 pair | — | — | — | **Required** | memory (large) | — | — | Baseline + scope + exchange† |
| `update_summary` | 8 pairs | Prior digest | — | — | Since-revision index | summary (large) | — | — | Digest job + memory index† |
| `continuity_check` | 6–8 pairs | Yes | Yes | Full compact | Recent accepted | entities, scenario, state, summary, canon md | **Task-scoped** | **Required** | Pointers + brief only |
| `expand_entity` | — | — | — | Target only | — | entities | Target inline | — | Expand job + target record |
| `update_state` *(AIT-T1-A)* | 1–2 pairs | — | Current via SIO | Compact (presence) | — | state.json | — | — | State update job + scope |

† Omit exchange / recent turns when SB already contains transcript (`UtilityStoryContextDedup`).

### Play sub-actions & enhancements

| Surface | Context notes | Doc |
|---------|---------------|-----|
| `expand_entity` multi-select | Same as single; `updates[]` per entity | [expand-entity-enhancement.md](expand-entity-enhancement.md) |
| `extract_entities.updates[]` | Not a separate job — section of extract job | [entity-extract-update-workflow.md](entity-extract-update-workflow.md) |

---

## Design AI Tools

No play transcript or rolling summary unless author scopes a design-thread turn. See [design-ai-tools-context.md](design-ai-tools-context.md) for per-job JC detail.

| Job ID | Scenario fields | Published sources | SIO | Design thread history | User prompt |
|--------|:---------------:|:-----------------:|:---:|:---------------------:|:-----------:|
| `design_adventure` | First turn only | Optional pointers | — | **Primary** | Required |
| `design_extract_step` | Current snapshot | — | — | Implicit | Step extract |
| `draft_framework` | Excerpt | Template pointers (P1) | — | Implicit | Optional |
| `propose_json_import` | Snapshot for diff | Staged imports | Input | — | Import intent |
| `propose_source_edits` | — | canon md files | In + out scrape | — | Edit intent |
| `propose_entities_file` | Optional | entities, scenario, context-index | In + out scrape | — | Revision intent |
| `bootstrap_sections` | Inline (P0) | scenario (P1) | — | — | — |
| `expand_section` | — | entities (P1) | Input | — | Target section |
| `audit_canon` *(AIT-T2-D)* | Full | All canon md + entities | Input | — | Optional scope |

---

## Internal (neither catalog)

| Job ID | Context |
|--------|---------|
| `synthesize_source` | Target file + author synthesis prompt |
| `utility_worker_ping` | Probe string only |

---

## Profile caps (assembler)

Align `UtilityStoryContextProfiles` with this matrix:

| Job ID | MaxTurnPairs | IncludeRollingSummary | IncludeEntityIndex | IncludeState | MaxContextChars |
|--------|-------------:|:---------------------:|:------------------:|:------------:|----------------:|
| `process_turn` | 2 | false | false | false | 8_000 |
| `extract_entities` | 1 | false | compact | false | 8_000 |
| `propose_memories` | 1 | false | false | false | 8_000 |
| `update_summary` | 8 | true | false | false | 12_000 |
| `continuity_check` | 8 | true | true | true | 16_000 |
| `expand_entity` | 0 | false | target | false | 4_000 |
| `update_state` | 2 | false | compact | via SIO | 8_000 |

Canon slice caps: `UtilityCanonSliceProfiles` — continuity 1500 chars inline; extract/process_turn 400–600; expand_entity 900 target-inline.

---

## Scheduler order (auto post-turn)

Settings defaults (new adventures): all auto jobs **on** except `process_turn` (manual only). See [ai-tools-backlog-tracker.md § Staged decisions](ai-tools-backlog-tracker.md#staged-decisions-locked-2026-07-04).

When multiple jobs run on the same turn:

```
extract_entities → propose_memories → update_state* → update_summary (interval) → continuity_check (debounced)
```

\* `update_state` ships in pass-1 (CMD-458); see [update-state-workflow.md](update-state-workflow.md).

`continuity_check` must run **after** siblings settle (injection-first dependency). Skips auto-run when no new **accepted** turn since `LastCheckedAt`. Brief includes pending proposals from earlier jobs in the batch.

---

## Gaps vs code today

**Pass-1 (CMD-449, 2026-07-04):** Play matrix rows above are implemented in code; remaining gaps are design-profile depth and optional v1 `refresh_context_index` LLM patch.

| Gap | Matrix row | Fix doc |
|-----|------------|---------|
| Design jobs lack full assembler profiles | Design table | [design-ai-tools-context.md](design-ai-tools-context.md) |
| `refresh_context_index` v1 (LLM patch) | Optional | [ai-tools-backlog-tracker.md](ai-tools-backlog-tracker.md) |

---

## Sync with utility-job-context-assembly.md

When this matrix changes:

1. Update the table in [utility-job-context-assembly.md](utility-job-context-assembly.md) § Content matrix.  
2. Update `UtilityStoryContextProfiles.cs` and canon slice profiles.  
3. Note change in [ai-tools-backlog-tracker.md](ai-tools-backlog-tracker.md) status log if driven by a new AIT item.

---

*Last updated: 2026-07-04*
