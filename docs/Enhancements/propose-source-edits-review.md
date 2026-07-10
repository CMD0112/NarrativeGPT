# Source edits (`propose_source_edits`) — review

**Status:** Design note (2026-07-04)  
**Index:** [ai-tools-review-index.md](ai-tools-review-index.md)  
**Parent review:** [ai-tools-jobs-review.md](ai-tools-jobs-review.md)  
**Related:** [utility-source-file-io.md](utility-source-file-io.md) · [ai-tools-design-segregation.md](ai-tools-design-segregation.md)

---

## Decision (2026-07-04)

| Topic | Decision |
|-------|----------|
| **Mode** | **Design AI Tools only** — removed from play catalog ([segregation](ai-tools-design-segregation.md)) |
| Job fate | **Keep — refine** |
| Canonical path | Publish → pointer → delimited `source-edits.json` on worker |
| Play entry | **Remove** Play settings “Edit sources with AI”; authors use design workspace |

---

## Role

Propose **markdown edits** to adventure canon sources (`world.md`, `plot.md`, `scenario.md`, `instructions-snippet.md`) — for author review before publish/sync.

**Design-mode only** — pre-play authoring and design-thread context, not post-turn play automation.

Distinct from play jobs:

- `extract_entities` — structured entity proposals from play exchange
- Entity accept → sources sync pipeline — in-session canon without markdown edit job

---

## Current implementation

| Aspect | Today |
|--------|--------|
| **Triggers** | ~~Play AI Tools catalog~~; Play Sources “Edit with AI”; design workspace; source editor synthesize |
| **Routing** | `UtilityJobRouter.DesignJobs` → DesignThread |
| **Dual prompt paths** | **Worker + runId:** `SourceFileRevisionService` (publish/pointer/scrape). **Fallback:** `SourceEditService` inline excerpts (4k cap) |
| **Response** | JSON array `{ targetFile, operation, content, rationale }` |
| **Apply** | `SourceEditReviewQueue` → accept → re-export sources |
| **`cast.md`** | Guide allows; apply returns false |

### Gaps (target)

| Gap | Target |
|-----|--------|
| Play catalog / Sources AI button | **Remove** per segregation |
| `cast.md` in guide | **Remove** — cast via entity workflow / `propose_entities_file` |
| Inline excerpt fallback | **Retire** production path; keep `ForLocalInference` only |
| Play story context in prompt | **Out of scope** — design thread + published sources only |

---

## Decisions (locked 2026-07-04)

| Topic | Decision |
|-------|----------|
| `cast.md` | **Remove** from allowed targets |
| Inline fallback | **Retire** on worker production; keep offline QA / `ForLocalInference` |

See [ai-tools-backlog-tracker.md § Staged decisions](ai-tools-backlog-tracker.md#staged-decisions-locked-2026-07-04).

---

## Prompt architecture (target)

1. Publish core lore files → TASK-SCOPED pointers (`SourceFileRevisionService`).
2. Author prompt from design UI (what to change in world/plot/scenario/instructions).
3. Delimited `source-edits.json` output scrape.
4. Instruction guide: canon-format.md templates; rationale required.

**No** play transcript / rolling summary in packet — design segregation.

---

*Last updated: 2026-07-04*
