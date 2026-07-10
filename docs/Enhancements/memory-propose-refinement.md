# Memory propose — prompt refinement

**Status:** Design note (2026-07-04)  
**Index:** [ai-tools-review-index.md](ai-tools-review-index.md)  
**Parent review:** [ai-tools-jobs-review.md](ai-tools-jobs-review.md)  
**Related:** [memory-update-workflow.md](memory-update-workflow.md) (deferred link/continuation workflow) · [utility-source-file-io.md](utility-source-file-io.md) · [entity-extract-update-workflow.md](entity-extract-update-workflow.md)

---

## User decisions — chat capture (2026-07-04)

| # | Question | Decision |
|---|----------|----------|
| 1 | Refine prompt? | **Yes** — expanded guide + baseline + rubric ([Prompt architecture](#prompt-architecture-target)) |
| 2 | Baseline delivery? | **Hybrid** — inline index default; publish `memory.json` when session large ([Memory baseline](#memory-baseline)) |
| 3 | Update / amend workflow? | **Deferred** — link/continuation model, not in-place edits ([memory-update-workflow.md](memory-update-workflow.md)) |
| 4 | Auto default? | **On** (`AutoProposeMemories = true` for new settings) |
| 5 | Tags / pinned / outcome? | **Keep all** — see [field recommendations](#response-object--field-recommendations); sparse `pinned`, closed tag vocabulary |

---

## Decisions (2026-07-04)

| Topic | Decision |
|-------|----------|
| Job fate | **Keep — refine** prompt and baseline |
| Auto post-turn | **Default on** for new settings (`AutoProposeMemories = true`) |
| P0 response shape | **Append-only** JSON array (new events) |
| Memory updates / links | **Deferred** — see [memory-update-workflow.md](memory-update-workflow.md) |
| Baseline delivery | **Hybrid** — compact inline index in job core; optional input publish of `memory.json` when session is large (see below) |

---

## Role

`propose_memories` records **discrete story events** from the scoped exchange — things that happened — not standing world-model definitions (entities) and not the rolling digest (summary).

---

## Prompt architecture (target)

Split into **instruction guide** (stable methodology) + **job core** (scoped task + baseline + exchange).

### Instruction guide (expand `GenerationJobGuideService`)

```
You propose discrete story events from scoped play context.

Events vs other artifacts:
- Events (this job): things that happened at a point in play — past tense, episodic.
- Entities (other job): durable referents (people, places, concepts) — not play-by-play.
- Summary (other job): rolling digest — not individual event bullets.

Retrieve the memory baseline before proposing (published memory.json or === MEMORY BASELINE ===).
Do not re-propose events already listed in the baseline for the same exchange anchor.
Prefer one concise memory per distinct beat; split only when beats are independently important later.

Respond with JSON only — array of event objects. If nothing worth recording, return [].
```

### Job core sections (in order)

| Section | Content |
|---------|---------|
| `=== MEMORY PROPOSAL JOB ===` | Response contract + pointer to baseline |
| `=== MEMORY BASELINE ===` | Compact index (see below) — or retrieve-only line when publish path used |
| `=== SCOPE ===` | Existing `UtilityTranscriptScopeService.FormatScopeBlock` |
| `=== EXCHANGE ===` | Player/narrator pair when not omitted by dedup |

Dedup with assembler: omit `=== EXCHANGE ===` when story block already contains transcript (`UtilityStoryContextDedup`).

---

## Memory baseline

### What to include

Compact lines the model can scan for duplicates and context:

```
[id] turn:41 | pinned | tags: combat, gate | Greta blocked the party at Greyford Gate.
[id] turn:38 | tags: discovery | Found a rusted key under the altar.
(pending) turn:41 | tags: … | …
```

| Source | Include |
|--------|---------|
| Accepted `Memory.Entries` | Last **N** by recency (e.g. 20), or all within **M** turns of scope target |
| `Memory.ReviewQueue` | Same window — marked `(pending)` so model does not re-propose |
| Pinned memories | Always include if outside recency window (they affect play packets) |

**Do not** inline full `memory.json` in the packet when the document is large.

### Delivery: inline index vs source file publish

| Approach | When | Pros | Cons |
|----------|------|------|------|
| **Inline `=== MEMORY BASELINE ===`** | Default; small/medium sessions | No publish round-trip; always in packet; easy to cap | Token cost grows with session; truncated index may miss old duplicates |
| **Input-only publish `memory.json`** | Large sessions (threshold: e.g. >15 entries or >4k chars) | Authoritative full document; model retrieves via TASK-SCOPED pointer; matches entity job pattern | Requires `UtilitySourceFileIoCatalog` entry + publish plumbing; job core becomes retrieve instruction + minimal index (pinned + pending only) |
| **Hybrid (recommended)** | Production default | Inline compact index for recent + pending + pinned; publish full `memory.json` when over threshold; pointer line says “retrieve for complete history” | Slightly more assembly logic |

**Explanation:** Today the model proposes blind — dedup happens only at apply (`IsDuplicateMemory`). A **baseline** gives the model the same “what’s already recorded” view entities get from `entities.json`, reducing duplicate proposals and improving judgment about what is *new* in this exchange.

Source file publish is not required for P0 if inline index is capped well; add publish when playtests show duplicate proposals or baseline truncation pain.

---

## Response object — field recommendations

### `text` (required)

- One event, past tense, **specific** (who/what/where when known).
- Good: `"Greta refused entry at Greyford Gate until the party produced the writ."`
- Avoid: entity-style definitions (`"Greta is the warden"`) — that belongs in entities.

### `tags` (recommended, light taxonomy)

Use a **small closed vocabulary** in the guide; model may add one freeform tag when needed.

| Tag | Use when |
|-----|----------|
| `discovery` | New information learned |
| `combat` | Fight or violent confrontation |
| `social` | Negotiation, deception, relationship shift |
| `travel` | Movement between places |
| `quest` | Objective advanced or failed |
| `loss` | Death, destruction, item lost |
| `revelation` | Secret exposed |
| `choice` | Player decision with lasting consequence |

**UI today:** tags appear in review list subtitle — keep tags short (1–3 per memory).

**Not for P0:** entity id linking in tags — defer to [memory-update-workflow.md](memory-update-workflow.md).

### `pinned` (sparse)

- `true` only when the event should **always** appear in play packets (`=== PINNED MEMORY ===`).
- Guide: “Pin rarely — author-facing beats that must not fade (vows, deaths, major revelations).”
- Default `false`; most proposals should not be pinned.

### `anchor` (recommended)

- `pairOffset`: 0 for scoped exchange (default).
- `playerHint`: short hint from player line (wrapper can also set from scope).
- Enables `IsDuplicateMemory` and future link workflow.

Prompt: “Always include anchor for transcript-scoped events.”

### `outcome` (optional, high value when short)

- **One line**: consequence or open thread (“Party still lacks the writ”; “Gate remains sealed”).
- **UI today:** used as review preview when present (`FormatMemoryPreview`).
- Guide: “Use when the event implies an unresolved state the narrator should respect.”
- Not a second memory — if the consequence is a separate beat, propose a second memory instead.

---

## What to record — rubric (instruction guide)

**Propose when:**

- A fact was established that may matter later but is not a standing entity definition.
- A commitment, betrayal, promise, or irreversible choice occurred.
- A mystery or clue was introduced.

**Skip when:**

- Pure atmosphere with no lasting fact.
- Already covered in baseline for this anchor (same beat).
- Better captured as an entity create/update (new NPC, location rename).
- Rolling plot summary material only (wait for `update_summary`).

---

## Auto default on

**Decision:** `AutoProposeMemories` defaults **true** for new adventures / reset settings.

**Implementation note:** Set default on `AdventureMetadata.Settings` (or template for new linked projects). Existing adventures keep saved preference until author changes it.

Runs **second** in post-turn scheduler (after extract). `AutoExtractEntities` and `AutoProposeMemories` both default **on** for new settings — see [ai-tools-backlog-tracker.md](ai-tools-backlog-tracker.md).

---

## Code touchpoints (expected)

| Area | File |
|------|------|
| Guide | `GenerationJobGuideService` |
| Job core | `GenerationJobHandlers.BuildScopedMemoryProposalPrompt` |
| Baseline builder | New helper (e.g. `MemoryBaselineService`) |
| Source I/O (optional P1) | `UtilitySourceFileIoCatalog`, publish dispatch for `memory.json` |
| Default | `AdventureMetadata.Settings.AutoProposeMemories` |

---

## Out of scope (P0)

- Dual-section `extractions` + `links` response — [memory-update-workflow.md](memory-update-workflow.md)
- Amending existing memory text in place
- Continuity brief assembly — [continuity-check-redesign.md](continuity-check-redesign.md)

---

*Last updated: 2026-07-04*
